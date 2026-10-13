using Contracts;
using Microsoft.AspNetCore.Mvc;

namespace WebMVC.Controllers;

/// <summary>Public certificate catalogue, rendered from the API's live rows.</summary>
public sealed class CertificatesController(ILogger<CertificatesController> logger) : Controller
{
    public async Task<IActionResult> Index(
        [FromServices] ICompanionApiClient api,
        CancellationToken cancellationToken)
    {
        try
        {
            var certificates = await api.GetCertificatesAsync(cancellationToken: cancellationToken);
            return View(certificates);
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
