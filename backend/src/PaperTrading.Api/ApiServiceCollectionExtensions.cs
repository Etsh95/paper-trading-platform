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

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            // The API is reachable only through the reverse proxy in front of it.
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

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
