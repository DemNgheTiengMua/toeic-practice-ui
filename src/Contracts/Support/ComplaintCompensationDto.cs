using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>
/// #350: the one compensation ending a complaint resolution carries. The three arms are mutually
/// exclusive by construction: a resolution is <c>none</c>, a make-good grant, or the instruction to
/// record a refund - never two at once.
/// <para>
/// <b>Why an arm rather than an optional credit number.</b> A lone <c>grantCredits</c> could not say
/// "this incident is being handed to the refund desk", so staff resolved a complaint and then walked
/// to another screen to type the same incident again. <c>refund</c> creates no refund; it returns the
/// learner and the complaint so the client can open the refund desk with them carried.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ComplaintCompensationDto>))]
public enum ComplaintCompensationDto
{
    [JsonStringEnumMemberName("none")] None,

    [JsonStringEnumMemberName("make_good")] MakeGood,

    [JsonStringEnumMemberName("refund")] Refund,
}
