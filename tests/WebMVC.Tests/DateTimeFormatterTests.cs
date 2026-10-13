using WebMVC;

namespace WebMVC.Tests;

public sealed class DateTimeFormatterTests
{
    // 2026-10-13T17:00:00Z — midnight in Ho Chi Minh (UTC+7), 02:00 the next day in Tokyo (UTC+9).
    private static readonly DateTimeOffset Instant = new(2026, 10, 13, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Format_renders_the_stored_utc_instant_in_the_given_zone()
    {
        Assert.Equal("14 Oct 2026, 00:00", DateTimeFormatter.FormatInZone(Instant, "Asia/Ho_Chi_Minh", "d MMM yyyy, HH:mm"));
        Assert.Equal("14 Oct 2026, 02:00", DateTimeFormatter.FormatInZone(Instant, "Asia/Tokyo", "d MMM yyyy, HH:mm"));
    }

    [Fact]
    public void FormatDate_renders_only_the_date_in_the_given_zone()
    {
        Assert.Equal("13 Oct 2026", DateTimeFormatter.FormatInZone(Instant, "UTC", "d MMM yyyy"));
        Assert.Equal("14 Oct 2026", DateTimeFormatter.FormatInZone(Instant, "Asia/Ho_Chi_Minh", "d MMM yyyy"));
    }

    [Fact]
    public void Format_preserves_the_instant_not_the_wall_clock()
    {
        // Same instant, two zones: the rendered times differ but must point at the same moment.
        var hcmc = DateTimeFormatter.FormatInZone(Instant, "Asia/Ho_Chi_Minh", "HH:mm");
        var tokyo = DateTimeFormatter.FormatInZone(Instant, "Asia/Tokyo", "HH:mm");
        Assert.Equal("00:00", hcmc);
        Assert.Equal("02:00", tokyo);
    }
}
