using Microsoft.Extensions.Diagnostics.HealthChecks;
using PaperTrading.Api;
using PaperTrading.Api.Endpoints;
using PaperTrading.Api.Realtime;
using PaperTrading.Application;
using PaperTrading.Infrastructure;
using Scalar.AspNetCore;

// Composition root: the only place that knows about every layer.
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication(builder.Configuration)      // engine, use cases, market-data ingestion
    .AddInfrastructure(builder.Configuration)   // instrument catalog, Binance/mock feeds
    .AddApi(builder.Configuration);             // HTTP, SignalR, auth, CORS, rate limits

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
