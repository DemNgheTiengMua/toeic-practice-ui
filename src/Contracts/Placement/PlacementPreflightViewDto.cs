using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #213 / SCR-PL-01: the placement preflight — what will be measured, what may be carried over, and
/// whether a retake is allowed yet.
/// </summary>
/// <param name="CertId">The certificate being placed.</param>
/// <param name="CertCode">Its code.</param>
/// <param name="Offers">One entry per skill the certificate examines, in its own order.</param>
/// <param name="CooldownUntil">When a retake opens; null means now.</param>
/// <param name="CooldownWaived">True when the cooldown was waived because the previous sitting failed on the system's side.</param>
/// <param name="HasReusableSkills">True when at least one skill may be carried over.</param>
/// <param name="IsStartable">The single gate the start endpoint also enforces.</param>
/// <param name="RequiresMicrophone">A skill that must be measured needs a microphone.</param>
/// <param name="RequiresAudio">A skill that must be measured plays audio.</param>
public sealed record PlacementPreflightViewDto(
    int CertId,
    string CertCode,
    int? ExamId,
    List<PlacementSkillOfferDto> Offers,
    DateTimeOffset? CooldownUntil,
    bool CooldownWaived,
    bool HasReusableSkills,
    bool IsStartable,
    bool RequiresMicrophone,
    bool RequiresAudio,
    bool IsRetake = false,
    int AllSkillsRetakeCredits = 5,
    int ListeningAndReadingRetakeCredits = 3,
    int? CreditBalance = null);

/// <summary>
/// #213: one skill the placement will handle. <paramref name="CanReuse"/> is the only field the choice
/// turns on; the level, position and date are what the screen shows when it is true.
/// </summary>
public sealed record PlacementSkillOfferDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    string Modality,
    bool RequiresMicrophone,
    bool RequiresAudio,
    string? LevelCode,
    int? PositionInBand,
    DateTimeOffset? MeasuredAt,
    DateTimeOffset? ExpiresAt,
    bool CanReuse,
    bool IsAged);
