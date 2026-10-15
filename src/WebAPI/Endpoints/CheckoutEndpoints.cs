using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class CheckoutEndpoints
{
    public static void MapCheckoutEndpoints(this IEndpointRouteBuilder app)
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

        app.MapPost("/api/payments/webhook", async (
            HttpContext http,
            IPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(http.Request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var signature = http.Request.Headers["x-signature"].FirstOrDefault()
                ?? http.Request.Headers["signature"].FirstOrDefault();

            var result = await payments.HandleWebhookAsync(body, signature, cancellationToken);
            if (!result.IsValid)
            {
                return Results.BadRequest(new { error = "Invalid signature" });
            }
            return Results.Ok(new { message = "Webhook processed" });
        }).AllowAnonymous();

        app.MapPost("/api/payments/payos/confirm-webhook", async (
            PayOsPaymentGateway payos,
            CancellationToken cancellationToken) =>
        {
            var confirmedUrl = await payos.ConfirmWebhookUrlAsync(cancellationToken: cancellationToken);
            return Results.Ok(new { webhookUrl = confirmedUrl });
        }).RequireAuthorization(Roles.Admin);

        app.MapGet("/api/learner/wallet", async (
            HttpContext http,
            IPaymentService payments,
            CancellationToken cancellationToken) =>
        {
            var wallet = await payments.GetWalletAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(wallet);
        }).RequireAuthorization(Roles.Learner);
    }
}
