using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaperTrading.Application.Abstractions;
using PaperTrading.Application.MarketData;
using PaperTrading.Application.Trading;
using PaperTrading.Domain;
using PaperTrading.Domain.Matching;
using PaperTrading.Infrastructure.Instruments;
using PaperTrading.Infrastructure.MarketData;

namespace PaperTrading.UnitTests.Infrastructure;

public class MarketDataTests
{
    [Fact]
    public async Task Mock_feed_continues_from_the_last_real_price_instead_of_jumping_to_its_reference()
    {
        var cache = new QuoteCache();
        cache.Update(new Quote("BTCUSDT", 64_000m, 64_000.02m, DateTimeOffset.UtcNow), "Binance");
        var options = Options.Create(new MarketDataOptions { MockInterval = TimeSpan.FromMilliseconds(10) });
        var catalog = new StaticInstrumentCatalog();
        var mock = new MockQuoteSource(options, TimeProvider.System, cache, catalog);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var first = await mock.StreamAsync(catalog.Instruments, cts.Token).FirstAsync(q => q.Symbol == "BTCUSDT", cts.Token);

        // Reference price for BTC in the catalog is 95,000; a failover must not jump there.
        Assert.InRange(first.Mid, 64_000m * 0.99m, 64_000m * 1.01m);
        Assert.True(first.IsValid);
    }

    [Fact]
    public async Task Mock_feed_keeps_at_least_the_live_spread_on_failover()
    {
        // A wide live market: resting buy limits inside it must not be filled by a tighter simulated spread.
        var cache = new QuoteCache();
        cache.Update(new Quote("BTCUSDT", 50_000m, 51_000m, DateTimeOffset.UtcNow), "Binance");
        var catalog = new StaticInstrumentCatalog();
        var mock = new MockQuoteSource(Options.Create(new MarketDataOptions { MockInterval = TimeSpan.FromMilliseconds(10) }), TimeProvider.System, cache, catalog);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var first = await mock.StreamAsync(catalog.Instruments, cts.Token).FirstAsync(q => q.Symbol == "BTCUSDT", cts.Token);

        Assert.True(first.Ask - first.Bid >= 1_000m, $"spread {first.Ask - first.Bid} is tighter than the live 1,000");
    }

    [Fact]
    public async Task Ingestor_drops_invalid_quotes_and_feeds_valid_ones_to_engine_and_cache()
    {
        var engine = new MatchingEngine(new StaticInstrumentCatalog().Instruments, new TradingOptions());
        await using var sequencer = new EngineSequencer(engine);
        var cache = new QuoteCache();
        var ingestor = new MarketDataIngestor(sequencer, cache);
        var trading = new TradingService(sequencer);

        await ingestor.PublishAsync(new Quote("BTCUSDT", 10m, 9m, DateTimeOffset.UtcNow), "Test", CancellationToken.None); // crossed
        var valid = new Quote("BTCUSDT", 10m, 11m, DateTimeOffset.UtcNow);
        await ingestor.PublishAsync(valid, "Test", CancellationToken.None);

        Assert.Equal(valid, await trading.GetExecutionQuoteAsync("BTCUSDT"));
        Assert.Equal(valid, Assert.Single(cache.All));
    }

    [Fact]
    public async Task Binance_message_parsing_is_exact_decimal()
    {
        var json = """{"stream":"btcusdt@bookTicker","data":{"u":1,"s":"BTCUSDT","b":"64342.01000000","B":"1.2","a":"64342.02000000","A":"0.5"}}"""u8;

        Assert.True(BinanceQuoteSource.TryParse(json, DateTimeOffset.UtcNow, out var quote));
        Assert.Equal(64_342.01m, quote.Bid);
        Assert.Equal(64_342.02m, quote.Ask);
        Assert.False(BinanceQuoteSource.TryParse("""{"data":{"s":"BTCUSDT","b":"1","a":"0.5"}}"""u8, DateTimeOffset.UtcNow, out _)); // crossed
        Assert.False(BinanceQuoteSource.TryParse("not json"u8, DateTimeOffset.UtcNow, out _));
        // Unexpected shapes are rejected, not thrown on:
        Assert.False(BinanceQuoteSource.TryParse("[1,2,3]"u8, DateTimeOffset.UtcNow, out _));
        Assert.False(BinanceQuoteSource.TryParse("""{"data":"oops"}"""u8, DateTimeOffset.UtcNow, out _));
        Assert.False(BinanceQuoteSource.TryParse("""{"data":{"s":42,"b":"1","a":"2"}}"""u8, DateTimeOffset.UtcNow, out _));
        Assert.False(BinanceQuoteSource.TryParse("""{"data":{"s":"BTCUSDT","b":1,"a":2}}"""u8, DateTimeOffset.UtcNow, out _));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Service_fails_over_to_mock_and_later_retries_the_primary()
    {
        var primary = new FailingSource("Binance");
        var fallback = new CountingSource("Mock");
        var options = Options.Create(new MarketDataOptions
        {
            Provider = "Binance",
            FailuresBeforeFallback = 1,
            RetryPrimaryAfter = TimeSpan.FromMilliseconds(300),
        });
        var catalog = new StaticInstrumentCatalog();
        var engine = new MatchingEngine(catalog.Instruments, new TradingOptions());
        await using var sequencer = new EngineSequencer(engine);
        var ingestor = new MarketDataIngestor(sequencer, new QuoteCache());
        var service = new MarketDataService([primary, fallback], options, ingestor, catalog, TimeProvider.System, NullLogger<MarketDataService>.Instance);

        await service.StartAsync(CancellationToken.None);
        // Wait for both: on a busy machine the first 300 ms fallback window can close before the mock's first tick,
        // and the service then simply falls back again after the next primary failure.
        await WaitUntil(() => primary.Attempts >= 2 && fallback.Served > 0, TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        Assert.True(fallback.Served > 0, "fallback should have served quotes");
        Assert.True(primary.Attempts >= 2, "primary should have been retried after RetryPrimaryAfter");
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
    }

    private sealed class FailingSource(string name) : IQuoteSource
    {
        private int _attempts;
        public string Name => name;
        public int Attempts => Volatile.Read(ref _attempts);

        public async IAsyncEnumerable<Quote> StreamAsync(IReadOnlyList<Instrument> instruments, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            await Task.Yield();
            throw new InvalidOperationException("unreachable");
#pragma warning disable CS0162 // iterator needs a yield
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class CountingSource(string name) : IQuoteSource
    {
        private int _served;
        public string Name => name;
        public int Served => Volatile.Read(ref _served);

        public async IAsyncEnumerable<Quote> StreamAsync(IReadOnlyList<Instrument> instruments, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (true)
            {
                await Task.Delay(20, cancellationToken);
                Interlocked.Increment(ref _served);
                yield return new Quote("BTCUSDT", 100m, 100.01m, DateTimeOffset.UtcNow);
            }
        }
    }
}
