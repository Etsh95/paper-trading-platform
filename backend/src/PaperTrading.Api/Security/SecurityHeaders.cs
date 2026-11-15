namespace PaperTrading.Api.Security;

/// <summary>
/// Hardening headers on every API response. The API only returns JSON, but these keep any future endpoint safe
/// by default: no MIME sniffing, HTTPS-only, no framing, no referrer leakage, and no caching of per-account data.
/// </summary>
public static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
            if (context.Request.Path.StartsWithSegments("/api"))
                headers.CacheControl = "no-store";
            return next(context);
        });
}
