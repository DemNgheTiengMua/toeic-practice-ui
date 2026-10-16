using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class CheckoutController(
    IOrdersClient api,
    CurrentUser currentUser,
    ILogger<CheckoutController> logger)
    : LearnerControllerBase(currentUser, logger)
{
    [HttpGet("/checkout")]
    public async Task<IActionResult> Index([FromQuery] int packageId, CancellationToken cancellationToken)
    {
        if (packageId <= 0)
        {
            var packages = await api.GetPublicPackagesAsync(cancellationToken);
            var featured = packages.FirstOrDefault(p => p.IsFeatured) ?? packages.FirstOrDefault();
            if (featured is null)
            {
                return RedirectToAction("Index", "Pricing");
            }
            packageId = featured.PackageId;
        }

        try
        {
            var preview = await api.GetCheckoutPreviewAsync(packageId, cancellationToken);
            return View(new CheckoutViewModel(preview, ErrorMessage: TempData["ErrorMessage"] as string));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.EmailNotVerified)
        {
            logger.LogInformation("Unverified learner attempted checkout");
            return View(new CheckoutViewModel(null, IsBlockedUnverified: true));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.PackageNotFound)
        {
            logger.LogWarning(ex, "Package {PackageId} not found for checkout", packageId);
            TempData["ShellError"] = "The selected credit package is no longer available.";
            return RedirectToAction("Index", "Pricing");
        }
    }

    [HttpPost("/checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrder([FromForm] int packageId, CancellationToken cancellationToken)
    {
        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest"
            || Request.Headers.Accept.Any(a => a?.Contains("application/json") == true);

        try
        {
            var response = await api.CreateOrderAsync(new CreateOrderRequest(packageId), cancellationToken);
            if (isAjax)
            {
                return Json(new
                {
                    checkoutUrl = response.CheckoutUrl,
                    orderCode = response.OrderCode,
                    paymentNo = response.PaymentNo,
                    amount = response.Amount,
                    gateway = response.Gateway,
                    status = response.Status
                });
            }
            return Redirect(response.CheckoutUrl);
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.EmailNotVerified)
        {
            if (isAjax)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "EMAIL_NOT_VERIFIED", message = "Email verification required" });
            }
            return View("Index", new CheckoutViewModel(null, IsBlockedUnverified: true));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.OrderAlreadyOpen)
        {
            if (isAjax)
            {
                return StatusCode(StatusCodes.Status409Conflict, new { error = ApiError.Codes.OrderAlreadyOpen, message = "You already have an unfinished order." });
            }
            TempData["ErrorMessage"] = "You already have an unfinished order.";
            return RedirectToAction("Index", new { packageId });
        }
    }

    [HttpPost("/checkout/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder(
        [FromForm] string orderCode,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return RedirectToAction("Index", "Pricing");
        }

        try
        {
            await api.CancelOrderAsync(new CancelOrderRequest(orderCode), cancellationToken);
            TempData["ShellNotice"] = $"Order {orderCode} was cancelled.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to cancel order {OrderCode}", orderCode);
            TempData["ShellError"] = "Could not cancel order.";
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Wallet");
    }

    [HttpGet("/checkout/mock-gateway")]
    public async Task<IActionResult> MockGateway([FromQuery] string orderCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return RedirectToAction("Index", "Pricing");
        }

        try
        {
            var result = await api.GetPaymentResultAsync(orderCode, cancellationToken);
            return View(new MockGatewayViewModel(orderCode, result.PackageName, result.Credits, result.Amount));
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to load order {OrderCode} for mock gateway", orderCode);
            return RedirectToAction("Index", "Wallet");
        }
    }

    [HttpPost("/checkout/mock-gateway/action")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MockGatewayAction(
        [FromForm] string orderCode,
        [FromForm] string action,
        [FromForm] string? errorCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await api.ApplyMockGatewayActionAsync(
                new MockGatewayActionRequest(orderCode, action, errorCode),
                cancellationToken);
            return Redirect($"/payment/result?orderCode={Uri.EscapeDataString(orderCode)}");
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to apply mock gateway action {Action} to order {OrderCode}", action, orderCode);
            return Redirect($"/payment/result?orderCode={Uri.EscapeDataString(orderCode)}");
        }
    }

    [HttpGet("/payment/result")]
    public async Task<IActionResult> Result([FromQuery] string orderCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return RedirectToAction("Index", "Pricing");
        }

        try
        {
            var result = await api.HandleReturnAsync(new HandleReturnRequest(orderCode), cancellationToken);
            return View(new PaymentResultViewModel(result));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load payment result for order {OrderCode}", orderCode);
            return View(new PaymentResultViewModel(new PaymentResultDto(
                OrderId: 0,
                OrderCode: orderCode,
                Status: OrderStatusDto.Pending,
                PackageName: "Unknown",
                Credits: 0,
                Amount: 0,
                Gateway: PaymentGatewayDto.Mock,
                CreatedAt: DateTimeOffset.UtcNow,
                PaidAt: null,
                CurrentBalance: 0,
                Message: "Could not read the order status."), LoadFailed: true));
        }
    }
}
