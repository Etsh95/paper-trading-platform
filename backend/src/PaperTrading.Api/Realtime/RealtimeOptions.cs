namespace PaperTrading.Api.Realtime;

public sealed class RealtimeOptions
{
    /// <summary>How often coalesced quotes are pushed to browsers.</summary>
    public TimeSpan BroadcastInterval { get; init; } = TimeSpan.FromMilliseconds(250);
}
