using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using PaperTrading.Api.Serialization;

namespace PaperTrading.Api.IntegrationTests;

public class OrdersApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly object MarketBuy = new { symbol = "BTCUSDT", side = "Buy", type = "Market", quantity = "0.1" };

    [Fact]
    public async Task New_session_gets_initial_balance_with_money_serialized_as_strings()
    {
        var (client, _, _) = await factory.CreateTraderAsync();

        var portfolio = await (await client.GetAsync("/api/portfolio")).JsonAsync();

        Assert.Equal(JsonValueKind.String, portfolio.GetProperty("cash").ValueKind);
        Assert.Equal("100000", portfolio.GetProperty("cash").GetString());
        Assert.Equal("0", portfolio.GetProperty("realizedPnl").GetString());
    }

    [Fact]
    public async Task Order_endpoints_require_authentication()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PlaceOrderAsync(MarketBuy, "k")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/portfolio")).StatusCode);
    }

    [Fact]
    public async Task Missing_idempotency_key_is_a_bad_request()
    {
        var (client, _, _) = await factory.CreateTraderAsync();

        var response = await client.PlaceOrderAsync(MarketBuy);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_required", (await response.JsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Market_buy_fills_at_ask_and_returns_created()
    {
        await factory.SetQuoteAsync("BTCUSDT", 50_000m, 50_010m);
        var (client, _, _) = await factory.CreateTraderAsync();

        var response = await client.PlaceOrderAsync(MarketBuy, Guid.NewGuid().ToString());
        var order = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal("Filled", order.GetProperty("status").GetString());
        Assert.Equal("50010", order.GetProperty("fillPrice").GetString());
        Assert.Equal("5.001", order.GetProperty("fee").GetString());

        var portfolio = await (await client.GetAsync("/api/portfolio")).JsonAsync();
        Assert.Equal("94993.999", portfolio.GetProperty("cash").GetString());
    }

    [Fact]
    public async Task Order_responses_expose_the_contract_not_engine_internals()
    {
        await factory.SetQuoteAsync("BTCUSDT", 50_000m, 50_010m);
        var (client, _, _) = await factory.CreateTraderAsync();

        var order = await (await client.PlaceOrderAsync(MarketBuy, Guid.NewGuid().ToString())).JsonAsync();
        var fields = order.EnumerateObject().Select(p => p.Name).ToHashSet();

        Assert.Contains("id", fields);
        Assert.Contains("fillPrice", fields);
        Assert.DoesNotContain("accountId", fields);
        Assert.DoesNotContain("sequence", fields);
        Assert.DoesNotContain("idempotencyKey", fields);
    }

    [Fact]
    public async Task Retrying_with_the_same_key_replays_instead_of_buying_twice()
    {
        await factory.SetQuoteAsync("BTCUSDT", 50_000m, 50_010m);
        var (client, _, _) = await factory.CreateTraderAsync();
        var key = Guid.NewGuid().ToString();

        var first = await client.PlaceOrderAsync(MarketBuy, key);
        var second = await client.PlaceOrderAsync(MarketBuy, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal((await first.JsonAsync()).GetProperty("id").GetGuid(), (await second.JsonAsync()).GetProperty("id").GetGuid());
        var trades = await (await client.GetAsync("/api/trades")).JsonAsync();
        Assert.Equal(1, trades.GetArrayLength());
    }

    [Fact]
    public async Task Concurrent_retries_with_the_same_key_create_one_order()
    {
        await factory.SetQuoteAsync("BTCUSDT", 50_000m, 50_010m);
        var (client, _, _) = await factory.CreateTraderAsync();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => client.PlaceOrderAsync(MarketBuy, key)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(29, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var ids = await Task.WhenAll(responses.Select(async r => (await r.JsonAsync()).GetProperty("id").GetGuid()));
        Assert.Single(ids.Distinct());
    }

    [Fact]
    public async Task Same_key_with_a_different_body_is_a_conflict()
    {
        await factory.SetQuoteAsync("BTCUSDT", 50_000m, 50_010m);
        var (client, _, _) = await factory.CreateTraderAsync();
        var key = Guid.NewGuid().ToString();
        await client.PlaceOrderAsync(MarketBuy, key);

        var response = await client.PlaceOrderAsync(new { symbol = "BTCUSDT", side = "Buy", type = "Market", quantity = "0.2" }, key);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Business_rejection_is_422_with_a_stable_code_and_is_kept_in_history()
    {
        await factory.SetQuoteAsync("BTCUSDT", 50_000m, 50_010m);
        var (client, _, _) = await factory.CreateTraderAsync();

        var response = await client.PlaceOrderAsync(new { symbol = "BTCUSDT", side = "Buy", type = "Market", quantity = "5" }, "k");
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("insufficient_funds", problem.GetProperty("code").GetString());
        Assert.Equal("Rejected", problem.GetProperty("order").GetProperty("status").GetString());
        var history = await (await client.GetAsync("/api/orders?status=rejected")).JsonAsync();
        Assert.Equal(1, history.GetArrayLength());
    }

    [Theory]
    [InlineData("""{"symbol":"BTCUSDT","side":"Buy","type":"Market","quantity":"0.000001"}""", "invalid_quantity")]
    [InlineData("""{"symbol":"NOPE","side":"Buy","type":"Market","quantity":"1"}""", "unknown_symbol")]
    [InlineData("""{"symbol":"BTCUSDT","side":"Buy","type":"Limit","quantity":"0.1"}""", "limit_price_required")]
    [InlineData("""{"symbol":"BTCUSDT","side":"Buy","type":"Limit","quantity":"0.1","limitPrice":"1.001"}""", "invalid_price")]
    public async Task Invalid_orders_are_400_with_a_code(string json, string code)
    {
        var (client, _, _) = await factory.CreateTraderAsync();

        var response = await PostRawAsync(client, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.JsonAsync()).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("""{"symbol":"BTCUSDT","side":0,"type":"Market","quantity":"0.1"}""")] // numeric enums refused
    [InlineData("""{"symbol":"BTCUSDT","side":"Buy","type":"Market"}""")]               // missing quantity
    [InlineData("""{"symbol":"BTCUSDT","side":"Buy","type":"Market","quantity":"1e3"}""")] // no exponents
    public async Task Malformed_bodies_are_rejected(string json)
    {
        var (client, _, _) = await factory.CreateTraderAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await PostRawAsync(client, json)).StatusCode);
    }

    [Fact]
    public async Task Limit_order_rests_then_fills_when_price_crosses()
    {
        await factory.SetQuoteAsync("ETHUSDT", 3_000m, 3_000.5m);
        var (client, _, _) = await factory.CreateTraderAsync();

        var placed = await client.PlaceOrderAsync(new { symbol = "ETHUSDT", side = "Buy", type = "Limit", quantity = "1", limitPrice = "2900" }, "k");
        var placedOrder = await placed.JsonAsync();
        var id = placedOrder.GetProperty("id").GetGuid();
        Assert.Equal("Open", placedOrder.GetProperty("status").GetString());
        Assert.Equal("2902.9", (await (await client.GetAsync("/api/portfolio")).JsonAsync()).GetProperty("reservedCash").GetString());

        await factory.SetQuoteAsync("ETHUSDT", 2_899m, 2_900m);

        var order = await (await client.GetAsync($"/api/orders/{id}")).JsonAsync();
        Assert.Equal("Filled", order.GetProperty("status").GetString());
        Assert.Equal("2900", order.GetProperty("fillPrice").GetString());
    }

    [Fact]
    public async Task Cancel_open_order_then_cancel_again_is_conflict()
    {
        var (client, _, _) = await factory.CreateTraderAsync();
        var placed = await client.PlaceOrderAsync(new { symbol = "SOLUSDT", side = "Buy", type = "Limit", quantity = "1", limitPrice = "10" }, "k");
        var id = (await placed.JsonAsync()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/orders/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/orders/{id}")).StatusCode);
        Assert.Equal("0", (await (await client.GetAsync("/api/portfolio")).JsonAsync()).GetProperty("reservedCash").GetString());
    }

    [Fact]
    public async Task Accounts_cannot_see_or_cancel_each_others_orders()
    {
        var (alice, _, _) = await factory.CreateTraderAsync();
        var (bob, _, _) = await factory.CreateTraderAsync();
        var placed = await alice.PlaceOrderAsync(new { symbol = "SOLUSDT", side = "Buy", type = "Limit", quantity = "1", limitPrice = "10" }, "k");
        var id = (await placed.JsonAsync()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/orders/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/orders/{id}")).StatusCode);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var (client, _, token) = await factory.CreateTraderAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token[..^4] + "AAAA");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/portfolio")).StatusCode);
    }

    [Fact]
    public async Task SignalR_pushes_quotes_to_everyone_and_fills_to_the_owner()
    {
        var (client, _, token) = await factory.CreateTraderAsync();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/market"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        var quoteReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fillReceived = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>("quotes", _ => quoteReceived.TrySetResult());
        connection.On<JsonElement>("tradeExecuted", t => fillReceived.TrySetResult(t));
        await connection.StartAsync();

        await factory.SetQuoteAsync("BNBUSDT", 600m, 600.1m);
        await quoteReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await client.PlaceOrderAsync(new { symbol = "BNBUSDT", side = "Buy", type = "Market", quantity = "1" }, "k");
        var trade = await fillReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("600.1", trade.GetProperty("price").GetString());
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Config_exposes_fee_rate_and_instruments_expose_filters()
    {
        var client = factory.CreateClient();

        var config = await (await client.GetAsync("/api/config")).JsonAsync();
        var instruments = await (await client.GetAsync("/api/instruments")).JsonAsync();

        Assert.Equal("0.001", config.GetProperty("feeRate").GetString());
        Assert.Contains(instruments.EnumerateArray(), i => i.GetProperty("symbol").GetString() == "BTCUSDT"
            && i.GetProperty("stepSize").GetString() == "0.00001");
    }

    [Fact]
    public async Task Health_endpoints_respond()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        await factory.SetQuoteAsync("BTCUSDT", 50_000m, 50_010m);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Theory]
    [InlineData("1.50000000", "1.5")]
    [InlineData("100.00", "100")]
    [InlineData("0.00000001", "0.00000001")]
    [InlineData("-2.10", "-2.1")]
    public void Decimal_normalization_strips_trailing_zeros_only(string input, string expected)
    {
        Assert.Equal(expected, DecimalStringJsonConverter.Normalize(decimal.Parse(input)).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Task<HttpResponseMessage> PostRawAsync(HttpClient client, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }
}
