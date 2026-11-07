using System.Collections.Generic;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>
/// #203 / S-05 / ADM-07: the stimulus-and-children studio.
/// <para>
/// <b>One screen, master-detail.</b> The stimulus is pinned left and its children stack right, because
/// the two hard shapes ADM-07 names - a shared passage and a shared audio track - are authored as one
/// bundle and corrected one item at a time only when the whole is visible. The alternative, a list of
/// items with a passage upload on each, is the shape that produced duplicated passages.
/// </para>
/// <para>
/// <b>Nothing here computes a verdict.</b> The gauges, the overlaps, the gaps and the refusals all come
/// from <see cref="IStimulusStudioClient"/>, which is the API's one seam; this controller groups and
/// forwards, exactly as <see cref="AdminCoverageController"/> does. A screen that recomputed an IoU
/// would eventually disagree with the rule that refuses the save.
/// </para>
/// <para>
/// ADR-0002: the browser posts to this controller, never to the API. The JWT stays in the HttpOnly
/// cookie and the forwards carry the anti-forgery token.
/// </para>
/// </summary>
[Authorize(Policy = Permissions.ContentAuthor)]
[Route("admin/studio/stimulus")]
public sealed class AdminStimulusStudioController(
    IStimulusStudioClient api,
    CurrentUser currentUser,
    ILogger<AdminStimulusStudioController> logger) : AdminControllerBase(currentUser, logger)
{
    /// <summary>
    /// The two skills this screen authors. A speaking or writing prompt is one task with a rubric, and
    /// it belongs to the productive-task studio (S-07); offering it here would pre-create child slots
    /// the schema cannot hold.
    /// </summary>
    private static readonly IReadOnlyList<string> Skills = ["reading", "listening"];

    [HttpGet("")]
    public async Task<IActionResult> Index(string skill = "reading", CancellationToken cancellationToken = default)
    {
        var modality = Skills.Contains(skill) ? skill : "reading";

        try
        {
            var options = await api.GetStimulusStudioOptionsAsync(modality, cancellationToken);
            var model = StimulusStudioPresentation.Blank(
                modality,
                options,
                code => CefrLevelLabels.LabelForCode(code, Logger!),
                code => ModalityLabels.LabelForCode(code, Logger!));

            return View("~/Views/Admin/StimulusStudio.cshtml", model);
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not load the stimulus studio options for {Skill}", modality);
            return View("~/Views/Admin/StimulusStudio.cshtml",
                new StimulusStudioViewModel(
                    modality,
                    "Reading",
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
    /// bundle does not fit a query string.
    /// </summary>
    [HttpPost("analyse")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyse([FromBody] StimulusDraftRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var analysis = await api.AnalyseStimulusDraftAsync(request, cancellationToken);
            return Json(new
            {
                acceptable = analysis.Acceptable,
                refusals = analysis.Refusals,
                unrunChecks = analysis.UnrunChecks,
                gauges = analysis.Gauges,
                verdict = analysis.LevelVerdict,
                selectedBand = analysis.SelectedBand,
                fittingBand = analysis.FittingBand,
                overlaps = analysis.Overlaps,
                gaps = analysis.Gaps,
                maximumOverlap = analysis.MaximumOverlap,
            });
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not analyse a stimulus draft");
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    /// <summary>The save. A refusal arrives as the API's sentence, which names the gate.</summary>
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] StimulusDraftRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var saved = await api.SaveStimulusBundleAsync(request, cancellationToken);
            return Json(new { stimulusId = saved.StimulusId, children = saved.Children });
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not save a stimulus bundle");
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }
}
