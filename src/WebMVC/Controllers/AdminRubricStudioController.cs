using System.Text.Json;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>
/// #208 / S-08: the rubric and grader calibration studio - the twenty-eight-cell grid, the three
/// anchors, and the ladder that proves the rubric separates before anyone grades against it.
/// <para>
/// <b>One screen, one rubric.</b> Opened against a (certificate, skill) pair, because a rubric is
/// certificate-scoped: the same four canonical codes exist once per certificate and nowhere may they be
/// shared. The screen renders four criteria by seven bands whether or not the rubric has been written, so
/// an empty band is a hole the author can see rather than a column that is simply absent.
/// </para>
/// <para>
/// <b>Nothing here computes a verdict.</b> The ladder's plot, the criterion it names and the lock's
/// refusal all come from the API - which is the one place the ladder runs. A screen that re-derived
/// "0.65 apart" would eventually disagree with the rule that refuses the lock, and the figure the author
/// acted on would be the one with no rule behind it.
/// </para>
/// <para>
/// ADR-0002: the browser posts to this controller, never to the API. The JWT stays in the HttpOnly
/// cookie and every forward carries the anti-forgery token.
/// </para>
/// </summary>
[Authorize(Policy = Permissions.BlueprintDesign)]
[Route("admin/studio/rubric")]
public sealed class AdminRubricStudioController(
    IRubricStudioClient api,
    CurrentUser currentUser,
    ILogger<AdminRubricStudioController> logger) : AdminControllerBase(currentUser, logger)
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int? certId, int? skillId, CancellationToken cancellationToken = default)
    {
        if (certId is not > 0 || skillId is not > 0)
        {
            return View("~/Views/Admin/RubricStudio.cshtml", Empty(
                "Open this studio from a certificate's rubric: it authors one skill's rubric at a time, "
                + "so it needs the certificate and the skill it belongs to."));
        }

        try
        {
            var studio = await api.GetRubricStudioAsync(certId.Value, skillId.Value, await BearerAsync(), cancellationToken);
            return View("~/Views/Admin/RubricStudio.cshtml", RubricStudioPresentation.For(studio));
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not load rubric studio for cert {CertId} skill {SkillId}", certId, skillId);
            return View("~/Views/Admin/RubricStudio.cshtml", Empty(ex.Detail ?? ex.Message));
        }
        catch (HttpRequestException ex) when (ex is not ApiException { Code: ApiError.Codes.Locked or ApiError.Codes.Unauthenticated })
        {
            Logger!.LogError(ex, "Could not reach the rubric studio for cert {CertId} skill {SkillId}", certId, skillId);
            return View("~/Views/Admin/RubricStudio.cshtml",
                Empty("Could not reach the rubric service. Nothing was changed; reload in a moment."));
        }
    }

    /// <summary>
    /// The grid save. A refusal arrives as the API's sentence, which names the cell or the hole - the
    /// controller does not re-word it.
    /// </summary>
    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveRubricRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var studio = await api.SaveRubricAsync(request, await BearerAsync(), cancellationToken);
            return Json(new
            {
                filledCells = studio.Criteria.Sum(c => c.Cells.Count),
                criteria = studio.Criteria.Count,
            });
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not save a rubric");
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    /// <summary>
    /// The ladder run. It writes nothing: the author sees the plot and the criterion it names before
    /// committing the rubric to it.
    /// </summary>
    [HttpPost("ladder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ladder([FromBody] RunRubricLadderRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var ladder = await api.RunRubricLadderAsync(request, await BearerAsync(), cancellationToken);
            return Json(LadderJson(ladder));
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not run a rubric ladder");
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    /// <summary>
    /// The lock. A refusal is the ladder's own verdict, which names which of monotonicity, separation or
    /// the trap ceiling failed - and, for a flat segment, the criterion responsible.
    /// </summary>
    [HttpPost("lock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock([FromBody] RunRubricLadderRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var ladder = await api.LockRubricAsync(request, await BearerAsync(), cancellationToken);
            return Json(new
            {
                ladder = LadderJson(ladder),
                locked = ladder.Accepted,
            });
        }
        catch (ApiException ex)
        {
            Logger!.LogWarning(ex, "Could not lock a rubric");
            return StatusCode(ex.Status, new { message = ex.Detail ?? ex.Message, code = ex.Code });
        }
    }

    private static object LadderJson(RubricLadderDto ladder) => new
    {
        outcome = ladder.Outcome,
        accepted = ladder.Accepted,
        reason = ladder.Reason,
        // The criterion an author has to rewrite. Empty on acceptance and on the trap refusal (a trap
        // is a whole-rubric failure), which is the ladder's own distinction, not this page's.
        responsibleCriteria = ladder.ResponsibleCriteria,
        label = RubricLadderOutcomes.Label(ladder.Outcome),
        ruleCode = RubricLadderOutcomes.RuleCode(ladder.Outcome),
        points = ladder.Points.Select(p => new { band = p.Band, composite = p.Composite, isVerboseTrap = p.IsVerboseTrap }),
    };

    private static RubricStudioViewModel Empty(string message) =>
        new(0, string.Empty, 0, string.Empty, string.Empty, string.Empty, [],
            new RubricLadderPanel(RubricLadderOutcomes.NotPlotted, false, null, [], [], false),
            false, null, message);
}
