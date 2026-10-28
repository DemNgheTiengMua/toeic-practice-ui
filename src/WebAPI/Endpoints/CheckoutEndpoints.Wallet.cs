using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class CheckoutEndpoints
{
    private static void MapWallet(IEndpointRouteBuilder app)
    {

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
