using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class CheckoutEndpoints
{
    private static void MapWebhooks(IEndpointRouteBuilder app)
    {

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
    }
}
