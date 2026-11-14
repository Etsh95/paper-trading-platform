using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PaperTrading.Api.Infrastructure;
using PaperTrading.Api.MarketData;
using PaperTrading.Core.Domain;
using PaperTrading.Core.Engine;

namespace PaperTrading.Api.Tests;

/// <summary>Quote source the tests drive by hand, so prices are deterministic.</summary>
public sealed class ManualQuoteSource : IQuoteSource
{
    private readonly Channel<Quote> _quotes = Channel.CreateUnbounded<Quote>();

    public string Name => "Manual";

    public void Push(Quote quote) => _quotes.Writer.TryWrite(quote);

    public async IAsyncEnumerable<Quote> StreamAsync(IReadOnlyList<CatalogEntry> instruments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var q in _quotes.Reader.ReadAllAsync(cancellationToken)) yield return q;
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public ManualQuoteSource Quotes { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("MarketData:Provider", "Manual");
        builder.UseSetting("RateLimiting:SessionsPerMinutePerIp", "10000");
        builder.UseSetting("RateLimiting:OrdersPerSecondPerAccount", "10000");
        builder.UseSetting("RateLimiting:OrderBurstPerAccount", "10000");
        builder.ConfigureTestServices(s => s.AddSingleton<IQuoteSource>(Quotes));
    }

    /// <summary>Pushes a quote and waits until the engine has applied it.</summary>
    public async Task SetQuoteAsync(string symbol, decimal bid, decimal ask)
    {
        var quote = new Quote(symbol, bid, ask, DateTimeOffset.UtcNow);
        Quotes.Push(quote);
        var engine = Services.GetRequiredService<EngineSequencer>();
        for (var i = 0; i < 200; i++)
        {
            if (await engine.ExecuteAsync(e => e.GetQuote(symbol)) == quote) return;
            await Task.Delay(10);
        }
        throw new TimeoutException("Quote was not applied by the engine.");
    }

    /// <summary>Creates a session and returns a client authorized as that account.</summary>
    public async Task<(HttpClient Client, Guid AccountId, string Token)> CreateTraderAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/api/sessions", null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, body.GetProperty("accountId").GetGuid(), token);
    }
}

public static class HttpClientExtensions
{
    public static Task<HttpResponseMessage> PlaceOrderAsync(this HttpClient client, object body, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
