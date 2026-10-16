using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Authorize(Policy = Roles.Learner)]
[Route("support")]
public sealed class SupportController(
    ISupportClient api,
    CurrentUser currentUser,
    ILogger<SupportController> logger) : LearnerControllerBase(currentUser, logger)
{
    [HttpGet("report")]
    public async Task<IActionResult> Report(
        [FromQuery] string? category = "mentor",
        [FromQuery] string? type = "mentor_no_show",
        [FromQuery] int? bookingId = null,
        [FromQuery] int? attemptId = null,
        [FromQuery] int? orderId = null,
        CancellationToken cancellationToken = default)
    {
        var safeCategory = category switch
        {
            "mentor" or "exam" or "payment" => category,
            _ => "mentor"
        };
        var safeType = EnumMapping.TryParseWireCode<ComplaintTypeDto>(type ?? "", out var parsedType)
            ? parsedType.ToWireCode()
            : (safeCategory == "exam" ? "grading_issue" : safeCategory == "payment" ? "payment_issue" : "mentor_no_show");
        var eligible = await api.GetEligibleSubjectsAsync(cancellationToken);
        var vm = new SupportReportViewModel(eligible, safeCategory, safeType, bookingId, attemptId, orderId);
        return View("~/Views/Support/Report.cshtml", vm);
    }

    [HttpPost("report")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitReport(
        [FromForm] string? category,
        [FromForm] string? type,
        [FromForm] int? bookingId,
        [FromForm] int? attemptId,
        [FromForm] int? orderId,
        [FromForm] string? reason,
        CancellationToken cancellationToken = default)
    {

        if (string.IsNullOrWhiteSpace(reason))
        {
            var eligible = await api.GetEligibleSubjectsAsync(cancellationToken);
            var vm = new SupportReportViewModel(
                eligible, category, type, bookingId, attemptId, orderId, reason,
                "Please describe the problem before sending.");
            return View("~/Views/Support/Report.cshtml", vm);
        }

        if (reason.Trim().Length > 4000)
        {
            var eligible = await api.GetEligibleSubjectsAsync(cancellationToken);
            var vm = new SupportReportViewModel(
                eligible, category, type, bookingId, attemptId, orderId, reason,
                "Please keep your description under 4000 characters.");
            return View("~/Views/Support/Report.cshtml", vm);
        }

        if (string.IsNullOrWhiteSpace(type) || !EnumMapping.TryParseWireCode<ComplaintTypeDto>(type, out var typeDto))
        {
            var eligible = await api.GetEligibleSubjectsAsync(cancellationToken);
            var vm = new SupportReportViewModel(
                eligible, category, type, bookingId, attemptId, orderId, reason,
                "Please choose what happened.");
            return View("~/Views/Support/Report.cshtml", vm);
        }

        // Validate subject choice
        int? finalBookingId = null;
        int? finalAttemptId = null;
        int? finalOrderId = null;

        if (typeDto is ComplaintTypeDto.MentorNoShow or ComplaintTypeDto.MentorQuality)
        {
            if (!bookingId.HasValue || bookingId.Value <= 0)
            {
                var eligible = await api.GetEligibleSubjectsAsync(cancellationToken);
                var vm = new SupportReportViewModel(
                    eligible, category, type, bookingId, attemptId, orderId, reason,
                    "Please choose a session before sending.");
                return View("~/Views/Support/Report.cshtml", vm);
            }
            finalBookingId = bookingId.Value;
        }
        else if (typeDto is ComplaintTypeDto.GradingIssue or ComplaintTypeDto.TechnicalError)
        {
            if (!attemptId.HasValue || attemptId.Value <= 0)
            {
                var eligible = await api.GetEligibleSubjectsAsync(cancellationToken);
                var vm = new SupportReportViewModel(
                    eligible, category, type, bookingId, attemptId, orderId, reason,
                    "Please choose an exam before sending.");
                return View("~/Views/Support/Report.cshtml", vm);
            }
            finalAttemptId = attemptId.Value;
        }
        else if (typeDto is ComplaintTypeDto.PaymentIssue)
        {
            if (!orderId.HasValue || orderId.Value <= 0)
            {
                var eligible = await api.GetEligibleSubjectsAsync(cancellationToken);
                var vm = new SupportReportViewModel(
                    eligible, category, type, bookingId, attemptId, orderId, reason,
                    "Please choose an order before sending.");
                return View("~/Views/Support/Report.cshtml", vm);
            }
            finalOrderId = orderId.Value;
        }

        try
        {
            var request = new FileComplaintRequest(typeDto, reason.Trim(), finalBookingId, finalAttemptId, finalOrderId);
            await api.FileComplaintAsync(request, cancellationToken);
            TempData["Success"] = "Report received. An admin will review your report within 2 working days.";
            return RedirectToAction(nameof(History));
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to submit complaint: {Code}", ex.Code);
            var eligible = await api.GetEligibleSubjectsAsync(cancellationToken);
            var vm = new SupportReportViewModel(
                eligible, category, type, bookingId, attemptId, orderId, reason,
                ex.Detail ?? ex.Title ?? ex.Message);
            return View("~/Views/Support/Report.cshtml", vm);
        }
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(CancellationToken cancellationToken = default)
    {
        var complaints = await api.GetLearnerComplaintsAsync(cancellationToken);
        var vm = new SupportHistoryViewModel(complaints);
        return View("~/Views/Support/History.cshtml", vm);
    }
}
