using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>
/// #223 / D21: which of three different absences one row of the standing carries. They are three
/// facts, not two, and collapsing any pair of them is the defect this type exists to prevent.
/// <para>
/// <see cref="NotMeasured"/> is about the learner: the certificate examines this skill and nothing has
/// measured it, so a placement (or a mock section) is owed. <see cref="NotExamined"/> is about the
/// certificate: this modality has no skill here at all, so no placement will ever fill it.
/// <see cref="Measured"/> is the only one that carries a level, and a level is never absent from it.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MentorSkillStandingStatus>))]
public enum MentorSkillStandingStatus
{
    [JsonStringEnumMemberName("measured")] Measured,
    [JsonStringEnumMemberName("not_measured")] NotMeasured,
    [JsonStringEnumMemberName("not_examined")] NotExamined,
}

/// <summary>
/// #223: one row of the picture a mentor receives. A row exists for every skill the certificate
/// examines, plus a row per modality it does not.
/// <para>
/// <b>A not-examined row names no skill.</b> <see cref="SkillId"/>, <see cref="SkillCode"/> and
/// <see cref="SkillName"/> are null there by design: inventing a skill id, a code or a name for a
/// modality the certificate never defines would make the table look complete and be a fabrication.
/// The row carries <see cref="Modality"/> so the page can still name it.
/// </para>
/// <para>
/// <b>The level is a CEFR level and nothing else.</b> It arrives from the learner's measured state
/// (#211); it is never looked up from <c>ScoreBands</c>, whose codes can read as levels ("B2") while
/// being a score range on this certificate's own scale. A certificate whose bands stop at C1 still
/// reports a C2 skill as C2.
/// </para>
/// </summary>
/// <param name="SkillId">The certificate's skill, or null when the certificate does not examine it.</param>
/// <param name="Modality">Always set: listening, reading, speaking or writing.</param>
/// <param name="SkillCode">The skill's own code, null for a not-examined modality.</param>
/// <param name="SkillName">The skill's own name, null for a not-examined modality.</param>
/// <param name="Status">Measured, not measured, or not examined - three different statements.</param>
/// <param name="LevelCode">The measured CEFR level; null unless <paramref name="Status"/> is measured.</param>
/// <param name="PositionInBand">0-100 inside that level, a position and never a percentage.</param>
/// <param name="IsStale">True once D18's 90-day reuse window has closed on this measurement.</param>
public sealed record MentorSkillStandingDto(
    int? SkillId,
    string Modality,
    string? SkillCode,
    string? SkillName,
    MentorSkillStandingStatus Status,
    string? LevelCode = null,
    int? PositionInBand = null,
    bool IsStale = false);

/// <summary>
/// #223 / D21: the learner's whole examined vector as a mentor receives it, derived at read time from
/// <c>LearnerSkillStates</c> through the one writer's read (#211).
/// <para>
/// <b>Nothing here is stored on the booking.</b> A booking is a row about a session; the standing is a
/// row about a learner that changes after the booking is made. Storing a copy would make an old booking
/// the second writer of a fact #211 owns, which is the defect class this rebuild keeps finding. So an
/// existing booking renders with today's standing, and no migration is needed.
/// </para>
/// <para>
/// <b><see cref="HasNoMeasurement"/> is not "every row is not measured".</b> When nothing has been
/// measured the overall is null and the learner owes a placement, which is a different sentence from
/// "this skill was not measured" on a certificate where another skill was.
/// </para>
/// </summary>
public sealed record MentorLearnerStandingDto(
    int CertId,
    string CertCode,
    bool HasNoMeasurement,
    string? OverallLevelCode,
    int? LimitingSkillId,
    string? LimitingSkillCode,
    IReadOnlyList<MentorSkillStandingDto> Skills);
