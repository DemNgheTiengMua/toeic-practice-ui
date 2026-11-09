using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Infrastructure;

namespace WebMVC.Controllers;

/// <summary>Public certificate catalogue, rendered from the API's live rows.</summary>
public sealed class CertificatesController(
    ILogger<CertificatesController> logger,
    ILearnerContextClient? learnerContext = null) : Controller
{
    public async Task<IActionResult> Index(
        [FromServices] ICertificatesClient api,
        CancellationToken cancellationToken)
    {
        var user = new CurrentUser(new HttpContextAccessor { HttpContext = HttpContext });

        // #250 audit: the page has to say where the learner IS, not only where they could go. Without
        // this every card rendered the same "Switch to X" button, including the active one, whose
        // switch does nothing. The shell is the one place that knows which certificate is active, and
        // it is loaded here rather than in a base class because this page is public: a signed-out
        // visitor has no shell at all, and an API failure must leave the catalogue readable rather
        // than turning the marketing page into an error screen.
        if (user.IsSignedIn && !user.IsStaff && learnerContext is not null)
        {
            try
            {
                LearnerShell.Set(HttpContext, await learnerContext.GetLearnerContextAsync(cancellationToken));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not load learner shell context for the certificate catalogue");
            }
        }

        try
        {
            var certificates = await api.GetCertificatesAsync(cancellationToken: cancellationToken);
            return View(new WebMVC.Models.CertificateCatalogViewModel(certificates, user.IsSignedIn, user.IsStaff)
            {
                ActiveCertCode = LearnerShell.Get(HttpContext)?.Active?.Code,
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
