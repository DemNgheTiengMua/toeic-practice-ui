using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>CK_PaymentEvents_Source: webhook, status_query, return_redirect, cancel.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentEventSourceDto>))]
public enum PaymentEventSourceDto
{
    [JsonStringEnumMemberName("webhook")] Webhook,
    [JsonStringEnumMemberName("status_query")] StatusQuery,
    [JsonStringEnumMemberName("return_redirect")] ReturnRedirect,
    [JsonStringEnumMemberName("cancel")] Cancel,
}
