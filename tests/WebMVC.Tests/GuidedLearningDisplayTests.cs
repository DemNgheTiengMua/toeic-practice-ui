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
