namespace PaperTrading.Api.MarketData;

public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    /// <summary>"Binance" (live public WebSocket) or "Mock" (generated random walk).</summary>
    public string Provider { get; set; } = "Binance";

    /// <summary>Market-data-only Binance endpoint; no API key needed.</summary>
    public string BinanceStreamUrl { get; set; } = "wss://data-stream.binance.vision";

    /// <summary>Switch to the mock feed after repeated failures (e.g. Binance unreachable from the hosting region).</summary>
    public bool FallbackToMock { get; set; } = true;

    public int FailuresBeforeFallback { get; set; } = 3;

    /// <summary>Give up on a connection attempt after this long.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>While on the fallback feed, try the primary again this often.</summary>
    public TimeSpan RetryPrimaryAfter { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Reconnect if the socket goes silent for this long.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan MockInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How often coalesced quotes are pushed to browsers.</summary>
    public TimeSpan BroadcastInterval { get; set; } = TimeSpan.FromMilliseconds(250);
}
