using Contracts;
using System.Text.Json;
using WebMVC.Models;
using System.Text.RegularExpressions;

namespace WebMVC.Tests;

public sealed class PreflightViewTests
{
    [Fact]
    public void View_and_partials_only_render_markup_and_reference_external_assets()
    {
        var root = TestPathHelper.FindRepoRoot();
        var directory = Path.Combine(root, "src", "WebMVC", "Views", "Exams");
        var view = File.ReadAllText(Path.Combine(directory, "Preflight.cshtml"));
        var partials = Directory.GetFiles(directory, "_Preflight*.cshtml");
        Assert.NotEmpty(partials);
        var markup = view + string.Join("\n", partials.Select(File.ReadAllText));

        Assert.Contains("~/js/exams/preflight.js", view);
        Assert.Contains("~/css/exams/preflight.css", view);
        Assert.Contains("type=\"application/json\" id=\"exam-preflight-data\"", view);
        Assert.DoesNotMatch(@"<style\b|\bstyle\s*=|\bon(?:click|change|submit|load)\s*=", markup);
        Assert.DoesNotMatch(@"@(inject|functions)\b|\bnew\s+|\b(?:Math|JsonSerializer)\.|\.(?:GroupBy|Select|Where|OrderBy|TakeLast|Sum|Max|FirstOrDefault)\s*\(", markup);
        foreach (Match script in Regex.Matches(markup, @"<script\b(?<attributes>[^>]*)>(?<body>[\s\S]*?)</script>"))
        {
            var attributes = script.Groups["attributes"].Value;
            Assert.True(attributes.Contains("src=") || attributes.Contains("type=\"application/json\""),
                "Executable scripts must live in external JavaScript files.");
        }
    }

    [Theory]
    [InlineData(false, "badge--warning", "CHECKS PENDING", "CHECK REQUIRED")]
    [InlineData(true, "badge--success", "VERIFIED & READY", "MIC VERIFIED")]
    public void Model_preserves_verification_labels_and_urls(bool verified, string badge, string gate, string microphone)
    {
        var preflight = Status(verified: verified);
        var model = new ExamPreflightViewModel(preflight, "Equipment check failed");

        Assert.Same(preflight, model.Preflight);
        Assert.Equal("Pre-flight Check — Speaking exam", model.PageTitle);
        Assert.Equal("Equipment readiness and asset verification for Speaking exam.", model.PageDescription);
        Assert.Equal("/exams/7/instructions", model.InstructionsUrl);
        Assert.Equal("/exams/preflight/100/verify", model.VerifyUrl);
        Assert.Equal(badge, model.VerificationBadgeClass);
        Assert.Equal(gate, model.GateStatusLabel);
        Assert.Equal(microphone, model.MicrophoneStatusLabel);
        Assert.True(model.HasError);
    }

    [Fact]
    public void Model_initializes_no_audio_and_unverified_microphone_independently()
    {
        var model = new ExamPreflightViewModel(Status());
        Assert.Equal(100, model.InitialBufferPercent);
        Assert.Equal("100%", model.BufferPercentageLabel);
        Assert.Equal("preflight-buffer-complete", model.BufferProgressClass);
        Assert.Equal("NO AUDIO REQUIRED", model.BufferStatusLabel);
        Assert.Equal("No listening audio tracks in this exam blueprint", model.BufferDetails);
        using var json = JsonDocument.Parse(model.PageDataJson);
        Assert.Empty(json.RootElement.GetProperty("audioUrls").EnumerateArray());
        Assert.True(json.RootElement.GetProperty("requiresMic").GetBoolean());
        Assert.False(json.RootElement.GetProperty("isAlreadyVerified").GetBoolean());
    }

    [Fact]
    public void Model_waits_for_audio_and_safely_round_trips_urls_in_page_data()
    {
        string[] urls = ["/audio/quote'\"</script><script>alert('x')</script>&.mp3", "/audio/two.mp3"];
        var model = new ExamPreflightViewModel(Status(audioUrls: urls, verified: true));
        Assert.Equal(0, model.InitialBufferPercent);
        Assert.Equal("0%", model.BufferPercentageLabel);
        Assert.Equal("", model.BufferProgressClass);
        Assert.Equal("PRE-BUFFERING...", model.BufferStatusLabel);
        Assert.Equal("0 of 2 audio tracks pre-buffered", model.BufferDetails);
        Assert.DoesNotContain("<", model.PageDataJson);
        Assert.DoesNotContain("&", model.PageDataJson);
        using var json = JsonDocument.Parse(model.PageDataJson);
        Assert.Equal(urls, json.RootElement.GetProperty("audioUrls").EnumerateArray().Select(url => url.GetString()));
        Assert.True(json.RootElement.GetProperty("isAlreadyVerified").GetBoolean());
    }

    [Fact]
    public void Gate_keeps_a_normal_body_post_with_antiforgery_and_external_script_keeps_mic_checks()
    {
        var root = TestPathHelper.FindRepoRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "WebMVC", "Views", "Exams", "_PreflightGate.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "src", "WebMVC", "wwwroot", "js", "exams", "preflight.js"));
        Assert.Contains("method=\"post\" action=\"@Model.VerifyUrl\"", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
        Assert.Contains("id=\"start-test-btn\"", view);
        Assert.Contains("disabled", view);
        Assert.Contains("JSON.parse(document.getElementById(\"exam-preflight-data\").textContent)", script);
        Assert.Contains("state.audioBuffered && state.micVerified", script);
        Assert.Contains("micVerified: !requiresMic || isAlreadyVerified", script);
        Assert.Contains("new MediaRecorder(micStream)", script);
        Assert.Contains("navigator.mediaDevices.getUserMedia(constraints)", script);
        Assert.Contains("state.micVerified = true", script);
        Assert.Contains("state.micVerified = false", script);
        Assert.Contains("await bufferSingleAudio(url)", script);
        Assert.Contains("await preloadViaAudio(url)", script);
        Assert.DoesNotContain("preventDefault", script);
    }

    private static PreflightStatusDto Status(IReadOnlyList<string>? audioUrls = null, bool verified = false) =>
        new(100, 7, "Speaking exam", "IELTS", "IELTS", 15, true, true, true, verified, null, audioUrls ?? []);
}
