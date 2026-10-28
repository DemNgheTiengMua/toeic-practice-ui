using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_AiInsights_Scope: practice_session, attempt, path, dashboard.</summary>


/// <summary>CK_AiInsights_Status: pending, done, failed.</summary>


/// <summary>CK_AiInsights_Trend: improving, flat, declining.</summary>


/// <summary>CK_Practice_Status: in_progress, finished, abandoned.</summary>


/// <summary>CK_Practice_Source: all, wrong_before, unseen.</summary>


/// <summary>CK_Practice_Feedback: immediate, end.</summary>


/// <summary>CK_SavedQ_Source: manual, incorrect.</summary>


/// <summary>CK_Reco_Severity: info, minor, major.</summary>


/// <summary>CK_Orders_Status: pending, reconciling, paid, failed, expired.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrderStatusDto>))]
public enum OrderStatusDto
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("reconciling")] Reconciling,
    [JsonStringEnumMemberName("paid")] Paid,
    [JsonStringEnumMemberName("failed")] Failed,
    [JsonStringEnumMemberName("expired")] Expired,
}
