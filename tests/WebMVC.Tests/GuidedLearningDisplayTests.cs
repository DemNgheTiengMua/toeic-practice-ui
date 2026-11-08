using System;
using System.Linq;
using Contracts;
using Microsoft.AspNetCore.Http;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

/// <summary>The guided-learning and mentor screens: card order, resume point, and dates that render in
/// the learner's own zone. #159 deleted the dashboard pieces that lived here (the mentor countdown, the
/// recent-attempt links and the simulator) along with the blocks that used them.</summary>
public sealed class GuidedLearningDisplayTests
{
    private static LessonCardDto Card(int id, int order, string type, string markdown) =>
        new(id, type, "Title", markdown, null, null, null, null, null, null, null, order);

    [Fact]
    public void Lesson_player_sorts_cards_resumes_saved_card_and_prepares_safe_content()
    {
        var lesson = new LessonDetailDto(7, "Grammar", null, 1, null, 5,
            [Card(22, 2, "example", "<script>alert(1)</script>"), Card(11, 1, "rule", "**Rule**")]);
        var progress = new LessonProgressDto(1, 2, false, 22, null, null, null, ResumeCardIndex: 2);
        var model = new LessonViewModel(lesson, progress);

        Assert.Equal(new[] { 11, 22 }, model.Cards.Select(c => c.Card.CardId));
        Assert.Equal(2, model.InitialIndex);
        // #232: the reader finishes back on the path timeline, not on a module detail screen.
        Assert.Equal("/learning-path", model.FinishUrl);
        Assert.Equal("/admin/lessons/7/edit", model.ReturnUrl);
        Assert.Equal("lesson-card--rule", model.Cards[0].CssClass);
        Assert.Equal("<p><strong>Rule</strong></p>", model.Cards[0].ContentHtml);
        Assert.DoesNotContain("<script", model.Cards[1].ContentHtml);
        // #240: the resume point is the service's computed card index, so a last-viewed card that no
        // longer exists in the module arrives here already resolved to card one by the service.
        Assert.Equal(1, new LessonViewModel(lesson, progress with { LastViewedCardId = 999, ResumeCardIndex = 1 }).InitialIndex);
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
}
