using System.Buffers;
using System.Globalization;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PaperTrading.Api.Infrastructure;
using PaperTrading.Core.Domain;

namespace PaperTrading.Api.MarketData;

/// <summary>
/// Binance public combined stream of <c>@bookTicker</c> (best bid/ask). No API key required.
/// Prices arrive as JSON strings and are parsed straight to <see cref="decimal"/>; no floating point involved.
/// </summary>
public sealed class BinanceQuoteSource(IOptions<MarketDataOptions> options, TimeProvider time, ILogger<BinanceQuoteSource> logger) : IQuoteSource
{
    public string Name => "Binance";

    public async IAsyncEnumerable<Quote> StreamAsync(IReadOnlyList<CatalogEntry> instruments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var streams = string.Join('/', instruments.Select(e => $"{e.Instrument.Symbol.ToLowerInvariant()}@bookTicker"));
        var uri = new Uri($"{settings.BinanceStreamUrl.TrimEnd('/')}/stream?streams={streams}");

        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        // Linked token doubles as an idle watchdog: if nothing arrives for IdleTimeout, the receive is cancelled.
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        watchdog.CancelAfter(settings.IdleTimeout);

        await socket.ConnectAsync(uri, watchdog.Token);
        logger.LogInformation("Connected to Binance stream for {Count} symbols", instruments.Count);

        var buffer = new ArrayBufferWriter<byte>(4096);
        while (socket.State == WebSocketState.Open)
        {
            buffer.ResetWrittenCount();
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.GetMemory(4096), watchdog.Token);
                buffer.Advance(result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close) yield break;
            watchdog.CancelAfter(settings.IdleTimeout);

            if (TryParse(buffer.WrittenSpan, time.GetUtcNow(), out var quote)) yield return quote;
        }
    }

    /// <summary>Parses <c>{"stream":"btcusdt@bookTicker","data":{"s":"BTCUSDT","b":"1.0","a":"1.1",...}}</c>.</summary>
    public static bool TryParse(ReadOnlySpan<byte> json, DateTimeOffset receivedAt, out Quote quote)
    {
        quote = null!;
        try
        {
            var reader = new Utf8JsonReader(json);
            using var doc = JsonDocument.ParseValue(ref reader);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return false;

            if (data.TryGetProperty("s", out var s) && s.GetString() is { Length: > 0 } symbol &&
                TryDecimal(data, "b", out var bid) && TryDecimal(data, "a", out var ask))
            {
                quote = new Quote(symbol, bid, ask, receivedAt);
                return quote.IsValid;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryDecimal(JsonElement element, string name, out decimal value)
    {
        value = 0;
        return element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String &&
               decimal.TryParse(p.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
