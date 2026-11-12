using System.Globalization;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>
/// #281/#282: the refund screens. <see cref="Permissions.SupportTriage"/> is the gate because a refund
/// is support work; the admin-only namespace governs the system, not a learner's money.
/// </summary>
[Authorize(Policy = Permissions.SupportTriage)]
[Route("admin/refunds")]
public sealed class AdminRefundsController(
    IAdminRefundsClient api,
    CurrentUser currentUser,
    ILogger<AdminRefundsController> logger) : AdminControllerBase(currentUser, logger)
{
    private DateTimeFormatter Dates => HttpContext.RequestServices?.GetService(typeof(DateTimeFormatter)) as DateTimeFormatter
        ?? new(new HttpContextAccessor { HttpContext = HttpContext });

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var summary = await api.GetAdminRefundsAsync(q, status, page, 20, cancellationToken);
            return View("~/Views/Admin/Refunds.cshtml",
                new AdminRefundsViewModel(summary, q, status, page).WithDates(Dates));
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not load refunds list");
            TempData["Error"] = "Could not load the refunds list. Please try again.";
            var empty = new AdminRefundsSummaryDto([], 0, 1, 20, 0, 0, 0, 0);
            return View("~/Views/Admin/Refunds.cshtml",
                new AdminRefundsViewModel(empty, q, status, page).WithDates(Dates));
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var detail = await api.GetAdminRefundDetailAsync(id, cancellationToken);
            return View("~/Views/Admin/RefundDetail.cshtml",
                new AdminRefundDetailViewModel(detail).WithDates(Dates));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.RefundNotFound || ex.Status == 404)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load refund detail {RefundId}", id);
            TempData["Error"] = "Could not load the refund details.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromForm] int userId,
        [FromForm] string? method,
        [FromForm] int creditCount,
        [FromForm] decimal? amount,
        [FromForm] string? reason,
        [FromForm] int? orderId,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            TempData["Error"] = "A learner is required.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "A reason is required on every refund.";
            return RedirectToAction(nameof(Index));
        }

        // The form's select carries a wire code; anything unreadable is refused rather than guessed,
        // because guessing "credit" for a money refund would post a value nobody asked for.
        var isMoney = string.Equals(method, "money", StringComparison.OrdinalIgnoreCase);

        if (isMoney)
        {
            if (amount is not > 0m)
            {
                TempData["Error"] = "A money refund must return more than zero dong.";
                return RedirectToAction(nameof(Index));
            }
        }
        else if (creditCount <= 0)
        {
            TempData["Error"] = "A credit refund must return at least one credit.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var request = new RequestRefundRequest(
                userId,
                isMoney ? RefundMethodDto.Money : RefundMethodDto.Credit,
                isMoney ? 0 : creditCount,
                reason.Trim(),
                orderId is > 0 ? orderId : null,
                Amount: isMoney ? amount : null);
            var created = await api.RequestAdminRefundAsync(request, cancellationToken);
            TempData["Success"] = $"Refund #R-{created.RefundId} recorded and waiting for a decision.";
            return RedirectToAction(nameof(Detail), new { id = created.RefundId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to record refund: {Code}", ex.Code);
            TempData["Error"] = ex.Detail ?? ex.Title ?? ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record refund for learner {UserId}", userId);
            TempData["Error"] = "Could not record the refund. Please try again.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var approved = await api.ApproveAdminRefundAsync(id, cancellationToken);
            TempData["Success"] = approved.Method == RefundMethodDto.Money
                ? $"Refund #R-{id} approved. Nothing is paid yet: an accountant settles it when the transfer is made."
                : $"Refund #R-{id} approved and the credits are in the learner's balance.";
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to approve refund: {Code}", ex.Code);
            TempData["Error"] = ex.Detail ?? ex.Title ?? ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to approve refund {RefundId}", id);
            TempData["Error"] = "Could not approve the refund. Please try again.";
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("{id:int}/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(
        int id,
        [FromForm] string? reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "A reason is required to reject a refund.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        try
        {
            await api.RejectAdminRefundAsync(id, new RejectRefundRequest(reason.Trim()), cancellationToken);
            TempData["Success"] = $"Refund #R-{id} rejected. Nothing was paid.";
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to reject refund: {Code}", ex.Code);
            TempData["Error"] = ex.Detail ?? ex.Title ?? ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reject refund {RefundId}", id);
            TempData["Error"] = "Could not reject the refund. Please try again.";
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    /// <summary>
    /// #282: the accountant's act. It records that the transfer happened - the actor and the moment -
    /// and moves nothing. This is the step the dashboard's net revenue waits for (ADR-0014).
    /// </summary>
    [HttpPost("{id:int}/settle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settle(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var settled = await api.SettleAdminRefundAsync(id, cancellationToken);
            TempData["Success"] = settled.Method == RefundMethodDto.Money
                ? $"Refund #R-{id} settled. {CurrencyFormatter.FormatVnd(settled.Amount)} now counts against revenue."
                : $"Refund #R-{id} settled.";
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to settle refund: {Code}", ex.Code);
            TempData["Error"] = ex.Detail ?? ex.Title ?? ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to settle refund {RefundId}", id);
            TempData["Error"] = "Could not settle the refund. Please try again.";
        }

        return RedirectToAction(nameof(Detail), new { id });
    }

    /// <summary>
    /// #283: the accountant's batch file, reachable from the list screen. Follows the exam export: the MVC
    /// site relays the API's stream, so the browser never calls the API and the JWT stays in its cookie
    /// (ADR-0002).
    /// <para>
    /// <b>A POST, against the API's GET.</b> The action is anti-forgery protected, and a GET cannot be: a
    /// token is validated from a form field or a header, and a plain link carries neither. Nothing changes
    /// in the database either way, but the download is a form so that the range arrives with a token the
    /// same way every other staff action's input does.
    /// </para>
    /// <para>
    /// The range is required rather than defaulted. A convenience default would have to decide which day
    /// "today" is, and a second copy of the UTC+7 rule here could disagree with the API's own definition of
    /// a reporting day - the one thing ADR-0014 exists to prevent.
    /// </para>
    /// <para>
    /// The two dates are bound as text and parsed with the invariant culture, not bound as
    /// <see cref="DateOnly"/>. MVC's binder parses a date with the request's culture, so on a Vietnamese
    /// host <c>2026-11-01</c> - the only shape a <c>type="date"</c> input ever sends - would be refused or,
    /// worse, read as 1 November under one culture and 11 January under another.
    /// </para>
    /// </summary>
    [HttpPost("export")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(
        [FromForm] string? from,
        [FromForm] string? to,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadRange(from, to, out var fromDay, out var toDay))
        {
            TempData["Error"] = "Choose both dates as they appear in the form: the export covers a settlement-date range.";
            return RedirectToAction(nameof(Index));
        }

        if (toDay < fromDay)
        {
            TempData["Error"] = "The export range ends before it starts. Check the two dates.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var (stream, contentType, fileName) = await api.ExportAdminRefundsAsync(
                fromDay, toDay, await BearerAsync(), cancellationToken);

            return File(stream, contentType, fileName);
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to export refunds: {Code}", ex.Code);
            TempData["Error"] = ex.Detail ?? ex.Title ?? ex.Message;
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to export refunds for {From} to {To}", from, to);
            TempData["Error"] = "Could not export the refunds. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>The ISO date a date input sends, read without consulting the host's culture.</summary>
    private static bool TryReadRange(string? from, string? to, out DateOnly fromDay, out DateOnly toDay)
    {
        fromDay = default;
        toDay = default;

        return DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fromDay)
            && DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out toDay);
    }
}
