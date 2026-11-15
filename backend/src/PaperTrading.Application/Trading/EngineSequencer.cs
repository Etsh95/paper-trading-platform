using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PaperTrading.Application.Abstractions;
using PaperTrading.Domain;
using PaperTrading.Domain.Matching;

namespace PaperTrading.Application.Trading;

/// <summary>
/// Single-writer front door to the <see cref="MatchingEngine"/>.
/// <para>
/// Every command (order, cancel, price tick) and every read is queued on one channel and executed by one
/// consumer loop. This is how concurrent orders are handled correctly: there is no shared mutable state touched
/// by more than one thread, so there is nothing to lock and no interleaving to reason about.
/// </para>
/// Operations passed to <see cref="ExecuteAsync{T}"/> must return immutable data (snapshots), never live entities.
/// </summary>
internal sealed class EngineSequencer : IAsyncDisposable
{
    private readonly MatchingEngine _engine;
    private readonly IEngineEventSink? _sink;
    private readonly ILogger _logger;
    private readonly Channel<WorkItem> _queue;
    private readonly Task _loop;

    public EngineSequencer(MatchingEngine engine, IEngineEventSink? sink = null, ILogger<EngineSequencer>? logger = null, int capacity = 10_000)
    {
        _engine = engine;
        _sink = sink;
        _logger = logger ?? NullLogger<EngineSequencer>.Instance;
        _queue = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait, // back-pressure instead of dropping orders or ticks
        });
        _loop = Task.Factory.StartNew(RunAsync, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    /// <summary>Number of queued work items; useful as a health/lag signal.</summary>
    public int QueueLength => _queue.Reader.Count;

    public async Task<T> ExecuteAsync<T>(Func<MatchingEngine, T> operation, CancellationToken cancellationToken = default)
    {
        var item = new WorkItem<T>(operation);
        await _queue.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        return await item.Completion.ConfigureAwait(false);
    }

    /// <summary>Queues a price tick. Completes once queued, not once applied.</summary>
    public ValueTask PublishQuoteAsync(Quote quote, CancellationToken cancellationToken = default) =>
        _queue.Writer.WriteAsync(new QuoteItem(quote), cancellationToken);

    private async Task RunAsync()
    {
        await foreach (var item in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                item.Execute(_engine);
            }
            catch (Exception ex)
            {
                // Engine methods validate before mutating, so an exception here indicates a bug.
                // In production this should fail fast and recover from a command journal (see README).
                _logger.LogCritical(ex, "Engine operation failed");
                item.Fail(ex);
            }

            var events = _engine.DrainEvents();
            if (events.Count == 0 || _sink is null) continue;
            try
            {
                _sink.Publish(events);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event sink failed; {Count} events dropped", events.Count);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _loop.ConfigureAwait(false);
    }

    private abstract class WorkItem
    {
        public abstract void Execute(MatchingEngine engine);
        public virtual void Fail(Exception exception) { }
    }

    private sealed class WorkItem<T>(Func<MatchingEngine, T> operation) : WorkItem
    {
        // RunContinuationsAsynchronously: callers' continuations must never run on (and stall) the engine loop.
        private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Completion => _tcs.Task;

        public override void Execute(MatchingEngine engine) => _tcs.TrySetResult(operation(engine));

        public override void Fail(Exception exception) => _tcs.TrySetException(exception);
    }

    private sealed class QuoteItem(Quote quote) : WorkItem
    {
        public override void Execute(MatchingEngine engine) => engine.ApplyQuote(quote);
    }
}
