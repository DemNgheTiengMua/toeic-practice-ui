using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_Refunds_Method: which form of value a refund returns. GLOSSARY.md,
/// "Refund method" - one refund carries exactly one.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RefundMethodDto>))]
public enum RefundMethodDto
{
    [JsonStringEnumMemberName("money")] Money,

    [JsonStringEnumMemberName("credit")] Credit,
}
