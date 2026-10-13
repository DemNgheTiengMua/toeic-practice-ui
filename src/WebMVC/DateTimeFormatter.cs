using Contracts;

namespace WebMVC;

/// <summary>
/// The one place instants render (UI-52). Everything is stored UTC; this converts to the signed-in
/// learner's zone — chosen on the profile page from the <see cref="TimeZones"/> shortlist — and
/// formats it. Later screens must call this, never format a <see cref="DateTimeOffset"/> directly.
/// </summary>
public sealed class DateTimeFormatter(IHttpContextAccessor accessor)
{
    private string ZoneId
    {
        get
        {
            var item = accessor.HttpContext?.Items["tz"] as string;
            return TimeZones.Contains(item) ? item! : TimeZones.DefaultId;
        }
    }

    /// <summary>"14 Oct 2026, 20:30" in the learner's zone.</summary>
    public string Format(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(ZoneId))
            .ToString("d MMM yyyy, HH:mm");

    /// <summary>"14 Oct 2026" in the learner's zone.</summary>
    public string FormatDate(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(ZoneId))
            .ToString("d MMM yyyy");
}
