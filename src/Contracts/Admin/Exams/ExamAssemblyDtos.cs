using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #225 / S-09: the assembly workbench's read model — a paper's sections, what each one declares, how
/// much servable stock the pool can supply for it, and where it currently stands.
/// <para>
/// <b>The two numbers the screen exists for.</b> <see cref="ExamAssemblySectionDto.DeclaredCount"/> is
/// the shape the certificate declares (<c>Sections.ExpectedQuestionCount</c>);
/// <see cref="ExamAssemblySectionDto.SupplyCount"/> is what the warehouse can actually fill it with.
/// <see cref="ExamAssemblySectionDto.Shortfall"/> is their gap, computed once, here — a screen that
/// subtracted the two itself would be a second arithmetic path over the same stock and would
/// eventually disagree with the pool it reports.
/// </para>
/// <para>
/// <b>Nothing here is a stored fact.</b> There is no "assembly progress" column and no stored supply
/// count: the pool moves under a paper every time an item is retired, quarantined or promoted, so a
/// figure written once would be wrong the moment it mattered. Every number below is read at the
/// moment the screen is opened.
/// </para>
/// </summary>
public sealed record ExamAssemblyDto(
    int ExamId,
    string ExamName,
    string CertCode,
    string ContentTier,
    int DurationMinutes,
    int FinishedAttemptCount,
    IReadOnlyList<ExamAssemblySectionDto> Sections);

/// <summary>
/// One section of the paper. <see cref="Positions"/> is the shape the paper declares — one entry per
/// position, bound or not — so the screen can point at an empty slot rather than only naming a count.
/// </summary>
public sealed record ExamAssemblySectionDto(
    int SectionId,
    string Code,
    string Name,
    ModalityDto Modality,
    int DisplayOrder,
    int DeclaredCount,
    int BoundCount,
    int SupplyCount,
    int Shortfall,
    IReadOnlyList<ExamAssemblyPositionDto> Positions,
    IReadOnlyList<ExamAssemblyCandidateDto> Candidates,
    int CandidateTotal);

/// <summary>
/// One position on the paper, 1-based within its section — the same key
/// <c>UQ_ExamItems_Exam_Section_Order</c> makes unique and the sitting projection orders by.
/// <see cref="IsDeclared"/> is false for a binding placed beyond the section's declared count: that is
/// a real row and hiding it would make the bound total disagree with the positions on screen.
/// </summary>
public sealed record ExamAssemblyPositionDto(
    int DisplayOrder,
    bool IsDeclared,
    int? ItemVersionId,
    string? TopicCode,
    string? CefrLevel,
    int? UnitNumber);

/// <summary>
/// One version the section may bind. <see cref="ItemVersionId"/> is the newest version of an
/// <b>active</b> item in this section's skill cell — the only stock a learner is served (CNT-42).
/// <para>
/// <see cref="ContentTier"/> is carried because a production paper may only draw production stock
/// (INV-6), so a candidate list that mixed the tiers would offer versions the bind refuses.
/// </para>
/// </summary>
public sealed record ExamAssemblyCandidateDto(
    int ItemId,
    int ItemVersionId,
    int VersionNumber,
    string TopicCode,
    string CefrLevel,
    string ItemType,
    string ContentTier,
    string Stem);
