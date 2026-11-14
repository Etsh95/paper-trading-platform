namespace PaperTrading.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Base64 HMAC-SHA256 key (at least 32 bytes). Supplied via environment / secret store
    /// (<c>Auth__SigningKey</c>), never committed. If absent, a random per-process key is generated.
    /// </summary>
    public string? SigningKey { get; set; }

    public string Issuer { get; set; } = "paper-trading-api";
    public string Audience { get; set; } = "paper-trading-client";
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromDays(1);
}
