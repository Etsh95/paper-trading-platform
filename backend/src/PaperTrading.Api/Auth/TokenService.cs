using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace PaperTrading.Api.Auth;

/// <summary>Issues short-lived anonymous session tokens; the JWT subject is the paper account id.</summary>
public sealed class TokenService
{
    private readonly AuthOptions _options;
    private readonly TimeProvider _time;
    private readonly JsonWebTokenHandler _handler = new();

    public TokenService(IOptions<AuthOptions> options, TimeProvider time, ILogger<TokenService> logger)
    {
        _options = options.Value;
        _time = time;

        byte[] keyBytes;
        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            // Acceptable because account state is in-memory too: a restart invalidates both together.
            logger.LogWarning("Auth:SigningKey not configured; using an ephemeral key. Tokens will not survive a restart.");
            keyBytes = RandomNumberGenerator.GetBytes(64);
        }
        else
        {
            keyBytes = Convert.FromBase64String(_options.SigningKey);
            if (keyBytes.Length < 32) throw new InvalidOperationException("Auth:SigningKey must be at least 32 bytes.");
        }

        SigningKey = new SymmetricSecurityKey(keyBytes);
    }

    public SecurityKey SigningKey { get; }

    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid accountId)
    {
        var now = _time.GetUtcNow();
        var expires = now + _options.TokenLifetime;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity([new Claim("sub", accountId.ToString())]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });
        return (token, expires);
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = _options.Issuer,
        ValidAudience = _options.Audience,
        IssuerSigningKey = SigningKey,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "sub",
    };
}
