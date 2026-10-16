using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Authorize(Policy = Roles.Admin)]
[Route("admin/orders")]
public sealed class AdminOrdersController(
    IAdminOrdersClient api,
    CurrentUser currentUser,
    ILogger<AdminOrdersController> logger) : Controller
{

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] string? gateway,
        [FromQuery] int page = 1,
        [FromQuery] int? detailId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var summary = await api.GetAdminOrdersAsync(q, status, gateway, page, 20, cancellationToken);

            AdminOrderDetailDto? selectedOrder = null;
            if (detailId.HasValue)
            {
                try
                {
                    selectedOrder = await api.GetAdminOrderDetailAsync(detailId.Value, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not load order detail for order {OrderId}", detailId.Value);
                }
            }

            var vm = new AdminOrdersViewModel(summary, q, status, gateway, page, selectedOrder);
            return View("~/Views/Admin/Orders.cshtml", vm);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not load admin orders list");
            TempData["Error"] = "Could not load the orders list. Please try again.";
            var emptySummary = new AdminOrdersSummaryDto([], 0, 1, 20, 0, 0, 0, 0);
            return View("~/Views/Admin/Orders.cshtml", new AdminOrdersViewModel(emptySummary, q, status, gateway, page));
        }
    }

    [HttpGet("{id:int}/detail")]
    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken)
    {
        try
        {
            var detail = await api.GetAdminOrderDetailAsync(id, cancellationToken);
            return Json(detail);
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.OrderNotFound)
        {
            return NotFound(new { error = "Order not found" });
        }
    }

    [HttpPost("{id:int}/reconcile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reconcile(int id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await api.ReconcileAdminOrderAsync(id, cancellationToken);
            TempData["Success"] = $"Order {result.OrderCode} reconciled: status is now '{result.Status}'.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reconcile order {OrderId}", id);
            TempData["Error"] = "Could not reconcile order status. Please check gateway logs.";
        }

        return RedirectToAction(nameof(Index), new { detailId = id });
    }
}
