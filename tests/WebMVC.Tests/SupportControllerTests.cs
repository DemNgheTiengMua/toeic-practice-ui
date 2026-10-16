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

public sealed class SupportControllerTests
{
    private static EligibleSubjectsDto SampleEligibleSubjects() => new(
        Bookings:
        [
            new(8821, "Dr. Tran Minh Khoa", DateTimeOffset.UtcNow.AddDays(-2), "done"),
        ],
        Attempts:
        [
            new(1176, "TOEIC Full Test 1", DateTimeOffset.UtcNow.AddDays(-1), "graded", 850),
        ],
        Orders:
        [
            new(1042, "DH-20260919-0122", "5-credit package", 199000, DateTimeOffset.UtcNow.AddDays(-3), "paid"),
        ]);

    private static SupportController CreateController(TestSupportApiClient api)
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

        return new SupportController(api, currentUser, NullLogger<SupportController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            },
            TempData = new TempDataDictionary(httpContext, new EmptyTempDataProvider()),
        };
    }

    [Fact]
    public async Task Report_get_returns_view_with_eligible_subjects()
    {
        var api = new TestSupportApiClient
        {
            EligibleSubjects = SampleEligibleSubjects(),
        };
        var controller = CreateController(api);

        var result = await controller.Report();

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SupportReportViewModel>(viewResult.Model);
        Assert.Single(vm.EligibleSubjects.Bookings);
        Assert.Single(vm.EligibleSubjects.Attempts);
        Assert.Single(vm.EligibleSubjects.Orders);
    }

    [Fact]
    public async Task SubmitReport_with_valid_booking_redirects_to_history_with_flash_message()
    {
        var api = new TestSupportApiClient
        {
            EligibleSubjects = SampleEligibleSubjects(),
        };
        var controller = CreateController(api);

        var result = await controller.SubmitReport(
            category: "mentor",
            type: "mentor_no_show",
            bookingId: 8821,
            attemptId: null,
            orderId: null,
            reason: "Mentor was absent from the meeting.");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SupportController.History), redirect.ActionName);
        Assert.NotNull(controller.TempData["Success"]);
        Assert.NotNull(api.LastFiledRequest);
        Assert.Equal(ComplaintTypeDto.MentorNoShow, api.LastFiledRequest.Type);
        Assert.Equal(8821, api.LastFiledRequest.BookingId);
    }

    [Fact]
    public async Task SubmitReport_with_missing_reason_renders_form_with_validation_error()
    {
        var api = new TestSupportApiClient
        {
            EligibleSubjects = SampleEligibleSubjects(),
        };
        var controller = CreateController(api);

        var result = await controller.SubmitReport(
            category: "mentor",
            type: "mentor_no_show",
            bookingId: 8821,
            attemptId: null,
            orderId: null,
            reason: "   "); // whitespace

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SupportReportViewModel>(viewResult.Model);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("describe the problem", vm.ErrorMessage);
        Assert.Null(api.LastFiledRequest);
    }

    [Fact]
    public async Task SubmitReport_with_missing_subject_renders_form_with_validation_error()
    {
        var api = new TestSupportApiClient
        {
            EligibleSubjects = SampleEligibleSubjects(),
        };
        var controller = CreateController(api);

        var result = await controller.SubmitReport(
            category: "mentor",
            type: "mentor_no_show",
            bookingId: null, // missing booking
            attemptId: null,
            orderId: null,
            reason: "No session selected");

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SupportReportViewModel>(viewResult.Model);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("choose a session", vm.ErrorMessage);
        Assert.Null(api.LastFiledRequest);
    }

    [Fact]
    public async Task SubmitReport_when_api_throws_displays_error_message()
    {
        var api = new TestSupportApiClient
        {
            EligibleSubjects = SampleEligibleSubjects(),
            ThrowOnSubmit = new ApiException(ApiError.Codes.ComplaintInvalidSubject, "Invalid subject", 400, "The referenced booking does not belong to you."),
        };
        var controller = CreateController(api);

        var result = await controller.SubmitReport(
            category: "mentor",
            type: "mentor_no_show",
            bookingId: 8821,
            attemptId: null,
            orderId: null,
            reason: "Testing error handling");

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SupportReportViewModel>(viewResult.Model);
        Assert.Equal("The referenced booking does not belong to you.", vm.ErrorMessage);
    }

    [Fact]
    public async Task History_returns_view_with_complaint_list()
    {
        var api = new TestSupportApiClient
        {
            Complaints =
            [
                new(1, ComplaintTypeDto.MentorNoShow, "Mentor was absent", ComplaintStatusDto.Open, null, null, null, null, DateTimeOffset.UtcNow, 8821, null, null, "Booking #8821"),
                new(2, ComplaintTypeDto.PaymentIssue, "Duplicate charge", ComplaintStatusDto.Resolved, "Refunded and credited", DateTimeOffset.UtcNow, 10, 1, DateTimeOffset.UtcNow.AddDays(-1), null, null, 1042, "Order DH-1042"),
            ],
        };
        var controller = CreateController(api);

        var result = await controller.History();

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SupportHistoryViewModel>(viewResult.Model);
        Assert.Equal(2, vm.Complaints.Count);
    }

    [Fact]
    public async Task SubmitReport_with_valid_attempt_redirects_to_history()
    {
        var api = new TestSupportApiClient
        {
            EligibleSubjects = SampleEligibleSubjects(),
        };
        var controller = CreateController(api);

        var result = await controller.SubmitReport(
            category: "exam",
            type: "grading_issue",
            bookingId: null,
            attemptId: 1176,
            orderId: null,
            reason: "Score seems lower than rubric guidelines.");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SupportController.History), redirect.ActionName);
        Assert.NotNull(api.LastFiledRequest);
        Assert.Equal(ComplaintTypeDto.GradingIssue, api.LastFiledRequest.Type);
        Assert.Equal(1176, api.LastFiledRequest.AttemptId);
        Assert.Null(api.LastFiledRequest.BookingId);
    }

    [Fact]
    public async Task SubmitReport_with_valid_order_redirects_to_history()
    {
        var api = new TestSupportApiClient
        {
            EligibleSubjects = SampleEligibleSubjects(),
        };
        var controller = CreateController(api);

        var result = await controller.SubmitReport(
            category: "payment",
            type: "payment_issue",
            bookingId: null,
            attemptId: null,
            orderId: 1042,
            reason: "Bank deduction succeeded but credits delayed.");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SupportController.History), redirect.ActionName);
        Assert.NotNull(api.LastFiledRequest);
        Assert.Equal(ComplaintTypeDto.PaymentIssue, api.LastFiledRequest.Type);
        Assert.Equal(1042, api.LastFiledRequest.OrderId);
        Assert.Null(api.LastFiledRequest.BookingId);
    }

    [Fact]
    public async Task Report_get_with_unknown_category_and_type_safely_falls_back()
    {
        var api = new TestSupportApiClient
        {
            EligibleSubjects = SampleEligibleSubjects(),
        };
        var controller = CreateController(api);

        var result = await controller.Report(category: "unknown_cat", type: "unknown_type");

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SupportReportViewModel>(viewResult.Model);
        Assert.Equal("mentor", vm.Category);
        Assert.Equal("mentor_no_show", vm.Type);
    }

    [Fact]
    public async Task SubmitReport_with_excessive_reason_length_renders_validation_error()
    {
        var api = new TestSupportApiClient
        {
            EligibleSubjects = SampleEligibleSubjects(),
        };
        var controller = CreateController(api);

        var result = await controller.SubmitReport(
            category: "mentor",
            type: "mentor_no_show",
            bookingId: 8821,
            attemptId: null,
            orderId: null,
            reason: new string('x', 4001));

        var viewResult = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<SupportReportViewModel>(viewResult.Model);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("4000 characters", vm.ErrorMessage);
        Assert.Null(api.LastFiledRequest);
    }

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestSupportApiClient : ISupportClient
    {
        public EligibleSubjectsDto? EligibleSubjects { get; set; }
        public IReadOnlyList<LearnerComplaintDto>? Complaints { get; set; }
        public FileComplaintRequest? LastFiledRequest { get; set; }
        public Exception? ThrowOnSubmit { get; set; }

        public Task<EligibleSubjectsDto> GetEligibleSubjectsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(EligibleSubjects ?? SampleEligibleSubjects());

        public Task<LearnerComplaintDto> FileComplaintAsync(FileComplaintRequest request, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSubmit != null) throw ThrowOnSubmit;
            LastFiledRequest = request;
            return Task.FromResult(new LearnerComplaintDto(
                100, request.Type, request.Reason, ComplaintStatusDto.Open, null, null, null, null, DateTimeOffset.UtcNow, request.BookingId, request.AttemptId, request.OrderId, "Summary"));
        }

        public Task<IReadOnlyList<LearnerComplaintDto>> GetLearnerComplaintsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Complaints ?? []);
    }
}
