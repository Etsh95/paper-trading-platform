using Microsoft.AspNetCore.Mvc;
using PaperTrading.Api.Infrastructure;
using PaperTrading.Core.Domain;
using PaperTrading.Core.Engine;

namespace PaperTrading.Api.Endpoints;

public sealed class PlaceOrderBody
{
    public required string Symbol { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderType Type { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? LimitPrice { get; init; }

    public PlaceOrderRequest ToRequest() => new(Symbol, Side, Type, Quantity, LimitPrice);
}

public static class OrderEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    private const int MaxKeyLength = 100;

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").RequireAuthorization().WithTags("Orders");

        group.MapPost("/", PlaceOrder)
            .RequireRateLimiting(RateLimitPolicies.Orders)
            .WithSummary("Place a market or limit order. Requires an Idempotency-Key header.");

        group.MapGet("/", async (HttpContext http, EngineSequencer engine, string? status, int? limit, CancellationToken ct) =>
            {
                OrderStatus? filter = null;
                if (status is not null)
                {
                    if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                        return Results.Problem(statusCode: 400, title: "Invalid status filter.");
                    filter = parsed;
                }
                var accountId = http.User.AccountId();
                var take = Math.Clamp(limit ?? 200, 1, 500);
                return Results.Ok(await engine.ExecuteAsync(e => e.GetOrders(accountId, filter, take), ct));
            })
            .WithSummary("Order history, newest first. Optional ?status=Open|Filled|Cancelled|Rejected.");

        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, EngineSequencer engine, CancellationToken ct) =>
            {
                var accountId = http.User.AccountId();
                var order = await engine.ExecuteAsync(e => e.GetOrder(accountId, id), ct);
                return order is null ? Results.NotFound() : Results.Ok(order);
            });

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, EngineSequencer engine, CancellationToken ct) =>
            {
                var accountId = http.User.AccountId();
                var result = await engine.ExecuteAsync(e => e.CancelOrder(accountId, id), ct);
                return result.Outcome switch
                {
                    CancelOrderOutcome.Cancelled => Results.Ok(result.Order),
                    CancelOrderOutcome.NotCancellable => Problem(409, "Order is not open.", "order_not_open", result.Order),
                    _ => Results.NotFound(),
                };
            })
            .WithSummary("Cancel an open limit order and release its reservation.");

        app.MapGet("/api/trades", async (HttpContext http, EngineSequencer engine, int? limit, CancellationToken ct) =>
            {
                var accountId = http.User.AccountId();
                var take = Math.Clamp(limit ?? 200, 1, 500);
                return await engine.ExecuteAsync(e => e.GetTrades(accountId, take), ct);
            })
            .RequireAuthorization()
            .WithTags("Orders")
            .WithSummary("Executions (fills), newest first.");

        return app;
    }

    private static async Task<IResult> PlaceOrder(
        PlaceOrderBody body,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        HttpContext http,
        EngineSequencer engine,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxKeyLength)
            return Problem(400, $"A {IdempotencyHeader} header (1-{MaxKeyLength} characters) is required.", "idempotency_key_required");

        if (!Enum.IsDefined(body.Side) || !Enum.IsDefined(body.Type))
            return Problem(400, "Invalid side or type.", "invalid_request");

        var accountId = http.User.AccountId();
        var request = body.ToRequest();

        // The token only cancels *queueing*. Once queued, the order executes even if the client disconnects;
        // the client can safely retry with the same key to learn the outcome.
        var result = await engine.ExecuteAsync(e => e.PlaceOrder(accountId, request, idempotencyKey), ct);

        if (result.Replayed) http.Response.Headers[ReplayedHeader] = "true";

        return result.Outcome switch
        {
            PlaceOrderOutcome.Accepted when result.Replayed => Results.Ok(result.Order),
            PlaceOrderOutcome.Accepted => Results.Created($"/api/orders/{result.Order!.Id}", result.Order),
            PlaceOrderOutcome.Rejected => Problem(422, "Order rejected.", result.Order!.RejectReason!, result.Order),
            PlaceOrderOutcome.IdempotencyConflict => Problem(409, result.ErrorMessage!, result.ErrorCode!, result.Order),
            _ => Problem(400, result.ErrorMessage!, result.ErrorCode!),
        };
    }

    private static IResult Problem(int status, string title, string code, OrderSnapshot? order = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (order is not null) extensions["order"] = order;
        return Results.Problem(statusCode: status, title: title, extensions: extensions);
    }
}
