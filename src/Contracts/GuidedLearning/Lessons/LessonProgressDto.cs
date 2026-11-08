namespace Contracts;

/// <summary>
/// #240: where a learner stopped inside one module of a lesson.
/// <para>
/// <see cref="ResumeCardIndex"/> is the 1-based position in THIS module's card list - the number the
/// reader opens on. It is computed from <see cref="LastViewedCardId"/> by the service, so a client can
/// never disagree with the stored position, and "resume" is an exact card rather than a restart.
/// </para>
/// </summary>
public sealed record LessonProgressDto(
    int CardsCompleted,
    int TotalCards,
    bool IsCompleted,
    int? LastViewedCardId,
    DateTimeOffset? FirstOpenedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? UpdatedAt,
    int ResumeCardIndex = 1,
    string Module = LessonModuleCodes.Primary
);
