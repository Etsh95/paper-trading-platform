using System.Security.Claims;

namespace PaperTrading.Api.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The account id is the JWT subject. Endpoints using this require authorization, so it is always present.</summary>
    public static Guid AccountId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out var id)
            ? id
            : throw new InvalidOperationException("Authenticated principal has no valid 'sub' claim.");
}
