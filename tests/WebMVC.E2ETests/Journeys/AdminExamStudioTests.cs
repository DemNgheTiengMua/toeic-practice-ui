using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;
using WebMVC.E2ETests.Pages;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class AdminExamStudioTests : PlaywrightTestBase
{
    private LoginPage _loginPage = null!;
    private ExamStudioPage _studioPage = null!;

    [SetUp]
    public void SetupPages()
    {
        _loginPage = new LoginPage(Page, BaseUrl);
        _studioPage = new ExamStudioPage(Page, BaseUrl);
    }

    [Test]
    public async Task Should_Navigate_To_Exam_Studio_And_Verify_Curriculum_Rail()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        await _studioPage.GotoAsync(1);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/exams/\d+/studio", RegexOptions.IgnoreCase));

        // #159: two panes. The rail and the workstation; the audit is docked inside the
        // workstation so the publish gate stays visible without costing a third of the screen.
        await Expect(_studioPage.LeftRail).ToBeVisibleAsync();
        await Expect(_studioPage.CenterWorkstation).ToBeVisibleAsync();
        await Expect(_studioPage.RightAudit).ToBeVisibleAsync();
        Assert.That(await Page.Locator("#studio-workstation #studio-right-audit").CountAsync(), Is.EqualTo(1),
            "the publish audit belongs inside the workstation now");

        // Verify rail items and question counters
        await Expect(_studioPage.RailTotalCount).ToBeVisibleAsync();
        var sectionCount = await _studioPage.RailSections.CountAsync();
        Assert.That(sectionCount, Is.GreaterThan(0), "Exam Studio should render at least one section in the curriculum rail.");
    }

    [Test]
    public async Task Should_Show_One_Category_Dropdown_And_Fourteen_Controls_Per_Mcq()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await _studioPage.GotoAsync(1);

        var card = Page.Locator(".question-card[data-question-type='mcq']").First;
        await Expect(card).ToBeVisibleAsync();

        Assert.That(await card.Locator("[data-field='CategoryId']").CountAsync(), Is.EqualTo(1),
            "one Category dropdown per question is the ticket's rule");

        // Chromium keeps layout boxes for the children of a closed <details>, so a bare
        // offsetParent check would still count the collapsed rationale block. Excluding them is
        // what "the author sees" means.
        var visible = await card.EvaluateAsync<int>(@"el => Array.from(el.querySelectorAll('input,select,textarea,button'))
            .filter(c => c.offsetParent !== null && !c.closest('details:not([open])')).length");
        Assert.That(visible, Is.LessThanOrEqualTo(14),
            "a 4-option MCQ used to show 18 controls; it must not exceed 14");
    }

    [Test]
    public async Task Should_Refuse_A_New_Cluster_On_A_Locked_Exam_And_Say_Why()
    {
        // #159 must not regress the structural lock (#157). Exam 1 has a finished attempt in the
        // demo seed, so adding questions is refused with the #157 message that names the count.
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await _studioPage.GotoAsync(1);

        var sectionId = await Page.Locator(".studio-section-tab").First.GetAttributeAsync("data-section-id");
        Assert.That(sectionId, Is.Not.Null);

        var response = await Page.EvaluateAsync<System.Text.Json.JsonElement>(@"async (sid) => {
            const token = document.querySelector('#studio-csrf-holder input[name=""__RequestVerificationToken""]').value;
            const res = await fetch('/admin/exams/1/clusters', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
              body: JSON.stringify({ sectionId: Number(sid), questionCount: 1, unitNumber: 1 })
            });
            return { status: res.status, body: await res.json() };
        }", sectionId);

        Assert.That(response.GetProperty("status").GetInt32(), Is.EqualTo(400));
        var message = response.GetProperty("body").GetProperty("message").GetString();
        Assert.That(message, Does.Contain("structure is locked"));
        Assert.That(message, Does.Contain("learner attempt has already finished it"));
    }

    [Test]
    public async Task Should_Pick_Audio_With_A_File_Picker_And_Show_The_Rejection_For_A_Wrong_Type()
    {
        // #159: a typed path failed silently. The picker must answer: either the asset appears, or
        // the API's refusal is shown in place.
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await _studioPage.GotoAsync(1);

        var fileInput = Page.Locator("input[type='file'][accept='audio/*'][onchange*='uploadGroupAudioFile']").First;
        Assert.That(await fileInput.CountAsync(), Is.GreaterThan(0), "each audio group needs a real audio picker");

        var groupId = await fileInput.EvaluateAsync<string>(@"el => el.getAttribute('onchange').replace(/[^0-9]/g, '')");
        var status = Page.Locator($"#slice-status-{groupId}");
        var path = Page.Locator($"#audio-path-{groupId}");
        var before = await path.InputValueAsync();

        await fileInput.SetInputFilesAsync(new FilePayload
        {
            Name = "not-audio.pdf",
            MimeType = "application/pdf",
            Buffer = "not an audio file"u8.ToArray()
        });

        await Expect(status).ToContainTextAsync("not supported");
        await Expect(status).ToHaveAttributeAsync("data-state", "error");
        Assert.That(await path.InputValueAsync(), Is.EqualTo(before),
            "a rejected file must not overwrite the stored path");
    }

    [Test]
    public async Task Should_Open_Quick_Ingest_Modal_Load_Sample_And_Parse_Staging_Preview()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        await _studioPage.GotoAsync(1);

        // Trigger Quick Ingest modal
        await _studioPage.QuickIngestButton.ClickAsync();
        await Expect(_studioPage.QuickIngestModal).ToBeVisibleAsync();

        // Load sample template into scratchpad
        await _studioPage.QuickIngestSampleButton.ClickAsync();
        await Expect(_studioPage.QuickIngestRawTextArea).Not.ToHaveValueAsync("", new() { Timeout = 10000 });
        var rawText = await _studioPage.QuickIngestRawTextArea.InputValueAsync();
        Assert.That(rawText, Is.Not.Empty, "Sample template button should populate raw text textarea.");

        // Pre-flight staging parse
        await _studioPage.QuickIngestParseButton.ClickAsync();
        await Expect(_studioPage.QuickIngestPreviewContainer).ToBeVisibleAsync();

        var rowCount = await _studioPage.QuickIngestPreviewRows.CountAsync();
        Assert.That(rowCount, Is.GreaterThan(0), "Pre-flight staging preview should parse and display question rows.");

        // Close modal
        await _studioPage.QuickIngestCloseButton.ClickAsync();
        await Expect(_studioPage.QuickIngestModal).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Verify_Readiness_Audit_Placard_And_Export_Links()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        await _studioPage.GotoAsync(1);

        // Check readiness audit panel elements
        await Expect(_studioPage.AuditStatusBadge).ToBeVisibleAsync();
        await Expect(_studioPage.AuditQuestionCount).ToBeVisibleAsync();

        // Check dual export buttons
        await Expect(_studioPage.ExportZipButton).ToBeVisibleAsync();
        var zipHref = await _studioPage.ExportZipButton.GetAttributeAsync("href");
        Assert.That(zipHref, Does.Contain("/export?format=zip"));

        await Expect(_studioPage.ExportJsonButton).ToBeVisibleAsync();
        var jsonHref = await _studioPage.ExportJsonButton.GetAttributeAsync("href");
        Assert.That(jsonHref, Does.Contain("/export?format=json"));
    }

    [Test]
    public async Task Should_Redirect_Legacy_Questions_Route_Directly_To_Exam_Studio()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        // Navigate directly to legacy /admin/exams/1/questions with query params
        await Page.GotoAsync($"{BaseUrl}/admin/exams/1/questions?sectionId=1");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/exams/1/studio\?sectionId=1", RegexOptions.IgnoreCase));

        // Verify the 3-panel layout renders cleanly and no 500 occurred
        await Expect(_studioPage.LeftRail).ToBeVisibleAsync();
        await Expect(_studioPage.CenterWorkstation).ToBeVisibleAsync();
        await Expect(_studioPage.RightAudit).ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Render_Admin_Sidebar_And_Layout_On_Users_And_Orders_Pages()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        // Navigate to /admin/users
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await Expect(Page.Locator(".app-sidebar")).ToBeVisibleAsync();
        await Expect(Page.Locator("nav[aria-label='Admin navigation']")).ToBeVisibleAsync();
        await Expect(Page.Locator(".nav-list__item[href='/admin/users']")).ToHaveClassAsync(new Regex(@"is-active"));

        // Navigate to /admin/orders
        await Page.GotoAsync($"{BaseUrl}/admin/orders");
        await Expect(Page.Locator(".app-sidebar")).ToBeVisibleAsync();
        await Expect(Page.Locator("nav[aria-label='Admin navigation']")).ToBeVisibleAsync();
        await Expect(Page.Locator(".nav-list__item[href='/admin/orders']")).ToHaveClassAsync(new Regex(@"is-active"));
    }

    [Test]
    public async Task Should_Navigate_To_Exams_List_Filter_By_Cert_And_Purpose_And_Verify_Actions()
    {
        await _loginPage.GotoAsync();
        await _loginPage.LoginAsync("admin@demo.local", "Demo1234!");
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));

        // Navigate to /admin/exams
        await Page.GotoAsync($"{BaseUrl}/admin/exams");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/exams", RegexOptions.IgnoreCase));

        // Verify Certificate and Purpose filter dropdowns exist
        var certSelect = Page.Locator("#f-cert");
        var purposeSelect = Page.Locator("#f-purpose");
        await Expect(certSelect).ToBeVisibleAsync();
        await Expect(purposeSelect).ToBeVisibleAsync();

        // Verify streamlined action buttons exist in exam table rows
        var studioButtons = Page.Locator("a.btn--primary:has-text('Studio')");
        await Expect(studioButtons.First).ToBeVisibleAsync();

        var editInfoButtons = Page.Locator("a:has-text('Edit Info')");
        await Expect(editInfoButtons.First).ToBeVisibleAsync();

        // Filter by purpose = mock
        await purposeSelect.SelectOptionAsync(new SelectOptionValue { Value = "mock" });
        await Page.Locator("form.toolbar button[type='submit']").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"purpose=mock", RegexOptions.IgnoreCase));

        // Verify filtered table results display with Studio and Edit Info
        await Expect(Page.Locator("table.table")).ToBeVisibleAsync();
        await Expect(Page.Locator("a.btn--primary:has-text('Studio')").First).ToBeVisibleAsync();
        await Expect(Page.Locator("a:has-text('Edit Info')").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Store_The_Real_Duration_Of_Uploaded_Section_Audio()
    {
        // Issue #82: the duration probe used to race the upload, so most tracks were stored as 300 s.
        // VSTEP Sample Test 1 (exam 10, listening section 21) is used by no other journey; its
        // original section audio row is restored afterwards.
        const int examId = 10, sectionId = 21, seconds = 12;
        await using var db = await OpenAppDatabaseAsync();
        var original = await ReadSectionAudioAsync(db, examId, sectionId);

        try
        {
            // A small local file decodes faster than it uploads, which hides the race. Delay
            // loadedmetadata by 1.5 s (under the studio's 3 s probe timeout) to play a slow decode
            // of a large recording: the upload then finishes first, as it does in practice.
            await Page.AddInitScriptAsync("""
                const add = EventTarget.prototype.addEventListener;
                EventTarget.prototype.addEventListener = function (type, listener, options) {
                  if (type === 'loadedmetadata' && this instanceof HTMLMediaElement && typeof listener === 'function') {
                    const slow = function (e) { setTimeout(() => listener.call(this, e), 1500); };
                    return add.call(this, type, slow, options);
                  }
                  return add.call(this, type, listener, options);
                };
                """);
            await SignInAsync("admin@demo.local", "Demo1234!");
            await Page.GotoAsync($"{BaseUrl}/admin/exams/{examId}/studio");
            await Page.Locator($"input[type='file'][onchange*='uploadAudioFile(this, {sectionId})']").SetInputFilesAsync(
                new FilePayload { Name = "e2e-12s.wav", MimeType = "audio/wav", Buffer = SilentWav(seconds) });

            (string Path, int DurationSec)? stored = null;
            for (var i = 0; i < 40 && (stored is null || stored.Value.Path == original?.Path); i++)
            {
                await Task.Delay(250);
                stored = await ReadSectionAudioAsync(db, examId, sectionId);
            }

            Assert.That(stored?.Path, Is.Not.EqualTo(original?.Path), "section audio was not replaced");
            Assert.That(stored!.Value.DurationSec, Is.EqualTo(seconds));
        }
        finally
        {
            if (original is { } row)
            {
                await using var restore = new Microsoft.Data.SqlClient.SqlCommand(
                    "UPDATE ExamSectionAudio SET AudioPath = @path, DurationSec = @duration WHERE ExamId = @exam AND SectionId = @section", db);
                restore.Parameters.AddWithValue("@path", row.Path);
                restore.Parameters.AddWithValue("@duration", row.DurationSec);
                restore.Parameters.AddWithValue("@exam", examId);
                restore.Parameters.AddWithValue("@section", sectionId);
                await restore.ExecuteNonQueryAsync();
            }
        }
    }

    private static async Task<(string Path, int DurationSec)?> ReadSectionAudioAsync(
        Microsoft.Data.SqlClient.SqlConnection db, int examId, int sectionId)
    {
        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
            "SELECT AudioPath, DurationSec FROM ExamSectionAudio WHERE ExamId = @exam AND SectionId = @section", db);
        cmd.Parameters.AddWithValue("@exam", examId);
        cmd.Parameters.AddWithValue("@section", sectionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetString(0), reader.GetInt32(1)) : null;
    }

    /// <summary>A silent 8 kHz, 8-bit mono PCM WAV of exactly the given length.</summary>
    private static byte[] SilentWav(int seconds)
    {
        const int rate = 8000;
        var samples = seconds * rate;
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + samples); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate); w.Write((short)1); w.Write((short)8);
        w.Write("data"u8); w.Write(samples);
        w.Write(Enumerable.Repeat((byte)128, samples).ToArray());
        return ms.ToArray();
    }
}
