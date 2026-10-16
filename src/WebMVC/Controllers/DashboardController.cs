using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>The learner's landing page after sign-in, and the certificate switch every learner
/// page's switcher posts to.</summary>
public sealed class DashboardController(
    ILearnerContextClient api,
    IAuthClient auth,
    CurrentUser currentUser,
    ILogger<DashboardController> logger)
    : LearnerControllerBase(currentUser, logger)
{
    /// <param name="activation">"sent" or "failed" after the reminder's Resend button.</param>
    public async Task<IActionResult> Index(string? activation, CancellationToken cancellationToken)
    {
        var dashboard = await api.GetDashboardAsync(cancellationToken);

        // Verification is read live, not from the cookie: the learner may have just clicked the link.
        bool? emailVerified = null;
        try
        {
            emailVerified = (await auth.GetMeAsync(cancellationToken)).EmailVerified;
        }
        catch (HttpRequestException ex) when (ex is not ApiException)
        {
            // API unreachable: render without the reminder rather than fail the page.
            logger.LogWarning(ex, "Could not read verification status for the dashboard");
        }

        AiInsightDto? insight = null;
        try
        {
            insight = await api.GetDashboardInsightAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Failure to generate or fetch never blocks the dashboard
            logger.LogWarning(ex, "Could not pre-fetch AI insight for dashboard");
        }
        ViewBag.Insight = insight;

        return View(new DashboardViewModel(CurrentUser, dashboard, emailVerified, activation));
    }

    [HttpGet("/learner/insights/dashboard")]
    public async Task<IActionResult> GetDashboardInsight(int? certId, CancellationToken cancellationToken)
    {
        try
        {
            var insight = await api.GetDashboardInsightAsync(certId, cancellationToken);
            return Ok(insight);
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("/learner/insights/dashboard/retry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryDashboardInsight(int? certId, CancellationToken cancellationToken)
    {
        try
        {
            await api.RetryDashboardInsightAsync(certId, cancellationToken);
            return Ok(new { success = true });
        }
        catch (ApiException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("/learner/active-certificate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchCertificate(string code, string? returnUrl, CancellationToken cancellationToken)
    {
        try
        {
            await api.SwitchCertificateAsync(code, cancellationToken);
        }
        catch (ApiException ex) when (ex.Code is ApiError.Codes.CertificateUnavailable or ApiError.Codes.Validation)
        {
            // A stale switcher (the certificate was retired since the page loaded) or a form
            // posted without a choice: keep the current certificate and say why.
            logger.LogWarning("Certificate switch to {Code} refused: {ApiCode}", code, ex.Code);
            TempData["ShellError"] = ex.Code == ApiError.Codes.Validation
                ? "Choose a certificate to switch to. Your active certificate was not changed."
                : "That certificate is no longer available. Your active certificate was not changed.";
        }

        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction(nameof(Index));
    }
}
