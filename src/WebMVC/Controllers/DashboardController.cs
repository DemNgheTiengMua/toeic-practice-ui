using Contracts;
using Microsoft.AspNetCore.Mvc;

namespace WebMVC.Controllers;

/// <summary>The learner's landing page after sign-in, and the certificate switch every learner
/// page's switcher posts to.</summary>
public sealed class DashboardController(ICompanionApiClient api, CurrentUser currentUser, ILogger<DashboardController> logger)
    : LearnerControllerBase(api, currentUser)
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var dashboard = await Api.GetDashboardAsync(await BearerAsync(), cancellationToken);
        return View(dashboard);
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
            // A stale switcher (the certificate was retired since the page loaded) or a tampered
            // form: keep the current choice and say so rather than failing the page.
            logger.LogWarning("Certificate switch to {Code} refused: {ApiCode}", code, ex.Code);
            TempData["ShellError"] = "That certificate is no longer available. Your active certificate was not changed.";
        }

        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction(nameof(Index));
    }
}
