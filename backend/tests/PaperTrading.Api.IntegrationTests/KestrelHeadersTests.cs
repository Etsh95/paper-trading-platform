using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PaperTrading.Api.IntegrationTests;

/// <summary>
/// The in-memory test server never sends a <c>Server</c> header, so the banner check needs the real Kestrel server.
/// </summary>
public class KestrelHeadersTests : IClassFixture<KestrelHeadersTests.KestrelFactory>
{
    private readonly KestrelFactory _factory;

    public KestrelHeadersTests(KestrelFactory factory) => _factory = factory;

    public sealed class KestrelFactory : WebApplicationFactory<Program>
    {
        public KestrelFactory() => UseKestrel(0); // any free port

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("MarketData:Provider", "Mock");
        }
    }

    [Fact]
    public async Task Kestrel_does_not_announce_itself()
    {
        _factory.StartServer();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.EnsureSuccessStatusCode();
        Assert.False(response.Headers.Contains("Server"), string.Join(", ", response.Headers.Select(h => h.Key)));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }
}
