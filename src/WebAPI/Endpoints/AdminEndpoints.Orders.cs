using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using WebAPI.Filters;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapOrders(RouteGroupBuilder admin)
    {

        admin.MapGet("/orders", async (
            string? q,
            string? status,
            string? gateway,
            [AsParameters] PaginationQuery pagination,
            IAdminOrderService orders,
            CancellationToken cancellationToken) =>
        {
            var summary = await orders.GetOrdersAsync(q, status, gateway, pagination.PageOrDefault, pagination.PageSizeOrDefault, cancellationToken);
            return Results.Ok(summary);
        }).AddEndpointFilter<RequestValidationFilter<PaginationQuery>>();

        admin.MapGet("/orders/{orderId:int}", async (
            int orderId,
            IAdminOrderService orders,
            CancellationToken cancellationToken) =>
        {
            var detail = await orders.GetOrderDetailAsync(orderId, cancellationToken);
            return detail is not null ? Results.Ok(detail) : Results.NotFound();
        });

        admin.MapPost("/orders/{orderId:int}/reconcile", async (
            int orderId,
            IAdminOrderService orders,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await orders.ReconcileOrderAsync(orderId, cancellationToken);
                return Results.Ok(result);
            }
            catch (OrderNotFoundException)
            {
                return Results.NotFound();
            }
        });
    }
}
