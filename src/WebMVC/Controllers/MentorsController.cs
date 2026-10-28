using Contracts;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public sealed class MentorsController(
    IMentorsClient api,
    CurrentUser currentUser,
    ILogger<MentorsController> logger)
    : LearnerControllerBase(currentUser, logger)
{
    private DateTimeFormatter Dates => HttpContext.RequestServices?.GetService(typeof(DateTimeFormatter)) as DateTimeFormatter
        ?? new(new HttpContextAccessor { HttpContext = HttpContext });

    [HttpGet("/mentors")]
    public async Task<IActionResult> Index([FromQuery] string? skill, CancellationToken cancellationToken)
    {
        var shell = LearnerShell.Get(HttpContext);
        var activeCertCode = shell?.Active?.Code ?? "TOEIC";

        try
        {
            var mentors = await api.GetMentorsAsync(activeCertCode, skill, cancellationToken);
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
            var detail = await api.GetMentorDetailAsync(mentorId, cancellationToken);
            return View(new MentorDetailViewModel(detail).PrepareDates(Dates));
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
            var detail = await api.GetMentorDetailAsync(mentorId, cancellationToken);
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
                        ErrorMessage: "The selected time slot is no longer available. Please choose another slot.").PrepareDates(Dates);
                    return View(vm);
                }
            }

            var viewModel = new MentorConfirmViewModel(
                detail.Mentor,
                selectedSlot,
                balance).PrepareDates(Dates);

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
            var request = new BookMentorRequest(slotId, focusArea, examId);
            var booking = await api.BookMentorAsync(mentorId, request, cancellationToken);

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
            var detail = await api.GetMentorDetailAsync(mentorId, cancellationToken);
            var shell = LearnerShell.Get(HttpContext);
            var balance = shell?.CreditBalance ?? 0;
            var selectedSlot = slotId.HasValue ? detail.AvailableSlots.FirstOrDefault(s => s.SlotId == slotId.Value) : null;
            return View("Confirm", new MentorConfirmViewModel(
                detail.Mentor,
                selectedSlot,
                balance,
                focusArea,
                examId,
                ErrorMessage: $"Not enough credits. A mentor session costs {detail.Mentor.PriceCredits} credit(s) and you have {balance}. Nothing was deducted.").PrepareDates(Dates));
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.SlotNotAvailable)
        {
            logger.LogWarning(ex, "Slot {SlotId} not available for mentor {MentorId}", slotId, mentorId);
            var detail = await api.GetMentorDetailAsync(mentorId, cancellationToken);
            var shell = LearnerShell.Get(HttpContext);
            var balance = shell?.CreditBalance ?? 0;
            return View("Confirm", new MentorConfirmViewModel(
                detail.Mentor,
                null,
                balance,
                focusArea,
                examId,
                ErrorMessage: "Booking failed. The time slot may no longer be available. No credit was deducted. Pick a different slot.").PrepareDates(Dates));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to book mentor {MentorId}", mentorId);
            var detail = await api.GetMentorDetailAsync(mentorId, cancellationToken);
            var shell = LearnerShell.Get(HttpContext);
            var balance = shell?.CreditBalance ?? 0;
            var selectedSlot = slotId.HasValue ? detail.AvailableSlots.FirstOrDefault(s => s.SlotId == slotId.Value) : null;
            return View("Confirm", new MentorConfirmViewModel(
                detail.Mentor,
                selectedSlot,
                balance,
                focusArea,
                examId,
                ErrorMessage: ex.Detail ?? "Booking failed. Please try again.").PrepareDates(Dates));
        }
    }

    [HttpGet("/mentors/bookings")]
    public async Task<IActionResult> Bookings(CancellationToken cancellationToken)
    {
        try
        {
            var bookings = await api.GetLearnerBookingsAsync(cancellationToken);
            return View(new LearnerBookingsViewModel(bookings).PrepareDates(Dates));
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to load learner bookings");
            return View(new LearnerBookingsViewModel([], "Could not load bookings. Please try again.").PrepareDates(Dates));
        }
    }

    [HttpGet("/mentors/bookings/{bookingId:int}")]
    public async Task<IActionResult> BookingDetail(int bookingId, CancellationToken cancellationToken)
    {
        try
        {
            var booking = await api.GetBookingDetailAsync(bookingId, cancellationToken);
            return View(new MentorBookingDetailViewModel(
                booking,
                SuccessMessage: TempData["ShellSuccess"] as string,
                ErrorMessage: TempData["ShellError"] as string).PrepareDates(Dates));
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
            var result = await api.CancelBookingAsync(bookingId, cancellationToken);
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

    [HttpPost("/mentors/bookings/{bookingId:int}/rate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RateBooking(
        int bookingId,
        [FromForm] int rating,
        [FromForm] string? text,
        CancellationToken cancellationToken)
    {
        if (rating < 1 || rating > 5)
        {
            TempData["ShellError"] = "Rating must be between 1 and 5.";
            return RedirectToAction(nameof(BookingDetail), new { bookingId });
        }

        if (text is not null && text.Length > 2000)
        {
            TempData["ShellError"] = "Review comments cannot exceed 2000 characters.";
            return RedirectToAction(nameof(BookingDetail), new { bookingId });
        }

        try
        {
            var request = new RateMentorRequest(rating, text);
            await api.RateBookingAsync(bookingId, request, cancellationToken);
            TempData["ShellSuccess"] = "Thank you for rating your mentor session!";
            return RedirectToAction(nameof(BookingDetail), new { bookingId });
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound();
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.BookingAlreadyRated)
        {
            logger.LogWarning(ex, "Booking {BookingId} has already been rated", bookingId);
            TempData["ShellError"] = "This booking has already been rated.";
            return RedirectToAction(nameof(BookingDetail), new { bookingId });
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.BookingNotFinished)
        {
            logger.LogWarning(ex, "Booking {BookingId} is not finished", bookingId);
            TempData["ShellError"] = "Only completed bookings can be rated.";
            return RedirectToAction(nameof(BookingDetail), new { bookingId });
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to submit rating for booking {BookingId}", bookingId);
            TempData["ShellError"] = ex.Detail ?? "Could not submit rating. Please try again.";
            return RedirectToAction(nameof(BookingDetail), new { bookingId });
        }
    }
}
