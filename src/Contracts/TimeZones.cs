namespace Contracts;

/// <summary>
/// The shortlist of IANA time zones the profile page offers (UI-52) — not the full tz database.
/// Storage stays UTC everywhere; this list only controls how instants render for a learner.
/// </summary>
public static class TimeZones
{
    public const string DefaultId = "Asia/Ho_Chi_Minh";

    /// <summary>IANA ids in display order, Vietnam first.</summary>
    public static readonly IReadOnlyList<string> Ids =
    [
        "Asia/Ho_Chi_Minh",
        "Asia/Bangkok",
        "Asia/Jakarta",
        "Asia/Singapore",
        "Asia/Manila",
        "Asia/Tokyo",
        "Asia/Seoul",
        "Asia/Shanghai",
        "Australia/Sydney",
        "Europe/London",
        "Europe/Berlin",
        "America/New_York",
        "America/Los_Angeles",
        "UTC",
    ];

    public static bool Contains(string? id) => id is not null && Ids.Contains(id);
}
