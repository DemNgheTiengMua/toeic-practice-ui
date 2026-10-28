using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class CheckoutEndpoints
{
    private static void MapMockGateway(IEndpointRouteBuilder app)
    {

        app.MapPost("/api/mock-gateway/action", async (
            MockGatewayActionRequest request,
            HttpContext http,
            IPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await payments.ApplyMockGatewayActionAsync(
                    CurrentUserId(http),
                    request.OrderCode,
                    request.Action,
                    request.ErrorCode,
                    cancellationToken);
                return Results.Ok(result);
            }
            catch (OrderNotFoundException ex)
            {
                return Problem(StatusCodes.Status404NotFound, "Order not found", ApiError.Codes.OrderNotFound, ex.Message);
            }
        }).RequireAuthorization(Roles.Learner);
    }
}
