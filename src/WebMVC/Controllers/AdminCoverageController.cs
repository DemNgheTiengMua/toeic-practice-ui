using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>
/// #202 / S-01: the warehouse coverage matrix — the first staff screen. It renders the 120 cells with
/// how much content each holds against how much it should, so an empty cell is something an author
/// looks at rather than something they query.
/// <para>
/// Every number on the page comes from <c>vw_CoverageMatrix</c>. This controller groups the cells by
/// skill and turns two codes into text; it counts nothing, because a count here would be a second
/// definition of coverage and the two would eventually disagree.
/// </para>
/// </summary>
[Authorize(Policy = Permissions.ContentAuthor)]
[Route("admin/coverage")]
public sealed class AdminCoverageController(
    IAdminCoverageClient api,
    CurrentUser currentUser,
    ILogger<AdminCoverageController> logger) : AdminControllerBase(currentUser, logger)
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        try
        {
            var matrix = await api.GetAdminCoverageAsync(cancellationToken);

            // D19: no raw code reaches the page. An unmapped code renders as its fallback and logs,
            // which is why the registries take the logger rather than returning the code.
            var view = AdminCoveragePresentation.From(
                matrix,
                code => CefrLevelLabels.LabelForCode(code, logger),
                code => ModalityLabels.LabelForCode(code, logger));

            return View("~/Views/Admin/Coverage.cshtml", view);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not load the coverage matrix");
            return View("~/Views/Admin/Coverage.cshtml",
                new AdminCoverageViewModel([], "Could not load the coverage matrix. Please try again."));
        }
    }
}
