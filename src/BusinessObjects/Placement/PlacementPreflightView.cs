using System;
using System.Collections.Generic;
using System.Linq;

namespace BusinessObjects;

/// <summary>
/// #213 / SCR-PL-01: what a placement is about to measure, what may be carried over, and what it will
/// cost the learner — the gate before the sitting, not a catalogue of papers.
/// <para>
/// <b>Only the certificate's own skills appear.</b> The list is built from <c>Skills</c> rows of the
/// active certificate, so a certificate that examines two modalities offers two. A skill it does not
/// examine is not shown and not asked for: no microphone prompt on a reading paper.
/// </para>
/// <para>
/// <b>A reusable skill carries its ORIGINAL measurement date.</b> <see cref="PlacementSkillOffer.MeasuredAt"/>
/// is read from <c>LearnerSkillStates</c> and never restamped; the badge on the screen is what tells the
/// learner how old the number is. A reuse that wrote a new measurement would be a second measurement
/// pretending to be a carry-over.
/// </para>
/// </summary>
/// <param name="CertId">The certificate being placed.</param>
/// <param name="CertCode">Its code, so the screen can name it without a second lookup.</param>
/// <param name="ExamId">
/// The placement exam the start form submits. Null when the certificate has no published placement paper,
/// in which case there is nothing to start and the screen must say so rather than post an empty id.
/// </param>
/// <param name="Offers">One entry per skill the certificate examines, in the certificate's own order.</param>
/// <param name="CooldownUntil">
/// When a retake becomes available. Null means the learner may start now.
/// </param>
/// <param name="CooldownWaived">
/// True when the previous sitting failed on the system's side, so the cooldown was waived rather than
/// never applied. Distinguishing the two lets the screen say why.
/// </param>
/// <param name="HasReusableSkills">True when at least one skill may be carried over.</param>
public sealed record PlacementPreflightView(
    int CertId,
    string CertCode,
    int? ExamId,
    IReadOnlyList<PlacementSkillOffer> Offers,
    DateTimeOffset? CooldownUntil,
    bool CooldownWaived,
    bool HasReusableSkills)
{
    /// <summary>The single gate the start endpoint re-checks; the screen is not trusted to honour it.</summary>
    public bool IsStartable => CooldownUntil is null && ExamId is not null;

    /// <summary>Every examined skill must be supplied; a carried one is supplied by the prior sitting.</summary>
    public bool RequiresMicrophone =>
        Offers.Any(o => o.RequiresMicrophone && !o.CanReuse);
    public bool RequiresAudio => Offers.Any(o => o.RequiresAudio && !o.CanReuse);
}

/// <summary>
/// #213: one skill the placement will handle, and whether it has to be measured again.
/// </summary>
/// <param name="SkillId">The certificate's skill.</param>
/// <param name="SkillCode">Its own code, so a placard can name it.</param>
/// <param name="SkillName">Its display name.</param>
/// <param name="Modality">listening, reading, speaking or writing.</param>
/// <param name="RequiresMicrophone">A speaking skill needs one; asking a reading-only paper does not.</param>
/// <param name="RequiresAudio">A listening skill plays audio.</param>
/// <param name="Level">The level it would be carried over at; null when it must be measured.</param>
/// <param name="PositionInBand">0-100 inside that level. A position, never a percentage, never a band.</param>
/// <param name="MeasuredAt">The ORIGINAL measurement date of the carried result.</param>
/// <param name="ExpiresAt">When D18's 90-day reuse window closes for that result.</param>
/// <param name="CanReuse">
/// True when a stored state exists AND <see cref="ExpiresAt"/> has not passed. A stale state is shown —
/// it is still the answer — but it is not offered as a carry-over, because D18 does not allow it.
/// </param>
public sealed record PlacementSkillOffer(
    int SkillId,
    string SkillCode,
    string SkillName,
    string Modality,
    bool RequiresMicrophone,
    bool RequiresAudio,
    CefrLevel? Level,
    int? PositionInBand,
    DateTimeOffset? MeasuredAt,
    DateTimeOffset? ExpiresAt,
    bool CanReuse)
{
    /// <summary>PLC-06: from day 60 a carried result is aged and the screen says so.</summary>
    public bool IsAged(DateTimeOffset now) =>
        CanReuse && MeasuredAt is DateTimeOffset at && now >= at.AddDays(60);
}

/// <summary>#213: how the learner answered the preflight's one question.</summary>
public enum PlacementReuseChoice
{
    /// <summary>Carry over every skill whose stored result is inside the 90-day window, measure the rest.</summary>
    ReuseValid,

    /// <summary>Ignore every stored result and measure the whole paper again (story 52).</summary>
    Remeasure,
}
