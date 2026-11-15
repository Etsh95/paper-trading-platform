using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PaperTrading.Api.IntegrationTests;

/// <summary>Behind a trusted proxy, a client must not be able to pick its own rate-limit identity.</summary>
public class ForwardedHeadersTests : IClassFixture<ForwardedHeadersTests.TrustedProxyFactory>
{
    private readonly TrustedProxyFactory _factory;

    public ForwardedHeadersTests(TrustedProxyFactory factory) => _factory = factory;

    public sealed class TrustedProxyFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("MarketData:Provider", "Mock");
            builder.UseSetting("ForwardedHeaders:TrustProxy", "true");
            builder.UseSetting("RateLimiting:SessionsPerMinutePerIp", "2");
        }
    }

    [Fact]
    public async Task Spoofed_left_most_forwarded_address_does_not_bypass_the_rate_limit()
    {
        var client = _factory.CreateClient();
        async Task<HttpStatusCode> CreateSession(string forwardedFor)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/sessions");
            request.Headers.Add("X-Forwarded-For", forwardedFor);
            return (await client.SendAsync(request)).StatusCode;
        }

        // The proxy appends the real client address (10.0.0.1) on the right; the client controls the rest.
        Assert.Equal(HttpStatusCode.Created, await CreateSession("1.1.1.1, 10.0.0.1"));
        Assert.Equal(HttpStatusCode.Created, await CreateSession("2.2.2.2, 10.0.0.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await CreateSession("3.3.3.3, 10.0.0.1"));
    }
}
