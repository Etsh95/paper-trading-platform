using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using PaperTrading.Api.Auth;
using PaperTrading.Api.Endpoints;
using PaperTrading.Api.Health;
using PaperTrading.Api.Realtime;
using PaperTrading.Api.Security;
using PaperTrading.Api.Serialization;
using PaperTrading.Application.Abstractions;
using PaperTrading.Application.Trading;

namespace PaperTrading.Api;

/// <summary>Presentation-layer services: HTTP, SignalR, auth, CORS, rate limits, OpenAPI, health.</summary>
public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));

        // Money is serialized as strings; enums as names only.
        services.ConfigureHttpJsonOptions(o => ConfigureJson(o.SerializerOptions));
        services.AddSignalR().AddJsonProtocol(o => ConfigureJson(o.PayloadSerializerOptions));

        // Real-time: engine events -> account groups; quotes -> everyone, coalesced.
        services.AddSingleton(new RealtimeOptions());
        services.AddSingleton<SignalREventSink>();
        services.AddSingleton<IEngineEventSink>(sp => sp.GetRequiredService<SignalREventSink>());
        services.AddHostedService(sp => sp.GetRequiredService<SignalREventSink>());
        services.AddHostedService<QuoteBroadcaster>();

        services.AddSingleton<TokenService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TokenService>((o, tokens) =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = tokens.ValidationParameters();
                o.Events = new JwtBearerEvents
                {
                    // Accounts live in memory but the signing key survives restarts, so a valid token can outlive
                    // its account. Reject it (401): the client then starts a new session and tells the user why,
                    // rather than every endpoint quietly creating an empty account under the old id.
                    OnTokenValidated = async ctx =>
                    {
                        var trading = ctx.HttpContext.RequestServices.GetRequiredService<ITradingService>();
                        if (!Guid.TryParse(ctx.Principal?.FindFirst("sub")?.Value, out var accountId) ||
                            !await trading.AccountExistsAsync(accountId, ctx.HttpContext.RequestAborted))
                            ctx.Fail("Account no longer exists.");
                    },
                    // Browsers can't set headers on WebSocket upgrades, so SignalR sends the token in the query string.
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(MarketHub.Path))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    },
                };
            });
        services.AddAuthorization();
        services.AddPaperTradingRateLimits();

        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders("Location", OrderEndpoints.ReplayedHeader)));

        // Client IPs (used for rate limiting) come from X-Forwarded-For only when explicitly configured to trust the
        // proxy in front. On Azure Container Apps the platform ingress is the only way in, so it's enabled there.
        // Anywhere the API port is reachable directly, trusting the header would let clients pick their own IP.
        if (configuration.GetValue<bool>("ForwardedHeaders:TrustProxy"))
        {
            services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                // Only the right-most X-Forwarded-For entry is used: the one the trusted proxy appended. Anything a
                // client put to its left is ignored, so it can't choose its own rate-limit identity. Explicit (rather
                // than relying on the default) and covered by a test, since raising it would make IPs spoofable.
                o.ForwardLimit = 1;
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            });
        }

        services.AddProblemDetails();
        services.AddOpenApi();
        services.AddHealthChecks().AddCheck<MarketDataHealthCheck>("market-data", tags: ["ready"]);
        return services;
    }

    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.Converters.Add(new DecimalStringJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    }
}
