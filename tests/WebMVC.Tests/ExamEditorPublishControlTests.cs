using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #157: publishing is POST /{id}/publish and nothing else. The exam editor's Status dropdown set
/// Published directly on a 0-question exam in one form post, which is exactly the audit bypass the
/// ticket closes.
/// <para>
/// The first test renders the real page through the real host, so a publish control that comes back
/// anywhere in the editor — including a partial this file has never heard of — fails it. The other
/// two name the shape that makes such a control impossible: no request model and no admin action
/// carries a status. The last is the cheap AC5 grep, kept because AC5 is stated as a grep.
/// </para>
/// </summary>
public sealed class ExamEditorPublishControlTests : IClassFixture<ExamEditorPublishControlTests.Site>
{
    private readonly Site site;

    public ExamEditorPublishControlTests(Site site) => this.site = site;

    [Fact]
    public async Task The_rendered_editor_offers_no_status_field()
    {
        using var client = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", site.AdminCookie());

        var html = await (await client.GetAsync("/admin/exams/1/edit?step=1")).Content.ReadAsStringAsync();

        Assert.DoesNotContain(@"name=""Status""", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"value=""published""", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_incomplete_exam_offers_no_enabled_publish_control_and_no_status_field()
    {
        using var client = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", site.AdminCookie());

        var response = await client.GetAsync("/admin/exams/1/edit?step=3");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected the editor page, got {(int)response.StatusCode} {response.StatusCode} -> {response.Headers.Location}");

        // AC1: the exam is two questions short of its declared count, so every publish control the
        // page does render is disabled. A second, enabled control in any partial fails here - the
        // list of publish controls comes from the rendered page, not from a file this test names.
        var publishControls = Regex.Matches(html, @"<button[^>]*>[\s\S]{0,200}?Publish exam",
            RegexOptions.Singleline);
        Assert.NotEmpty(publishControls);
        foreach (Match control in publishControls)
        {
            Assert.Contains("disabled", control.Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void No_exam_request_model_can_carry_a_status()
    {
        // A form control can only reach the server through one of these, so a status here is the
        // only way a view could publish; it is not there, which is why the dropdown could not be
        // replaced by one in another partial.
        foreach (var model in new[]
                 {
                     typeof(ExamFormViewModel),
                     typeof(CreateAdminExamRequest),
                     typeof(UpdateAdminExamRequest),
                     typeof(ExamBlueprintDto),
                 })
        {
            Assert.Null(model.GetProperty("Status", BindingFlags.Public | BindingFlags.Instance));
        }
    }

    [Fact]
    public void No_admin_exam_action_takes_a_status()
    {
        // Catches the other half: a new endpoint "that just sets the status" would need one. The
        // exam list's read-only filter is the single legitimate 'status' parameter, and it is not
        // reachable from the editor form.
        var offenders = typeof(WebMVC.Controllers.AdminExamsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p =>
                p.ParameterType == typeof(ExamStatusDto)
                || string.Equals(p.Name, "status", StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Name)
            .Distinct()
            .ToList();

        Assert.Equal([nameof(WebMVC.Controllers.AdminExamsController.Index)], offenders);
    }

    [Fact]
    public void Ac5_the_immediate_publish_option_is_gone_from_the_generate_dialog()
    {
        // A cheap grep kept deliberately, because AC5 is stated as a grep: the string must not
        // exist anywhere under src/, not only in this dialog.
        var root = TestPathHelper.FindRepoRoot();
        var hit = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src"), "*.js", SearchOption.AllDirectories))
            .FirstOrDefault(f => File.ReadAllText(f).Contains("Published (immediately live)", StringComparison.Ordinal));

        Assert.Null(hit);
    }

    /// <summary>The real WebMVC host with the upstream API replaced, so nothing leaves the process.</summary>
    public sealed class Site : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ICompanionApiClient>(Upstream.Create().Client));
        }

        public string AdminCookie()
        {
            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                AuthClaims.For(1, "admin@test.local", Roles.Admin, "Admin"),
                CookieAuthenticationDefaults.AuthenticationScheme,
                nameType: IAccessTokenFactory.ClaimEmail,
                roleType: IAccessTokenFactory.ClaimRole));
            var properties = new AuthenticationProperties(new Dictionary<string, string?> { ["jwt"] = "token" })
            {
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
            };
            var ticket = new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme);

            return $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}";
        }
    }

    /// <summary>
    /// The API behind the page: one TOEIC certificate, and one exam that is deliberately short of
    /// its declared question count, which is the state the ticket is about.
    /// </summary>
    public class Upstream : DispatchProxy
    {
        public ICompanionApiClient Client { get; private set; } = null!;

        public static Upstream Create()
        {
            var proxy = DispatchProxy.Create<ICompanionApiClient, Upstream>();
            var stub = (Upstream)(object)proxy;
            stub.Client = proxy;
            return stub;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var certs = new[] { new AdminCertificateOptionDto(1, "toeic", "TOEIC") };
            var detail = new AdminExamDetailDto(
                ExamId: 1, CertId: 1, CertCode: "toeic", CertName: "TOEIC",
                Name: "Empty exam", Status: ExamStatusDto.Draft, DurationMinutes: 120,
                Purpose: ExamPurposeDto.Mock, Description: null, SourceName: null, InternalNote: null,
                QuestionCount: 2, ActualQuestionCount: 0, FinishedAttemptCount: 0,
                CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null,
                Audios: [],
                SectionProgress: [new AdminExamSectionProgressDto(1, "P5", "Incomplete Sentences", ModalityDto.Reading, 0, 2, true, false, "2 questions missing")]);

            return targetMethod?.Name switch
            {
                nameof(IAuthClient.GetMeAsync) => Task.FromResult(new AuthUserDto(1, "admin@test.local", "Admin", Roles.Admin, true)),
                nameof(IProfileClient.GetProfileAsync) => Task.FromResult(new ProfileDto("Admin", null, "Asia/Ho_Chi_Minh")),
                nameof(IAdminExamsClient.GetAdminExamByIdAsync) => Task.FromResult(detail),
                nameof(IAdminExamsClient.GetAdminExamsAsync) =>
                    Task.FromResult(new AdminExamListSummaryDto([], 0, 1, 20, certs)),
                _ => throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}"),
            };
        }
    }
}
