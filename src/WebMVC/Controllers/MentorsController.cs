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

    [HttpGet("/mentors/{mentorId:int}/confirm")]
    public async Task<IActionResult> Confirm(int mentorId, [FromQuery] int? slotId, CancellationToken cancellationToken)
    {
        try
        {
            var detail = await Api.GetMentorDetailAsync(mentorId, cancellationToken);
            var shell = LearnerShell.Get(HttpContext);
            var balance = shell?.CreditBalance ?? 0;

            MentorSlotDto? selectedSlot = null;
            if (detail.Mentor.Type == "human")
            {
                if (!slotId.HasValue)
                {
                    TempData["ShellError"] = "Please select a time slot to book.";
                    return RedirectToAction(nameof(Detail), new { mentorId });
                }

                selectedSlot = detail.AvailableSlots.FirstOrDefault(s => s.SlotId == slotId.Value);
                if (selectedSlot == null)
                {
                    var vm = new MentorConfirmViewModel(
                        detail.Mentor,
                        null,
                        balance,
                        ErrorMessage: "The selected time slot is no longer available. Please choose another slot.");
                    return View(vm);
                }
            }

            var viewModel = new MentorConfirmViewModel(
                detail.Mentor,
                selectedSlot,
                balance);

            return View(viewModel);
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load booking confirmation for mentor {MentorId}", mentorId);
            TempData["ShellError"] = "Could not load booking details.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("/mentors/{mentorId:int}/confirm")]
    [HttpPost("/mentors/{mentorId:int}/book")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmBooking(
        int mentorId,
        [FromForm] int? slotId,
        [FromForm] string? focusArea,
        [FromForm] int? examId,
        CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var request = new BookMentorRequest(slotId, focusArea, examId);
            var booking = await Api.BookMentorAsync(mentorId, request, bearer, cancellationToken);

            if (booking.MentorType == "ai" && !string.IsNullOrEmpty(booking.RedirectUrl) && booking.RedirectUrl.StartsWith("/") && !booking.RedirectUrl.StartsWith("//"))
            {
                return Redirect(booking.RedirectUrl);
            }

            TempData["ShellSuccess"] = "Booking confirmed!";
            return RedirectToAction(nameof(BookingDetail), new { bookingId = booking.BookingId });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.InsufficientCredits)
        {
            logger.LogWarning(ex, "Insufficient credits to book mentor {MentorId}", mentorId);
            var detail = await Api.GetMentorDetailAsync(mentorId, cancellationToken);
            var shell = LearnerShell.Get(HttpContext);
            var balance = shell?.CreditBalance ?? 0;
            var selectedSlot = slotId.HasValue ? detail.AvailableSlots.FirstOrDefault(s => s.SlotId == slotId.Value) : null;
            return View("Confirm", new MentorConfirmViewModel(
                detail.Mentor,
                selectedSlot,
                balance,
                focusArea,
                examId,
                ErrorMessage: $"Not enough credits. A mentor session costs {detail.Mentor.PriceCredits} credit(s) and you have {balance}. Nothing was deducted."));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.SlotNotAvailable)
        {
            logger.LogWarning(ex, "Slot {SlotId} not available for mentor {MentorId}", slotId, mentorId);
            var detail = await Api.GetMentorDetailAsync(mentorId, cancellationToken);
            var shell = LearnerShell.Get(HttpContext);
            var balance = shell?.CreditBalance ?? 0;
            return View("Confirm", new MentorConfirmViewModel(
                detail.Mentor,
                null,
                balance,
                focusArea,
                examId,
                ErrorMessage: "Booking failed. The time slot may no longer be available. No credit was deducted. Pick a different slot."));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to book mentor {MentorId}", mentorId);
            var detail = await Api.GetMentorDetailAsync(mentorId, cancellationToken);
            var shell = LearnerShell.Get(HttpContext);
            var balance = shell?.CreditBalance ?? 0;
            var selectedSlot = slotId.HasValue ? detail.AvailableSlots.FirstOrDefault(s => s.SlotId == slotId.Value) : null;
            return View("Confirm", new MentorConfirmViewModel(
                detail.Mentor,
                selectedSlot,
                balance,
                focusArea,
                examId,
                ErrorMessage: ex.Detail ?? "Booking failed. Please try again."));
        }
    }

    [HttpGet("/mentors/bookings")]
    public async Task<IActionResult> Bookings(CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var bookings = await Api.GetLearnerBookingsAsync(bearer, cancellationToken);
            return View(new LearnerBookingsViewModel(bookings));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load learner bookings");
            return View(new LearnerBookingsViewModel([], "Could not load bookings. Please try again."));
        }
    }

    [HttpGet("/mentors/bookings/{bookingId:int}")]
    public async Task<IActionResult> BookingDetail(int bookingId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var booking = await Api.GetBookingDetailAsync(bookingId, bearer, cancellationToken);
            return View(new MentorBookingDetailViewModel(
                booking,
                SuccessMessage: TempData["ShellSuccess"] as string,
                ErrorMessage: TempData["ShellError"] as string));
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load booking {BookingId}", bookingId);
            TempData["ShellError"] = "Could not load booking details.";
            return RedirectToAction(nameof(Bookings));
        }
    }

    [HttpPost("/mentors/bookings/{bookingId:int}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelBooking(int bookingId, CancellationToken cancellationToken)
    {
        try
        {
            var bearer = await BearerAsync();
            var result = await Api.CancelBookingAsync(bookingId, bearer, cancellationToken);
            TempData["ShellSuccess"] = "Your booking was cancelled and your credit has been refunded.";
            return RedirectToAction(nameof(BookingDetail), new { bookingId });
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to cancel booking {BookingId}", bookingId);
            TempData["ShellError"] = ex.Detail ?? "Could not cancel booking.";
            return RedirectToAction(nameof(BookingDetail), new { bookingId });
        }
    }
}
