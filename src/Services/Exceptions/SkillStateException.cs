using System;

namespace Services;

/// <summary>
/// #211: a per-skill state that cannot be recorded as stated. Every code names a refusal, not a
/// fallback: the caller is told which value is wrong and why it cannot be stored, because a clamped
/// value (140 → 100) would reach a screen reading as "top of the band".
/// </summary>
public class SkillStateException(string code, string message) : Exception(message)
{
    /// <summary>The position is not inside 0-100.</summary>
    public const string InvalidPosition = "skill_state_invalid_position";

    /// <summary>The coordinate is outside the 1.00-6.99 measurement axis.</summary>
    public const string OutsideScale = "skill_state_outside_scale";

    /// <summary>The source is not one of the three events that measure a skill.</summary>
    public const string UnknownSource = "skill_state_unknown_source";

    /// <summary>The skill does not belong to the certificate the state is being recorded under.</summary>
    public const string SkillNotInCertificate = "skill_state_skill_not_in_certificate";

    public string Code { get; } = code;
}
