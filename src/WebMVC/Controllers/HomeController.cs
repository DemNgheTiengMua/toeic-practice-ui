using System.Diagnostics;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public class HomeController(ICertificatesClient certificates, ILogger<HomeController> logger) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var user = new CurrentUser(new HttpContextAccessor { HttpContext = HttpContext });

        // #161: the landing page names the certificates it lists, so it has to know them. Reading the
        // catalogue is the one thing it asks of the API, and a failure leaves the list empty rather than
        // putting a guess in its place — the marketing page has to stay up when the API is down.
        IReadOnlyList<CertificateDto> catalog = [];
        try
        {
            catalog = await certificates.GetCertificatesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Landing page could not load the certificate catalogue");
        }

        return View(new HomeViewModel(user.IsSignedIn, user.IsAdmin, catalog));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
