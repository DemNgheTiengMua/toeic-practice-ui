using Contracts;
using System.Text.Json;
using WebMVC.Models;
using System.Text.RegularExpressions;

namespace WebMVC.Tests;

public sealed class PreflightViewTests
{
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
        Assert.Equal("No audio tracks in this exam blueprint", model.BufferDetails);
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

    private static PreflightStatusDto Status(IReadOnlyList<string>? audioUrls = null, bool verified = false) =>
        new(100, 7, "Speaking exam", "IELTS", "IELTS", 15, true, true, true, verified, null, audioUrls ?? []);
}
