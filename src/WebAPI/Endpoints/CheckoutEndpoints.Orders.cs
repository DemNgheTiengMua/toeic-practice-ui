using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class CheckoutEndpoints
{
    private static void MapOrders(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/checkout/preview", async (
            int packageId,
            HttpContext http,
            IPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var preview = await payments.GetCheckoutPreviewAsync(CurrentUserId(http), packageId, cancellationToken);
                return Results.Ok(preview);
            }
            catch (EmailNotVerifiedException ex)
            {
                return Problem(StatusCodes.Status403Forbidden, "Email verification required", ApiError.Codes.EmailNotVerified, ex.Message);
            }
            catch (PackageNotFoundException ex)
            {
                return Problem(StatusCodes.Status404NotFound, "Package not found", ApiError.Codes.PackageNotFound, ex.Message);
            }
        }).RequireAuthorization(Roles.Learner);

        app.MapPost("/api/checkout/orders", async (
            CreateOrderRequest request,
            HttpContext http,
            IPaymentService payments,
            AuthLinkOptions authLinks,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var baseUrl = !string.IsNullOrWhiteSpace(authLinks.PublicBaseUrl)
                    ? authLinks.PublicBaseUrl
                    : $"{http.Request.Scheme}://{http.Request.Host}";
                var response = await payments.CreateOrderAsync(CurrentUserId(http), request.PackageId, baseUrl, cancellationToken);
                return Results.Ok(response);
            }
            catch (EmailNotVerifiedException ex)
            {
                return Problem(StatusCodes.Status403Forbidden, "Email verification required", ApiError.Codes.EmailNotVerified, ex.Message);
            }
            catch (OrderAlreadyOpenException ex)
            {
                return Problem(StatusCodes.Status409Conflict, "Order already open", ApiError.Codes.OrderAlreadyOpen, ex.Message);
            }
            catch (PackageNotFoundException ex)
            {
                return Problem(StatusCodes.Status404NotFound, "Package not found", ApiError.Codes.PackageNotFound, ex.Message);
            }
        }).RequireAuthorization(Roles.Learner);

        app.MapPost("/api/checkout/return", async (
            HandleReturnRequest request,
            HttpContext http,
            IPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await payments.HandleReturnRedirectAsync(CurrentUserId(http), request.OrderCode, cancellationToken);
                return Results.Ok(result);
            }
            catch (OrderNotFoundException ex)
            {
                return Problem(StatusCodes.Status404NotFound, "Order not found", ApiError.Codes.OrderNotFound, ex.Message);
            }
        }).RequireAuthorization(Roles.Learner);

        app.MapGet("/api/checkout/orders/{orderCode}/result", async (
            string orderCode,
            HttpContext http,
            IPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await payments.GetPaymentResultAsync(CurrentUserId(http), orderCode, cancellationToken);
                return Results.Ok(result);
            }
            catch (OrderNotFoundException ex)
            {
                return Problem(StatusCodes.Status404NotFound, "Order not found", ApiError.Codes.OrderNotFound, ex.Message);
            }
        }).RequireAuthorization(Roles.Learner);

        app.MapPost("/api/checkout/cancel", async (
            CancelOrderRequest request,
            HttpContext http,
            IPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await payments.CancelOrderAsync(CurrentUserId(http), request.OrderCode, cancellationToken);
                return Results.Ok(new { message = "Order cancelled successfully" });
            }
            catch (OrderNotFoundException ex)
            {
                return Problem(StatusCodes.Status404NotFound, "Order not found", ApiError.Codes.OrderNotFound, ex.Message);
            }
        }).RequireAuthorization(Roles.Learner);
    }
}
