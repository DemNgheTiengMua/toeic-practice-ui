using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>The learner's landing page after sign-in, and the certificate switch every learner
/// page's switcher posts to.</summary>
public sealed class DashboardController(ICompanionApiClient api, CurrentUser currentUser, ILogger<DashboardController> logger)
    : LearnerControllerBase(api, currentUser, logger)
{
    /// <param name="activation">"sent" or "failed" after the reminder's Resend button.</param>
    public async Task<IActionResult> Index(string? activation, CancellationToken cancellationToken)
    {
        var bearer = await BearerAsync();
        var dashboard = await Api.GetDashboardAsync(bearer, cancellationToken);

        // Verification is read live, not from the cookie: the learner may have just clicked the link.
        bool? emailVerified = null;
        try
        {
            emailVerified = (await Api.GetMeAsync(bearer, cancellationToken)).EmailVerified;
        }
        catch (HttpRequestException ex) when (ex is not ApiException)
        {
            // API unreachable: render without the reminder rather than fail the page.
            logger.LogWarning(ex, "Could not read verification status for the dashboard");
        }

        return View(new DashboardViewModel(CurrentUser, dashboard, emailVerified, activation));
    }

    [HttpPost("/learner/active-certificate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchCertificate(string code, string? returnUrl, CancellationToken cancellationToken)
    {
        try
        {
            await Api.SwitchCertificateAsync(await BearerAsync(), code, cancellationToken);
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
