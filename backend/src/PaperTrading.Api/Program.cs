using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PaperTrading.Api.Auth;
using PaperTrading.Api.Endpoints;
using PaperTrading.Api.Infrastructure;
using PaperTrading.Api.MarketData;
using PaperTrading.Api.Realtime;
using PaperTrading.Core.Engine;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------- options
builder.Services.Configure<TradingOptions>(config.GetSection(TradingOptions.SectionName));
builder.Services.Configure<MarketDataOptions>(config.GetSection(MarketDataOptions.SectionName));
builder.Services.Configure<AuthOptions>(config.GetSection(AuthOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(config.GetSection(RateLimitOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);

// ---------------------------------------------------------------- engine (single writer)
builder.Services.AddSingleton(sp => new MatchingEngine(
    InstrumentCatalog.Default.Select(e => e.Instrument),
    sp.GetRequiredService<IOptions<TradingOptions>>().Value,
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<SignalREventSink>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SignalREventSink>());
builder.Services.AddSingleton(sp => new EngineSequencer(
    sp.GetRequiredService<MatchingEngine>(),
    sp.GetRequiredService<SignalREventSink>(),
    sp.GetRequiredService<ILogger<EngineSequencer>>()));

// ---------------------------------------------------------------- market data
builder.Services.AddSingleton<QuoteCache>();
builder.Services.AddSingleton<IQuoteSource, BinanceQuoteSource>();
builder.Services.AddSingleton<IQuoteSource, MockQuoteSource>();
builder.Services.AddHostedService<MarketDataService>();
builder.Services.AddHostedService<QuoteBroadcaster>();

// ---------------------------------------------------------------- serialization: money as strings
void ConfigureJson(System.Text.Json.JsonSerializerOptions o)
{
    o.Converters.Add(new DecimalStringJsonConverter());
    o.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
}
builder.Services.ConfigureHttpJsonOptions(o => ConfigureJson(o.SerializerOptions));
builder.Services.AddSignalR().AddJsonProtocol(o => ConfigureJson(o.PayloadSerializerOptions));

// ---------------------------------------------------------------- auth
builder.Services.AddSingleton<TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
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
builder.Services.AddAuthorization();
builder.Services.AddPaperTradingRateLimits();

// ---------------------------------------------------------------- web plumbing
var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithExposedHeaders("Location", OrderEndpoints.ReplayedHeader)));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Behind Azure Container Apps ingress, the only path in is the platform proxy.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<MarketDataHealthCheck>("market-data", tags: ["ready"]);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference("/docs");

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new()
{
    Predicate = c => c.Tags.Contains("ready"),
    ResultStatusCodes = { [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable },
});

app.MapGet("/", () => Results.Redirect("/docs")).ExcludeFromDescription();
app.MapSessionEndpoints();
app.MapMarketEndpoints();
app.MapOrderEndpoints();
app.MapPortfolioEndpoints();
app.MapHub<MarketHub>(MarketHub.Path);

app.Run();

public partial class Program;
