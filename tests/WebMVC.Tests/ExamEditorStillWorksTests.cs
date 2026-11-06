using System.Linq;
using System.Net;
using System.Net.Http.Json;
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
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #188 AC4: the existing exam editor still opens, saves and previews. The pool gained its own door
/// and nothing on the legacy path should have moved, so this drives the three routes a human
/// actually uses — <c>GET /admin/exams/{id}/edit</c>, <c>PUT .../studio/question/{qid}</c> and
/// <c>GET .../preview</c> — against the real host, rather than asserting a method exists.
/// <para>
/// It is deliberately a separate fixture from <see cref="ExamEditorPublishControlTests"/>: that one
/// throws on any unexpected upstream call, which is how it proves the editor reads nothing it was
/// not meant to; this one has to answer more calls.
/// </para>
/// </summary>
[Collection(AppHostCollection.Name)]
public sealed class ExamEditorStillWorksTests : IClassFixture<ExamEditorStillWorksTests.Site>
{
    private readonly Site site;

    public ExamEditorStillWorksTests(Site site) => this.site = site;

    [Fact]
    public async Task The_editor_opens_with_its_own_form()
    {
        using var client = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", site.AdminCookie());

        var response = await client.GetAsync("/admin/exams/1/edit?step=1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"editor answered {(int)response.StatusCode}");
        // The form the ticket means by "opens": the step-1 metadata fields, populated from the exam.
        Assert.Contains("Editor paper", html, StringComparison.Ordinal);
        Assert.Contains(@"name=""Name""", html, StringComparison.Ordinal);

        // …and step 3 is the readiness step, which reads the exam's progress rather than questions.
        var step3 = await client.GetAsync("/admin/exams/1/edit?step=3");
        Assert.True(step3.StatusCode == HttpStatusCode.OK, $"step 3 answered {(int)step3.StatusCode}");
        Assert.Contains("Question entry progress", await step3.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_editor_saves_a_question_through_its_autosave_route()
    {
        using var client = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", site.AdminCookie());
        var token = await site.AntiforgeryTokenAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Put, "/admin/exams/1/studio/question/101")
        {
            Content = JsonContent.Create(new UpdateQuestionRequest(
                GroupId: 11,
                Stem: "A saved stem",
                QuestionType: QuestionTypeDto.Mcq,
                Explanation: "Saved from the editor.", CefrLevelCode: "B1")),
        };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"autosave answered {(int)response.StatusCode}");
        var dto = await response.Content.ReadFromJsonAsync<AdminQuestionDto>();
        Assert.NotNull(dto);
        Assert.Equal("A saved stem", dto.Stem);
        Assert.Equal(101, site.Stub.LastSavedQuestionId);
    }

    /// <summary>
    /// The criterion the ticket calls out: the preview must render, through the real action and the
    /// learner's own sitting view — a 200 with the sitting shell, not a redirect to the list.
    /// </summary>
    [Fact]
    public async Task The_editor_preview_renders_the_sitting_view_for_a_draft()
    {
        using var client = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", site.AdminCookie());

        var response = await client.GetAsync("/admin/exams/1/preview?q=1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"preview answered {(int)response.StatusCode}");
        // The learner's sitting markup plus the preview placard, which only the preview partial renders.
        Assert.Contains("preview-answer-toggle", html, StringComparison.Ordinal);
        Assert.Contains("exam-sitting", html, StringComparison.Ordinal);
        // And the key the preview was given, so the page is the real preview and not an empty shell.
        Assert.Contains("A is the only grammatical fit.", html, StringComparison.Ordinal);
    }

    public sealed class Site : WebApplicationFactory<Program>
    {
        private Upstream? stub;

        /// <summary>Built by <see cref="ConfigureWebHost"/>, so it is the very instance the app has.</summary>
        public Upstream Stub => stub
            ?? throw new InvalidOperationException("Create a client first: the host is built lazily.");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ApiBaseUrl", "http://localhost:5001");
            stub = Upstream.Create();
            var client = stub.Client;
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ICompanionApiClient>(client));
        }

        public string AdminCookie()
        {
            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                AuthClaims.For(1, "admin@test.local", Roles.Admin, "Admin", Permissions.All),
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

        /// <summary>Reads a real anti-forgery token out of the page that hosts one.</summary>
        public async Task<string> AntiforgeryTokenAsync(HttpClient client)
        {
            var html = await (await client.GetAsync("/admin/exams/1/studio")).Content.ReadAsStringAsync();
            var match = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
            Assert.True(match.Success, "the studio page must carry an anti-forgery token for the autosave routes");
            return match.Groups[1].Value;
        }
    }

    /// <summary>
    /// The API behind the editor: one reading section with one question, and a preview that renders
    /// that question with the answer key the preview toggle reveals.
    /// </summary>
    public class Upstream : DispatchProxy
    {
        public ICompanionApiClient Client { get; private set; } = null!;
        public int? LastSavedQuestionId { get; private set; }

        private static readonly AttemptQuestionDto Question = new(
            101, 1, 1, 2, "P5", "Incomplete Sentences", "READ", "Reading", ModalityDto.Reading,
            "Depot notice ------- at six.", null, null, null, null, null, null, null,
            [new AttemptOptionDto(1, "A", "opens"), new AttemptOptionDto(2, "B", "opened")],
            null, false);

        private static AttemptTakeDto Take() => new(
            AttemptId: 0, ExamId: 1, "Editor paper", "TOEIC", AttemptStatusDto.InProgress,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), 3600,
            1, 0, 0, Question,
            [new NavigatorItemDto(101, 1, 2, "P5", ModalityDto.Reading, false, false, true, IsLocked: false)],
            false,
            Questions: [Question],
            Phases: [new AttemptPhaseDto(1, "READ", "Reading", 1, 0, 3600)],
            ActiveSkillId: 1);

        private static AdminExamDetailDto Detail() => new(
            ExamId: 1, CertId: 1, CertCode: "toeic", CertName: "TOEIC",
            Name: "Editor paper", Status: ExamStatusDto.Draft, DurationMinutes: 60,
            Purpose: ExamPurposeDto.Mock, Description: null, SourceName: null, InternalNote: null,
            QuestionCount: 1, ActualQuestionCount: 1, FinishedAttemptCount: 0,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null,
            Audios: [],
            SectionProgress: [new AdminExamSectionProgressDto(2, "P5", "Incomplete Sentences", ModalityDto.Reading, 1, 1, true, true, "Complete")]);

        private static AdminExamQuestionsDto Questions() => new(
            ExamId: 1, ExamName: "Editor paper", CertId: 1, CertCode: "toeic",
            Groups:
            [
                new(11, 1, 1, 2, "P5", "Incomplete Sentences", ModalityDto.Reading,
                    Passage: "Depot notice ------- at six.",
                    AudioPath: null, AudioStartMs: null, AudioEndMs: null, ImagePath: null, ImageAlt: null,
                    Transcript: null, PrepSeconds: null, ResponseSeconds: null, DisplayOrder: 1,
                    Questions:
                    [new AdminQuestionDto(
                        101, 11, 1, 2, "P5", "Incomplete Sentences", 2, "READ",
                        "Depot notice ------- at six.", QuestionTypeDto.Mcq,
                        "A is the only grammatical fit.", null, null, null, null, null, null, null, 1,
                        DateTimeOffset.UtcNow,
                        [new AdminQuestionOptionDto(1, 101, "A", "opens", true, null),
                         new AdminQuestionOptionDto(2, 101, "B", "opened", false, null)],
                        HasAnswers: false)],
                    UnitNumber: 1),
            ],
            AvailableSections: [new AdminSectionOptionDto(2, "P5", "Incomplete Sentences", ModalityDto.Reading, 1, 1)],
            AvailableCategories: []);

        private static ExamStudioDetailDto Studio() => new(
            ExamId: 1, CertId: 1, CertCode: "toeic", CertName: "TOEIC",
            Name: "Editor paper", Status: ExamStatusDto.Draft, Purpose: ExamPurposeDto.Mock,
            DurationMinutes: 60, Description: null, SourceName: null, InternalNote: null,
            QuestionCount: 1, ActualQuestionCount: 1, FinishedAttemptCount: 0,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: null,
            Sections:
            [
                new ExamStudioSectionDto(
                    SectionId: 2, CertId: 1, Code: "P5", Name: "Incomplete Sentences",
                    Modality: ModalityDto.Reading, ExpectedQuestionCount: 1, DisplayOrder: 1,
                    CurrentQuestionCount: 1, HasAudio: false, MasterAudioPath: null, MasterAudioDurationSec: null,
                    Groups: [],
                    OptionCount: 4, ClusterSize: 1, UnitCount: 1, HideOptionText: false,
                    RequirePassage: true, RequireAudio: false, RequireImage: false, RequiredKeywordCount: 0,
                    AllowedComponentTypesJson: null, LayoutType: "STANDARD_VERTICAL",
                    NavigationMode: "FULL_SANDBOX", TimerPolicyJson: null, ToolsConfigJson: null,
                    SkillId: 2, SkillCode: "READ", SkillName: "Reading"),
            ],
            AvailableCategories: [], AvailableCriteria: [],
            Audit: new ExamAuditResultDto(true, 1, 1, 0, 0, [], []));

        public static Upstream Create()
        {
            var proxy = DispatchProxy.Create<ICompanionApiClient, Upstream>();
            var stub = (Upstream)(object)proxy;
            stub.Client = proxy;
            return stub;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IAuthClient.GetMeAsync):
                    return Task.FromResult(new AuthUserDto(1, "admin@test.local", "Admin", Roles.Admin, true, Permissions.All));
                case nameof(IProfileClient.GetProfileAsync):
                    return Task.FromResult(new ProfileDto("Admin", null, "Asia/Ho_Chi_Minh"));
                case nameof(IAdminExamsClient.GetAdminExamByIdAsync):
                    return Task.FromResult(Detail());
                case nameof(IAdminExamsClient.GetAdminExamQuestionsAsync):
                    return Task.FromResult(Questions());
                case nameof(IAdminExamsClient.GetAdminExamStudioAsync):
                    return Task.FromResult(Studio());
                case nameof(IAdminExamsClient.GetAdminExamAuditAsync):
                    return Task.FromResult(new ExamAuditResultDto(true, 1, 1, 0, 0, [], []));
                case nameof(IAdminExamsClient.GetAdminExamsAsync):
                    return Task.FromResult(new AdminExamListSummaryDto([], 0, 1, 20, []));
                case nameof(IAdminExamsClient.UpdateAdminQuestionAsync):
                    LastSavedQuestionId = (int)args![0]!;
                    var request = (UpdateQuestionRequest)args[1]!;
                    return Task.FromResult(new AdminQuestionDto(
                        101, request.GroupId ?? 11, 1, 2, "P5", "Incomplete Sentences", 2, "READ",
                        request.Stem, request.QuestionType, request.Explanation,
                        null, null, null, null, null, null, null, request.DisplayOrder, DateTimeOffset.UtcNow,
                        [new AdminQuestionOptionDto(1, 101, "A", "opens", true, null)],
                        HasAnswers: false));
                case nameof(IAdminExamsClient.GetAdminExamPreviewAsync):
                    var take = Take();
                    return Task.FromResult(new ExamPreviewTakeDto(
                        take,
                        [new ExamPreviewAnswerDto(101, [1], "A is the only grammatical fit.")],
                        [new ExamPreviewSectionDto(2, "P5", "Incomplete Sentences", 1, 1, 1)],
                        60,
                        "draft"));
                default:
                    throw new InvalidOperationException($"Unexpected upstream call: {targetMethod?.Name}");
            }
        }
    }
}
