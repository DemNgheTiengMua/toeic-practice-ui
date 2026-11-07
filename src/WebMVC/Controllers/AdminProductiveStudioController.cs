using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>
/// #204 / S-07: the productive-task and visual-stimulus studio.
/// <para>
/// <b>One screen, one task.</b> The prompt is pinned left and its parts stack right, because a two-part
/// writing task is one task with two budgets and the envelope above them is a property of the task.
/// The alternative - a list of independent items - is the shape that lets the second part be forgotten.
/// </para>
/// <para>
/// <b>Nothing here computes a verdict.</b> The refusals, the unrun checks and the placement caps all
/// come from <see cref="IProductiveStudioClient"/>, which is the API's one seam; this controller groups
/// and forwards. A screen that recomputed the cap would eventually disagree with the rule that refuses
/// the save - and the cap is the one figure the ticket requires the author to see before working.
/// </para>
/// <para>
/// ADR-0002: the browser posts to this controller, never to the API. The JWT stays in the HttpOnly
/// cookie and the forwards carry the anti-forgery token.
/// </para>
/// </summary>
[Authorize(Policy = Permissions.ContentAuthor)]
[Route("admin/studio/productive")]
public sealed class AdminProductiveStudioController(
    IProductiveStudioClient api,
    CurrentUser currentUser,
    ILogger<AdminProductiveStudioController> logger) : AdminControllerBase(currentUser, logger)
{
    /// <summary>
    /// The two skills this screen authors. A shared passage or audio is the stimulus studio's (S-05);
    /// offering it here would pre-create a prompt a rubric cannot score as one task.
    /// </summary>
    private static readonly IReadOnlyList<string> Skills = ["writing", "speaking"];

    [HttpGet("")]
    public async Task<IActionResult> Index(string skill = "writing", CancellationToken cancellationToken = default)
    {
        var modality = Skills.Contains(skill) ? skill : "writing";

        try
        {
            var options = await api.GetProductiveStudioOptionsAsync(modality, cancellationToken);
            var model = ProductiveStudioPresentation.Blank(
                modality,
                options,
                code => CefrLevelLabels.LabelForCode(code, Logger!),
                code => ModalityLabels.LabelForCode(code, Logger!));

            return View("~/Views/Admin/ProductiveStudio.cshtml", model);
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not load the productive studio options for {Skill}", modality);
            return View("~/Views/Admin/ProductiveStudio.cshtml",
                new ProductiveStudioViewModel(
                    modality,
                    ModalityLabels.LabelForCode(modality, Logger!),
                    [],
                    [],
                    [],
                    [],
                    "Could not load the studio. Please try again.",
                    null));
        }
    }

    /// <summary>
    /// The live analysis the screen calls on every edit. It writes nothing, and it is a POST because a
    /// task does not fit a query string.
    /// </summary>
    [HttpPost("analyse")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyse([FromBody] ProductiveTaskDraftRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var analysis = await api.AnalyseProductiveTaskAsync(request, cancellationToken);
            return Json(new
            {
                acceptable = analysis.Acceptable,
                refusals = analysis.Refusals,
                unrunChecks = analysis.UnrunChecks,
                placementRecordingCap = analysis.PlacementRecordingCap,
                placementWordRanges = analysis.PlacementWordRanges,
                practiceWordRanges = analysis.PracticeWordRanges,
            });
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not analyse a productive task draft");
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    /// <summary>The save. A refusal arrives as the API's sentence, which names the rule.</summary>
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] ProductiveTaskDraftRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var saved = await api.SaveProductiveTaskAsync(request, cancellationToken);
            return Json(new { stimulusId = saved.StimulusId, parts = saved.Parts });
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not save a productive task");
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }
}
