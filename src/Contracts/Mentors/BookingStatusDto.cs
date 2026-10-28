using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Bookings_Status: pending, confirmed, done, cancelled.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BookingStatusDto>))]
public enum BookingStatusDto
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("confirmed")] Confirmed,
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("cancelled")] Cancelled,
}
