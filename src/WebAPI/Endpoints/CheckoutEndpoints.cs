using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class CheckoutEndpoints
{
    public static void MapCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        MapOrders(app);
        MapMockGateway(app);
        MapWebhooks(app);
        MapWallet(app);
    }
}
