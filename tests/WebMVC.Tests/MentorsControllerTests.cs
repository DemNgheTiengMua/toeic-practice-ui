using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC.Controllers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

public sealed class MentorsControllerTests
{
    private static MentorSummaryDto SampleHumanMentor(int id = 1) =>
        new(
            id,
            10,
            "Dr. Trần Minh Khoa",
            "10 years teaching TOEIC.",
            1,
            "TOEIC",
            "TOEIC Listening & Reading",
            1,
            "LIST",
            "Listening",
            "listening",
            "human",
            200000m,
            1,
            "10 years teaching TOEIC",
            "Listening strategies, pronunciation",
            60,
            true,
            4.5m,
            20);

    private static MentorSummaryDto SampleAiMentor(int id = 2) =>
        new(
            id,
            null,
            "AI Mentor — TOEIC Coach",
            "AI speaking practice available 24/7.",
            1,
            "TOEIC",
            "TOEIC Listening & Reading",
            null,
            null,
            null,
            null,
            "ai",
            0m,
            1,
            "AI speaking practice 24/7",
            "Instant speaking feedback",
            60,
            true,
            5.0m,
            120);

    private static MentorsController CreateController(TestApiClient api)
    {
        var identity = new ClaimsIdentity(AuthClaims.For(7, "learner@test.local", Roles.Learner, "Learner"), "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" });

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddMvcCore();
        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal, properties));
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = serviceProvider };
        LearnerShell.Set(httpContext, new LearnerContextDto(new CertificateOptionDto("TOEIC", "TOEIC"), [new CertificateOptionDto("TOEIC", "TOEIC")], 8));
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });

        var controller = new MentorsController(api, currentUser, NullLogger<MentorsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider()),
        };

        return controller;
    }

    [Fact]
    public async Task Index_returns_view_with_mentor_list()
    {
        var api = new TestApiClient
        {
            Mentors = [SampleHumanMentor(1), SampleAiMentor(2)],
        };

        var controller = CreateController(api);
        var result = await controller.Index(null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorListViewModel>(viewResult.Model);
        Assert.Equal(2, model.Mentors.Count);
        Assert.Equal("TOEIC", model.ActiveCertCode);
        Assert.Null(model.ActiveSkill);
    }

    [Fact]
    public async Task Index_with_skill_filter_passes_filter_to_api()
    {
        var api = new TestApiClient
        {
            Mentors = [SampleHumanMentor(1)],
        };

        var controller = CreateController(api);
        var result = await controller.Index("listening", CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorListViewModel>(viewResult.Model);
        Assert.Single(model.Mentors);
        Assert.Equal("listening", api.LastSkillQuery);
        Assert.Equal("listening", model.ActiveSkill);
    }

    [Fact]
    public async Task Index_handles_api_exception_gracefully()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException("ERROR", "API Failure", 500),
        };

        var controller = CreateController(api);
        var result = await controller.Index(null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorListViewModel>(viewResult.Model);
        Assert.Empty(model.Mentors);
        Assert.NotNull(model.ErrorMessage);
    }

    [Fact]
    public async Task Detail_returns_view_with_mentor_detail()
    {
        var futureSlot = new MentorSlotDto(1, 1, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1), "open");
        var review = new MentorReviewDto(1, 5, "Great teacher!", "Demo Learner", DateTimeOffset.UtcNow.AddDays(-2));

        var api = new TestApiClient
        {
            Detail = new MentorDetailDto(SampleHumanMentor(1), [futureSlot], [review]),
        };

        var controller = CreateController(api);
        var result = await controller.Detail(1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorDetailViewModel>(viewResult.Model);
        Assert.Equal("Dr. Trần Minh Khoa", model.Detail.Mentor.DisplayName);
        Assert.Single(model.Detail.AvailableSlots);
        Assert.Single(model.Detail.Reviews);
    }

    [Fact]
    public async Task Detail_returns_not_found_when_mentor_does_not_exist()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.NotFound, "Not Found", 404),
        };

        var controller = CreateController(api);
        var result = await controller.Detail(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Detail_redirects_to_index_on_unexpected_error()
    {
        var api = new TestApiClient
        {
            ExceptionToThrow = new ApiException(ApiError.Codes.Unexpected, "Error", 500),
        };

        var controller = CreateController(api);
        var result = await controller.Detail(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MentorsController.Index), redirect.ActionName);
        Assert.NotNull(controller.TempData["ShellError"]);
    }

    [Fact]
    public async Task Confirm_returns_view_with_slot_and_balance()
    {
        var futureSlot = new MentorSlotDto(1, 1, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1), "open");
        var api = new TestApiClient
        {
            Detail = new MentorDetailDto(SampleHumanMentor(1), [futureSlot], []),
        };

        var controller = CreateController(api);
        var result = await controller.Confirm(1, 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorConfirmViewModel>(viewResult.Model);
        Assert.Equal(1, model.Mentor.MentorId);
        Assert.NotNull(model.SelectedSlot);
        Assert.Equal(1, model.SelectedSlot.SlotId);
        Assert.Equal(8, model.CreditBalance);
        Assert.False(model.IsPaywall);
    }

    [Fact]
    public async Task Confirm_displays_paywall_when_insufficient_credits()
    {
        var futureSlot = new MentorSlotDto(1, 1, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1), "open");
        var expensiveMentor = SampleHumanMentor(1) with { PriceCredits = 10 };
        var api = new TestApiClient
        {
            Detail = new MentorDetailDto(expensiveMentor, [futureSlot], []),
        };

        var controller = CreateController(api); // user has 8 credits, mentor costs 10
        var result = await controller.Confirm(1, 1, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorConfirmViewModel>(viewResult.Model);
        Assert.True(model.IsPaywall);
        Assert.Equal(2, model.ShortBy);
    }

    [Fact]
    public async Task Confirm_redirects_to_detail_if_slot_missing_for_human_mentor()
    {
        var api = new TestApiClient
        {
            Detail = new MentorDetailDto(SampleHumanMentor(1), [], []),
        };

        var controller = CreateController(api);
        var result = await controller.Confirm(1, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MentorsController.Detail), redirect.ActionName);
    }

    [Fact]
    public async Task ConfirmBooking_redirects_to_booking_detail_for_human_mentor()
    {
        var api = new TestApiClient
        {
            BookingResult = new MentorBookingDto(
                101, 7, 1, "Dr. Trần Minh Khoa", "Headline", "human", 1,
                DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
                "confirmed", "https://meet.google.com/abc-defg-hij", "Listening", 1, null, DateTimeOffset.UtcNow),
        };

        var controller = CreateController(api);
        var result = await controller.ConfirmBooking(1, 1, "Listening", null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MentorsController.BookingDetail), redirect.ActionName);
        Assert.Equal(101, redirect.RouteValues?["bookingId"]);
        Assert.Equal("Booking confirmed!", controller.TempData["ShellSuccess"]);
    }

    [Fact]
    public async Task ConfirmBooking_redirects_to_speaking_attempt_for_ai_mentor()
    {
        var api = new TestApiClient
        {
            BookingResult = new MentorBookingDto(
                102, 7, 2, "AI Coach", "Headline", "ai", null,
                null, null, "confirmed", null, null, 1, null, DateTimeOffset.UtcNow,
                AttemptId: 55, RedirectUrl: "/speaking/take/55"),
        };

        var controller = CreateController(api);
        var result = await controller.ConfirmBooking(2, null, null, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/speaking/take/55", redirect.Url);
    }

    [Fact]
    public async Task ConfirmBooking_redisplays_confirm_view_when_insufficient_credits()
    {
        var futureSlot = new MentorSlotDto(1, 1, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1), "open");
        var api = new TestApiClient
        {
            Detail = new MentorDetailDto(SampleHumanMentor(1), [futureSlot], []),
            BookExceptionToThrow = new ApiException(ApiError.Codes.InsufficientCredits, "Not enough credits", 400),
        };

        var controller = CreateController(api);
        var result = await controller.ConfirmBooking(1, 1, null, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Confirm", viewResult.ViewName);
        var model = Assert.IsType<MentorConfirmViewModel>(viewResult.Model);
        Assert.NotNull(model.ErrorMessage);
        Assert.Contains("Not enough credits", model.ErrorMessage);
    }

    [Fact]
    public async Task ConfirmBooking_redisplays_confirm_view_when_slot_not_available()
    {
        var api = new TestApiClient
        {
            Detail = new MentorDetailDto(SampleHumanMentor(1), [], []),
            BookExceptionToThrow = new ApiException(ApiError.Codes.SlotNotAvailable, "Slot not available", 400),
        };

        var controller = CreateController(api);
        var result = await controller.ConfirmBooking(1, 1, null, null, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Confirm", viewResult.ViewName);
        var model = Assert.IsType<MentorConfirmViewModel>(viewResult.Model);
        Assert.NotNull(model.ErrorMessage);
        Assert.Contains("no longer be available", model.ErrorMessage);
    }

    [Fact]
    public async Task Bookings_returns_view_with_learner_bookings()
    {
        var booking = new MentorBookingDto(
            101, 7, 1, "Dr. Trần Minh Khoa", "Headline", "human", 1,
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            "confirmed", "https://meet.google.com/abc-defg-hij", "Listening", 1, null, DateTimeOffset.UtcNow);

        var api = new TestApiClient
        {
            BookingsList = [booking],
        };

        var controller = CreateController(api);
        var result = await controller.Bookings(CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LearnerBookingsViewModel>(viewResult.Model);
        Assert.Single(model.Bookings);
        Assert.Equal(101, model.Bookings[0].BookingId);
    }

    [Fact]
    public async Task BookingDetail_returns_view_with_booking_details()
    {
        var booking = new MentorBookingDto(
            101, 7, 1, "Dr. Trần Minh Khoa", "Headline", "human", 1,
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            "confirmed", "https://meet.google.com/abc-defg-hij", "Listening", 1, null, DateTimeOffset.UtcNow);

        var api = new TestApiClient
        {
            BookingResult = booking,
        };

        var controller = CreateController(api);
        var result = await controller.BookingDetail(101, CancellationToken.None);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MentorBookingDetailViewModel>(viewResult.Model);
        Assert.Equal(101, model.Booking.BookingId);
        Assert.Equal("https://meet.google.com/abc-defg-hij", model.Booking.MeetLink);
    }

    [Fact]
    public async Task CancelBooking_cancels_and_redirects_to_booking_detail()
    {
        var api = new TestApiClient
        {
            CancelResult = new CancelBookingResultDto(101, "cancelled", DateTimeOffset.UtcNow, 1),
        };

        var controller = CreateController(api);
        var result = await controller.CancelBooking(101, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MentorsController.BookingDetail), redirect.ActionName);
        Assert.Equal(101, redirect.RouteValues?["bookingId"]);
        Assert.NotNull(controller.TempData["ShellSuccess"]);
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class FixedAuthentication(ClaimsPrincipal principal, AuthenticationProperties properties) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, "Cookies")));

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? props) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? props) => Task.CompletedTask;
    }

    private sealed class TestApiClient : ICompanionApiClient
    {
        public IReadOnlyList<MentorSummaryDto> Mentors { get; set; } = [];
        public MentorDetailDto? Detail { get; set; }
        public MentorBookingDto? BookingResult { get; set; }
        public CancelBookingResultDto? CancelResult { get; set; }
        public IReadOnlyList<MentorBookingDto> BookingsList { get; set; } = [];
        public string? LastSkillQuery { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public Exception? BookExceptionToThrow { get; set; }

        public Task<IReadOnlyList<MentorSummaryDto>> GetMentorsAsync(string? certCode = null, string? skill = null, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            LastSkillQuery = skill;
            return Task.FromResult(Mentors);
        }

        public Task<MentorDetailDto> GetMentorDetailAsync(int mentorId, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(Detail!);
        }

        public Task<MentorBookingDto> BookMentorAsync(int mentorId, BookMentorRequest request, string bearerToken, CancellationToken cancellationToken = default)
        {
            if (BookExceptionToThrow is not null) throw BookExceptionToThrow;
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(BookingResult!);
        }

        public Task<CancelBookingResultDto> CancelBookingAsync(int bookingId, string bearerToken, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(CancelResult!);
        }

        public Task<MentorBookingDto> GetBookingDetailAsync(int bookingId, string bearerToken, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(BookingResult!);
        }

        public Task<IReadOnlyList<MentorBookingDto>> GetLearnerBookingsAsync(string bearerToken, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return Task.FromResult(BookingsList);
        }
    }
}
