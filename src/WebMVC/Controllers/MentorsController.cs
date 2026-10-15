using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class MentorsController(
    ICompanionApiClient api,
    CurrentUser currentUser,
    ILogger<MentorsController> logger)
    : LearnerControllerBase(api, currentUser, logger)
{
    [HttpGet("/mentors")]
    public async Task<IActionResult> Index([FromQuery] string? skill, CancellationToken cancellationToken)
    {
        var shell = LearnerShell.Get(HttpContext);
        var activeCertCode = shell?.Active?.Code ?? "TOEIC";

        try
        {
            var mentors = await Api.GetMentorsAsync(activeCertCode, skill, cancellationToken);
            return View(new MentorListViewModel(mentors, skill, activeCertCode));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load mentor list for cert {CertCode}", activeCertCode);
            return View(new MentorListViewModel([], skill, activeCertCode, "Could not load mentors. Please try again."));
        }
    }

    [HttpGet("/mentors/{mentorId:int}")]
    public async Task<IActionResult> Detail(int mentorId, CancellationToken cancellationToken)
    {
        try
        {
            var detail = await Api.GetMentorDetailAsync(mentorId, cancellationToken);
            return View(new MentorDetailViewModel(detail));
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load mentor details for mentor {MentorId}", mentorId);
            TempData["ShellError"] = "Could not load mentor profile.";
            return RedirectToAction(nameof(Index));
        }
    }
}
