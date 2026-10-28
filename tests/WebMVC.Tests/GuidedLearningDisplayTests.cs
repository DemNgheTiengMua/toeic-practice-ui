using System;
using System.Linq;
using Contracts;
using Microsoft.AspNetCore.Http;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

public sealed class GuidedLearningDisplayTests
{
    private static LessonCardDto Card(int id, int order, string type, string markdown) =>
        new(id, type, "Title", markdown, null, null, null, null, null, null, null, order);

    [Fact]
    public void Lesson_player_sorts_cards_resumes_saved_card_and_prepares_safe_content()
    {
        var lesson = new LessonDetailDto(7, "Grammar", null, 1, null, 5,
            [Card(22, 2, "example", "<script>alert(1)</script>"), Card(11, 1, "rule", "**Rule**")]);
        var progress = new LessonProgressDto(1, 2, false, 22, null, null, null);
        var model = new LessonViewModel(lesson, progress, 9);

        Assert.Equal(new[] { 11, 22 }, model.Cards.Select(c => c.Card.CardId));
        Assert.Equal(2, model.InitialIndex);
        Assert.Equal("/learning-path/modules/9", model.FinishUrl);
        Assert.Equal("/admin/lessons/7/edit", model.ReturnUrl);
        Assert.Equal("lesson-card--rule", model.Cards[0].CssClass);
        Assert.Equal("<p><strong>Rule</strong></p>", model.Cards[0].ContentHtml);
        Assert.DoesNotContain("<script", model.Cards[1].ContentHtml);
        Assert.Equal(1, new LessonViewModel(lesson, progress with { LastViewedCardId = 999 }).InitialIndex);
    }

    [Theory]
    [InlineData(-1, "STARTING NOW")]
    [InlineData(30, "IN 30M")]
    [InlineData(95, "IN 1H 35M")]
    [InlineData(1500, "IN 1D 1H")]
    public void Mentor_countdown_preserves_boundary_labels(int minutes, string expected)
    {
        var now = DateTimeOffset.Parse("2026-10-26T00:00:00Z");
        Assert.Equal(expected, DashboardPresentation.FormatCountdown(now.AddMinutes(minutes), now));
    }

    [Theory]
    [InlineData(ExamPurposeDto.Mock, "/exams/take/7", "/exams/result/7")]
    [InlineData(ExamPurposeDto.Placement, "/placement/take/7", "/placement/result/7")]
    [InlineData(ExamPurposeDto.SpeakingPractice, "/speaking/take/7", "/speaking/review/7")]
    [InlineData(ExamPurposeDto.WritingPractice, "/writing/take/7", "/writing/review/7")]
    public void Dashboard_attempt_links_preserve_each_sitting_destination(ExamPurposeDto purpose, string resume, string result)
    {
        var links = new DashboardAttemptLinks(new RecentAttemptDto(7, "Exam", purpose,
            AttemptStatusDto.Graded, DateTimeOffset.UtcNow, 100, null));
        Assert.Equal(resume, links.Resume);
        Assert.Equal(result, links.Result);
        Assert.Equal("Cancelled", DashboardPresentation.AttemptStatusLabel(AttemptStatusDto.Abandoned));
        Assert.Equal("Cancelled", DashboardPresentation.PracticeStatusLabel(PracticeStatusDto.Abandoned));
    }

    [Fact]
    public void Mentor_slot_dates_use_the_request_timezone_and_remain_display_ready()
    {
        var http = new DefaultHttpContext();
        http.Items[DateTimeFormatter.ZoneItemKey] = "Asia/Ho_Chi_Minh";
        var formatter = new DateTimeFormatter(new HttpContextAccessor { HttpContext = http });
        var start = DateTimeOffset.Parse("2026-10-25T18:00:00Z");
        var detail = new MentorDetailDto(null!, [new MentorSlotDto(1, 2, start, start.AddHours(1), "available")], []);
        var model = new MentorDetailViewModel(detail).PrepareDates(formatter);
        Assert.Equal("26 Oct 2026", model.Dates[start].Date);
        Assert.Equal("26 Oct 2026, 01:00", model.Dates[start].Full);
        Assert.Equal("01:00", model.Dates[start].Time);
        Assert.Empty(new LearnerBookingsViewModel([]).PrepareDates(formatter).Dates);
    }

    [Fact]
    public void Simulator_prepares_band_target_tick_and_headroom_for_rendering()
    {
        var dashboard = new LearnerDashboardDto(new("TOEIC", "TOEIC"), null, true, 0, 0, [], []);
        var config = new SimulatorConfigDto(100, null, null, "b2", null,
            [new SimulatorSkillDto(1, "R", "Reading", "reading", 25, 0, 50, 5),
             new SimulatorSkillDto(2, "L", "Listening", "listening", 10, 0, 50, 5)],
            [new ScoreBandThresholdDto("B1", "Threshold", 0, 49), new ScoreBandThresholdDto("B2", "Vantage", 50, 100)]);
        var model = new DashboardSimulatorViewModel(dashboard, config);
        Assert.Equal(35, model.InitialTotal);
        Assert.Equal("B1", model.InitialBand.Code);
        Assert.Equal(15, model.InitialDeficit);
        Assert.Equal("Vantage", model.TargetName);
        Assert.Equal(2, model.TopLeverageSkill.SkillId);
        Assert.Equal(40, model.TopHeadroom);
        Assert.Equal(25, model.Skills[1].MidTick);
        Assert.True(model.Bands["B1"].IsCurrent);
        Assert.True(model.Bands["B2"].IsTarget);
        Assert.Contains("\"minTotal\":50", model.BandsJson);
    }
}
