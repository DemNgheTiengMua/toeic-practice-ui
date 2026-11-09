using Contracts;
using Microsoft.AspNetCore.Mvc;

namespace WebMVC.Controllers;

/// <summary>Public certificate catalogue, rendered from the API's live rows.</summary>
public sealed class CertificatesController(ILogger<CertificatesController> logger) : Controller
{
    public async Task<IActionResult> Index(
        [FromServices] ICertificatesClient api,
        CancellationToken cancellationToken)
    {
        try
        {
            var certificates = await api.GetCertificatesAsync(cancellationToken: cancellationToken);
            var user = new CurrentUser(new HttpContextAccessor { HttpContext = HttpContext });
            // #250 audit: which certificate the learner is already on. Without it every card showed
            // an identical filled "Switch to X" button - including the active one, whose switch is a
            // no-op - so the page could not say where you are. The shell is the one place that knows.
            var activeCertCode = WebMVC.Infrastructure.LearnerShell.Get(HttpContext)?.Active?.Code;
            return View(new WebMVC.Models.CertificateCatalogViewModel(certificates, user.IsSignedIn, user.IsStaff)
            {
                ActiveCertCode = activeCertCode,
            });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Certificates API call failed with {Code}", ex.Code);
            return View("Unavailable");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Certificates API unreachable");
            return View("Unavailable");
        }
    }
}
