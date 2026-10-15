using Contracts;

namespace WebMVC;

/// <summary>
/// The one place instants render (UI-52). Everything is stored UTC; this converts to the signed-in
/// learner's zone — chosen on the profile page from the <see cref="TimeZones"/> shortlist — and
/// formats it. Later screens must call this, never format a <see cref="DateTimeOffset"/> directly.
/// </summary>
public sealed class DateTimeFormatter(IHttpContextAccessor accessor)
{
    /// <summary>HttpContext.Items key TimeZoneMiddleware writes the learner's zone into.</summary>
    public const string ZoneItemKey = "tz";

    private string ZoneId
    {
        get
        {
            var item = accessor.HttpContext?.Items[ZoneItemKey] as string;
            return TimeZones.Contains(item) ? item! : TimeZones.DefaultId;
        }
    }

    /// <summary>"14 Oct 2026, 20:30" in the learner's zone.</summary>
    public string Format(DateTimeOffset instant) =>
        FormatInZone(instant, ZoneId, "d MMM yyyy, HH:mm");

    /// <summary>"14 Oct 2026" in the learner's zone.</summary>
    public string FormatDate(DateTimeOffset instant) =>
        FormatInZone(instant, ZoneId, "d MMM yyyy");

    /// <summary>Format with a custom format string in the learner's zone.</summary>
    public string FormatCustom(DateTimeOffset instant, string format) =>
        FormatInZone(instant, ZoneId, format);

    /// <summary>The conversion, separable from the request so tests can drive it directly.</summary>
    public static string FormatInZone(DateTimeOffset instant, string zoneId, string format) =>
        TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(zoneId))
            .ToString(format);
}
