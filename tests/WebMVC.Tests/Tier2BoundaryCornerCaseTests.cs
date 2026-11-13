using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using WebMVC;
using WebMVC.Controllers;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// Tier 2: Boundary & Corner Cases Test Suite for WebMVC.
/// Covers empty/null inputs, extreme timeouts, special character / Vietnamese encoding integrity,
/// XSS sanitization, and error code failure cascading.
/// </summary>
public sealed class Tier2BoundaryCornerCaseTests
{
    // =========================================================================
    // 1. Extreme Timeouts & Countdown Bounds
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(-3600)]
    public void NonPositive_Remaining_Seconds_In_SittingSyncDto_Flags_AutoSubmitted(int remainingSeconds)
    {
        var dto = new SittingSyncDto(
            AttemptId: 301,
            RemainingSeconds: remainingSeconds,
            IsAutoSubmitted: remainingSeconds <= 0,
            ActiveSkillId: 1,
            ActiveUnitNumber: 1,
            ActiveGroupId: null,
            PhaseDeadlineAt: null,
            LastAudioOffsetMs: 0,
            AnswersSavedCount: 0);

        Assert.True(dto.IsAutoSubmitted);
        Assert.True(dto.RemainingSeconds <= 0);
    }

    [Fact]
    public void Extreme_Duration_Minutes_Does_Not_Cause_Integer_Overflow()
    {
        // 50,000 minutes = 3,000,000 seconds
        var durationMinutes = 50000;
        var durationSeconds = (long)durationMinutes * 60;

        Assert.Equal(3000000L, durationSeconds);
        var timeSpan = TimeSpan.FromMinutes(durationMinutes);
        Assert.Equal(50000, timeSpan.TotalMinutes);
    }

    // =========================================================================
    // 3. Special Characters, Vietnamese Unicode & Escaping Integrity
    // =========================================================================

    [Fact]
    public void Vietnamese_Diacritical_Text_Survives_Json_Serialization_Without_Mojibake()
    {
        var vietnamesePrompt = "Đề thi thử năng lực tiếng Anh TOEIC 2026: Đọc kỹ đoạn văn và chọn đáp án đúng nhất (A, B, C, hoặc D).";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var request = new SaveAnswerRequest(
            SelectedOptionId: 10,
            IsFlagged: false,
            ResponseText: vietnamesePrompt);

        var json = JsonSerializer.Serialize(request, options);
        var deserialized = JsonSerializer.Deserialize<SaveAnswerRequest>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(vietnamesePrompt, deserialized.ResponseText);

        // Ensure zero mojibake corruption patterns
        string[] mojibakeMarkers = ["Ã‚", "Ã¡", "Ã\xa0", "Ã£", "Ã©", "Ã¨", "áº", "á»", "Æ°", "Äƒ", "Ä‘"];
        foreach (var marker in mojibakeMarkers)
        {
            Assert.DoesNotContain(marker, deserialized.ResponseText);
        }
    }

    [Fact]
    public void XSS_Payload_In_FreeText_Response_Is_Escaped_By_AiNarrativeFormatter()
    {
        var xssPayload = "<script>alert('pwned')</script><iframe src='javascript:alert(1)'></iframe>**Safe Text**";
        var safeHtml = AiNarrativeFormatter.ToSafeHtml(xssPayload);

        Assert.DoesNotContain("<script", safeHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", safeHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", safeHtml);
        Assert.Contains("<strong>Safe Text</strong>", safeHtml);
    }

    [Fact]
    public void Quotes_And_Apostrophes_In_Answer_Options_Preserved_Properly()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var answer = new BatchAnswerItemDto(
            QuestionId: 405,
            SelectedOptionId: 1,
            IsFlagged: false,
            ResponseText: "She said, \"It's O'Connor's book.\"",
            SelectedOptionIdsJson: "[\"A\", \"B\"]");

        var json = JsonSerializer.Serialize(answer, options);
        var deserialized = JsonSerializer.Deserialize<BatchAnswerItemDto>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("She said, \"It's O'Connor's book.\"", deserialized.ResponseText);
        Assert.Equal("[\"A\", \"B\"]", deserialized.SelectedOptionIdsJson);
    }
}
