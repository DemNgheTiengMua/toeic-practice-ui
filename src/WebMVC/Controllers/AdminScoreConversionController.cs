using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Authorize(Policy = Roles.Admin)]
[Route("admin/score-conversion")]
public sealed class AdminScoreConversionController(
    IAdminScoreConversionClient api,
    CurrentUser currentUser,
    ILogger<AdminScoreConversionController> logger) : Controller
{

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? cert, [FromQuery] int? examId, CancellationToken cancellationToken)
    {
        try
        {
            var conversion = await api.GetAdminScoreConversionAsync(cert, examId, cancellationToken);
            return View("~/Views/Admin/ScoreConversion.cshtml", new AdminScoreConversionViewModel(
                conversion,
                ErrorMessage: TempData["Error"] as string,
                SuccessMessage: TempData["Success"] as string));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load admin score conversion");
            TempData["Error"] = "Could not load the score conversion table. Please try again.";
            return View("~/Views/Admin/ScoreConversion.cshtml", new AdminScoreConversionViewModel(
                new AdminScoreConversionDto(0, "", "", [], null, null, [], []),
                ErrorMessage: TempData["Error"] as string));
        }
    }

    [HttpPost("{certId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int certId, [FromForm] ScoreConversionFormModel form, CancellationToken cancellationToken)
    {
        try
        {
            var scales = form.Scales.Select(s => new UpdateScoreScaleEntryDto(s.SkillId, s.RawScore, s.ScaledScore)).ToList();
            var bands = form.Bands.Select(b => new UpdateScoreBandEntryDto(b.BandId, b.Code, b.Name, b.MinTotal, b.MaxTotal, b.DisplayOrder)).ToList();
            var request = new UpdateScoreConversionRequest(form.ExamId, scales, bands);

            var updated = await api.UpdateAdminScoreConversionAsync(certId, request, cancellationToken);
            TempData["Success"] = "Score conversion table saved successfully.";
            return RedirectToAction(nameof(Index), new { cert = updated.CertCode, examId = updated.ExamId });
        }
        catch (ApiException ex)
        {
            logger.LogWarning(ex, "Failed to save score conversion for cert {CertId}", certId);
            TempData["Error"] = ex.Detail ?? "Failed to save score conversion table. Check ranges and monotonicity.";
            return RedirectToAction(nameof(Index), new { cert = form.CertCode, examId = form.ExamId });
        }
    }
}
