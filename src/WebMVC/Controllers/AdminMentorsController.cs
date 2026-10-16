using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Authorize(Policy = Roles.Admin)]
[Route("admin/mentors")]
public sealed class AdminMentorsController(
    IAdminMentorsClient api,
    CurrentUser currentUser,
    ILogger<AdminMentorsController> logger) : Controller
{

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var mentors = await api.GetAdminMentorsAsync(cancellationToken);
            return View("~/Views/Admin/Mentors.cshtml", mentors);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not load admin mentors");
            TempData["Error"] = "Could not load the mentor list. Please try again.";
            return View("~/Views/Admin/Mentors.cshtml", Array.Empty<AdminMentorListItemDto>());
        }
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var certOptions = await api.GetMentorCertificateOptionsAsync(cancellationToken);
        var usersSummary = await api.GetAdminUsersAsync(null, null, 1, 100, cancellationToken);

        var model = new MentorFormViewModel
        {
            Certificates = certOptions,
            Users = usersSummary.Users,
            CertId = certOptions.Count > 0 ? certOptions[0].CertId : 0,
            Type = "human",
            PricePerSlot = 200000,
            PriceCredits = 1,
            SessionMinutes = 60,
            IsActive = true,
        };

        return View("~/Views/Admin/MentorForm.cshtml", model);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] MentorFormViewModel model, CancellationToken cancellationToken)
    {

        if (model.Type == "human" && (!model.UserId.HasValue || model.UserId.Value <= 0))
        {
            ModelState.AddModelError(nameof(model.UserId), "User account is required for human mentors.");
        }

        if (!ModelState.IsValid)
        {
            model.Certificates = await api.GetMentorCertificateOptionsAsync(cancellationToken);
            var usersSummary = await api.GetAdminUsersAsync(null, null, 1, 100, cancellationToken);
            model.Users = usersSummary.Users;
            return View("~/Views/Admin/MentorForm.cshtml", model);
        }

        try
        {
            var request = new CreateMentorProfileRequest(
                model.Type == "human" ? model.UserId : null,
                model.DisplayName,
                model.Bio,
                model.CertId,
                model.SkillId,
                model.Type,
                model.PricePerSlot,
                model.PriceCredits,
                model.Headline,
                model.Expertise,
                model.SessionMinutes,
                model.IsActive);

            await api.CreateMentorAsync(request, cancellationToken);
            TempData["Success"] = $"Mentor '{model.DisplayName}' created successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to create mentor");
            ModelState.AddModelError(string.Empty, ex.Detail ?? ex.Message);
            model.Certificates = await api.GetMentorCertificateOptionsAsync(cancellationToken);
            var usersSummary = await api.GetAdminUsersAsync(null, null, 1, 100, cancellationToken);
            model.Users = usersSummary.Users;
            return View("~/Views/Admin/MentorForm.cshtml", model);
        }
    }

    [HttpGet("{mentorId:int}/edit")]
    public async Task<IActionResult> Edit(int mentorId, CancellationToken cancellationToken)
    {

        try
        {
            var mentor = await api.GetAdminMentorByIdAsync(mentorId, cancellationToken);
            var certOptions = await api.GetMentorCertificateOptionsAsync(cancellationToken);
            var usersSummary = await api.GetAdminUsersAsync(null, null, 1, 100, cancellationToken);

            var model = new MentorFormViewModel
            {
                MentorId = mentor.MentorId,
                Type = mentor.Type,
                UserId = mentor.UserId,
                DisplayName = mentor.DisplayName,
                Bio = mentor.Bio,
                CertId = mentor.CertId,
                SkillId = mentor.SkillId,
                PricePerSlot = mentor.PricePerSlot,
                PriceCredits = mentor.PriceCredits,
                Headline = mentor.Headline,
                Expertise = mentor.Expertise,
                SessionMinutes = mentor.SessionMinutes,
                IsActive = mentor.IsActive,
                Certificates = certOptions,
                Users = usersSummary.Users,
            };

            return View("~/Views/Admin/MentorForm.cshtml", model);
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound();
        }
    }

    [HttpPost("{mentorId:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int mentorId, [FromForm] MentorFormViewModel model, CancellationToken cancellationToken)
    {

        if (model.Type == "human" && (!model.UserId.HasValue || model.UserId.Value <= 0))
        {
            ModelState.AddModelError(nameof(model.UserId), "User account is required for human mentors.");
        }

        if (!ModelState.IsValid)
        {
            model.Certificates = await api.GetMentorCertificateOptionsAsync(cancellationToken);
            var usersSummary = await api.GetAdminUsersAsync(null, null, 1, 100, cancellationToken);
            model.Users = usersSummary.Users;
            return View("~/Views/Admin/MentorForm.cshtml", model);
        }

        try
        {
            var request = new UpdateMentorProfileRequest(
                model.Type == "human" ? model.UserId : null,
                model.DisplayName,
                model.Bio,
                model.CertId,
                model.SkillId,
                model.PricePerSlot,
                model.PriceCredits,
                model.Headline,
                model.Expertise,
                model.SessionMinutes,
                model.IsActive);

            await api.UpdateMentorAsync(mentorId, request, cancellationToken);
            TempData["Success"] = $"Mentor '{model.DisplayName}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to update mentor {MentorId}", mentorId);
            ModelState.AddModelError(string.Empty, ex.Detail ?? ex.Message);
            model.Certificates = await api.GetMentorCertificateOptionsAsync(cancellationToken);
            var usersSummary = await api.GetAdminUsersAsync(null, null, 1, 100, cancellationToken);
            model.Users = usersSummary.Users;
            return View("~/Views/Admin/MentorForm.cshtml", model);
        }
    }

    [HttpGet("{mentorId:int}/slots")]
    public async Task<IActionResult> Slots(int mentorId, CancellationToken cancellationToken)
    {

        try
        {
            var mentor = await api.GetAdminMentorByIdAsync(mentorId, cancellationToken);
            var slots = await api.GetAdminMentorSlotsAsync(mentorId, cancellationToken);

            var model = new MentorSlotsViewModel
            {
                Mentor = mentor,
                Slots = slots,
                NewSlot = new CreateSlotViewModel
                {
                    LocalStartAt = DateTime.Today.AddDays(1).AddHours(9),
                },
            };

            return View("~/Views/Admin/MentorSlots.cshtml", model);
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound();
        }
    }

    [HttpPost("{mentorId:int}/slots/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSlot(int mentorId, [FromForm] CreateSlotViewModel form, CancellationToken cancellationToken)
    {

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please provide a valid start date and time.";
            return RedirectToAction(nameof(Slots), new { mentorId });
        }

        try
        {
            var request = new CreateMentorSlotRequest(form.LocalStartAt, form.LocalEndAt);
            await api.CreateMentorSlotAsync(mentorId, request, cancellationToken);
            TempData["Success"] = "Slot added successfully.";
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to create slot for mentor {MentorId}", mentorId);
            TempData["Error"] = ex.Detail ?? ex.Message;
        }

        return RedirectToAction(nameof(Slots), new { mentorId });
    }

    [HttpPost("{mentorId:int}/slots/{slotId:int}/close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseSlot(int mentorId, int slotId, CancellationToken cancellationToken)
    {

        try
        {
            await api.CloseMentorSlotAsync(mentorId, slotId, cancellationToken);
            TempData["Success"] = "Slot closed.";
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to close slot {SlotId} for mentor {MentorId}", slotId, mentorId);
            TempData["Error"] = ex.Detail ?? ex.Message;
        }

        return RedirectToAction(nameof(Slots), new { mentorId });
    }
}
