using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PaperTrading.Api.IntegrationTests;

/// <summary>At the account cap, sign-up fails with a code the frontend can explain (it has a translation for it).</summary>
public class CapacityTests : IClassFixture<CapacityTests.SmallFactory>
{
    private readonly SmallFactory _factory;

    public CapacityTests(SmallFactory factory) => _factory = factory;

    public sealed class SmallFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("MarketData:Provider", "Mock");
            builder.UseSetting("Trading:MaxAccounts", "1");
        }
    }

    [Fact]
    public async Task Sign_up_beyond_capacity_is_a_coded_503()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/sessions", null)).StatusCode);
        var refused = await client.PostAsync("/api/sessions", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal("capacity_reached", (await refused.JsonAsync()).GetProperty("code").GetString());
    }
}
