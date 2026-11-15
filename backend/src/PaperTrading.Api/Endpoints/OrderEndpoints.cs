using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PaperTrading.Api.Contracts;
using PaperTrading.Api.Security;
using PaperTrading.Application.Trading;
using PaperTrading.Domain;
using PaperTrading.Domain.Matching;

namespace PaperTrading.Api.Endpoints;

public static class OrderEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    private const int MaxKeyLength = 100;
    private const int MaxSymbolLength = 20;
    private const int DefaultPageSize = 200;
    private const int MaxPageSize = 500;

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").RequireAuthorization().WithTags("Orders");

        orders.MapPost("/", PlaceOrder)
            .RequireRateLimiting(RateLimitPolicies.Orders)
            .WithSummary("Place a market or limit order. Requires an Idempotency-Key header.");
        orders.MapGet("/", ListOrders)
            .WithSummary("Order history, newest first. Optional ?status=Open|Filled|Cancelled|Rejected.");
        orders.MapGet("/{id:guid}", GetOrder);
        orders.MapDelete("/{id:guid}", CancelOrder)
            .WithSummary("Cancel an open limit order and release its reservation.");

        app.MapGet("/api/trades", ListTrades)
            .RequireAuthorization()
            .WithTags("Orders")
            .WithSummary("Executions (fills), newest first.");

        return app;
    }

    private static async Task<Results<Created<OrderResponse>, Ok<OrderResponse>, ProblemHttpResult>> PlaceOrder(
        PlaceOrderBody body,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        HttpContext http,
        ITradingService trading,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxKeyLength)
            return Problem(StatusCodes.Status400BadRequest, $"A {IdempotencyHeader} header (1-{MaxKeyLength} characters) is required.", "idempotency_key_required");

        // `required` only checks the property is present; "symbol": null still binds.
        if (string.IsNullOrWhiteSpace(body.Symbol) || body.Symbol.Length > MaxSymbolLength)
            return Problem(StatusCodes.Status400BadRequest, $"A symbol of 1-{MaxSymbolLength} characters is required.", "invalid_request");

        var result = await trading.PlaceOrderAsync(http.User.AccountId(), body.ToDomain(), idempotencyKey, ct);
        if (result.Replayed) http.Response.Headers[ReplayedHeader] = "true";

        var order = result.Order?.ToResponse();
        return result.Outcome switch
        {
            PlaceOrderOutcome.Accepted when result.Replayed => TypedResults.Ok(order),
            PlaceOrderOutcome.Accepted => TypedResults.Created($"/api/orders/{order!.Id}", order),
            PlaceOrderOutcome.Rejected => Problem(StatusCodes.Status422UnprocessableEntity, "Order rejected.", result.ErrorCode!, order),
            PlaceOrderOutcome.IdempotencyConflict => Problem(StatusCodes.Status409Conflict, result.ErrorMessage!, result.ErrorCode!, order),
            _ => Problem(StatusCodes.Status400BadRequest, result.ErrorMessage!, result.ErrorCode!),
        };
    }

    private static async Task<Results<Ok<IReadOnlyList<OrderResponse>>, ProblemHttpResult>> ListOrders(
        HttpContext http, ITradingService trading, string? status, int? limit, CancellationToken ct)
    {
        OrderStatus? filter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return Problem(StatusCodes.Status400BadRequest, "Invalid status filter.", "invalid_status");
            filter = parsed;
        }

        var orders = await trading.GetOrdersAsync(http.User.AccountId(), filter, PageSize(limit), ct);
        return TypedResults.Ok(orders.ToResponses(ContractMapping.ToResponse));
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound>> GetOrder(Guid id, HttpContext http, ITradingService trading, CancellationToken ct) =>
        await trading.GetOrderAsync(http.User.AccountId(), id, ct) is { } order
            ? TypedResults.Ok(order.ToResponse())
            : TypedResults.NotFound();

    private static async Task<Results<Ok<OrderResponse>, NotFound, ProblemHttpResult>> CancelOrder(
        Guid id, HttpContext http, ITradingService trading, CancellationToken ct)
    {
        var result = await trading.CancelOrderAsync(http.User.AccountId(), id, ct);
        return result.Outcome switch
        {
            CancelOrderOutcome.Cancelled => TypedResults.Ok(result.Order!.ToResponse()),
            CancelOrderOutcome.NotCancellable => Problem(StatusCodes.Status409Conflict, "Order is not open.", "order_not_open", result.Order?.ToResponse()),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Ok<IReadOnlyList<TradeResponse>>> ListTrades(HttpContext http, ITradingService trading, int? limit, CancellationToken ct) =>
        TypedResults.Ok((await trading.GetTradesAsync(http.User.AccountId(), PageSize(limit), ct)).ToResponses(ContractMapping.ToResponse));

    private static int PageSize(int? requested) => Math.Clamp(requested ?? DefaultPageSize, 1, MaxPageSize);

    /// <summary>RFC 7807 problem with a stable machine-readable <c>code</c> (clients localize by code).</summary>
    private static ProblemHttpResult Problem(int status, string title, string code, OrderResponse? order = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (order is not null) extensions["order"] = order;
        return TypedResults.Problem(statusCode: status, title: title, extensions: extensions);
    }
}
