namespace WebMVC;

/// <summary>Renders stored UTC instants in the learner's own time zone (Users.TimeZoneId, an IANA
/// id). .NET resolves IANA ids on Windows too; an unknown id falls back to UTC rather than failing
/// the page.</summary>
public static class LocalTime
{
    public static DateTimeOffset In(DateTimeOffset instant, string? timeZoneId)
    {
        if (string.IsNullOrEmpty(timeZoneId))
        {
            return instant.ToUniversalTime();
        }

        try
        {
            return TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return instant.ToUniversalTime();
        }
    }

    /// <summary>"20 Sep 2026", the prototype's date format.</summary>
    public static string Date(DateTimeOffset instant, string? timeZoneId) =>
        In(instant, timeZoneId).ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
