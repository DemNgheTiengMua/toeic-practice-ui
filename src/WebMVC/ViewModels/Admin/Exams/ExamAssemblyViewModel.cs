using System;
using System.Collections.Generic;
using System.Linq;
using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #225 / S-09: the assembly workbench. One row per section, and the row's whole job is the gap
/// between <see cref="ExamAssemblySectionRowViewModel.Declared"/> and
/// <see cref="ExamAssemblySectionRowViewModel.Supply"/>. An over-demand section reads as a shortfall
/// with a figure and a word, never as a silently short paper.
/// <para>
/// <b>No arithmetic lives here.</b> The shortfall is a column of <see cref="ExamAssemblySectionDto"/>,
/// as the service computed it, for the reason <see cref="AdminCoverageViewModel"/> gives: a second
/// subtraction over the same stock is how a screen starts disagreeing with the pool it reports. This
/// type turns codes into text and states, and nothing else.
/// </para>
/// <para>
/// The domain language is §4's: sections, items, versions, positions and units. No "question" and no
/// "test".
/// </para>
/// </summary>
public sealed record ExamAssemblyViewModel(
    int ExamId,
    string ExamName,
    string CertCode,
    string TierLabel,
    bool IsProductionTier,
    int DurationMinutes,
    int FinishedAttemptCount,
    IReadOnlyList<ExamAssemblySectionRowViewModel> Sections)
{
    /// <summary>Set when the payload carried no section at all, rather than when the list is short.</summary>
    public bool HasNoSections => Sections.Count == 0;

    /// <summary>
    /// True when the paper is structurally locked (#157): a finished attempt was scored against this
    /// shape, so nothing may be bound. The screen still reads — an audit needs to see it — but the
    /// bind controls are absent rather than present and refused.
    /// </summary>
    public bool IsLocked => FinishedAttemptCount > 0;
}

/// <summary>
/// One section's line. <see cref="StateClass"/> is a named state rather than an expression in the
/// markup, so the short, met and empty rows cannot be painted by three comparisons that drift.
/// </summary>
public sealed record ExamAssemblySectionRowViewModel(
    int SectionId,
    string Code,
    string Name,
    string ModalityLabel,
    int DisplayOrder,
    int Declared,
    int Bound,
    int Supply,
    int Shortfall,
    IReadOnlyList<ExamAssemblyPositionRowViewModel> Positions,
    IReadOnlyList<ExamAssemblyCandidateRowViewModel> Candidates,
    int CandidateTotal)
{
    /// <summary>Nothing in the pool can fill this section, so it is empty rather than thin.</summary>
    public bool IsEmpty => Supply == 0;

    /// <summary>The pool cannot fill what the certificate declares — the ticket's shortfall.</summary>
    public bool IsShort => Shortfall > 0 && Supply > 0;

    public bool IsMet => Shortfall == 0;

    public string StateClass =>
        IsEmpty ? "assembly-row--empty" : IsShort ? "assembly-row--short" : "assembly-row--met";

    /// <summary>
    /// The shortfall in words as well as figures, because colour is the second layer of information:
    /// a lone "-9" names no noun and no unit.
    /// </summary>
    public string ShortfallText => Shortfall == 0 ? "0 short" : $"{Shortfall} short";

    /// <summary>What the paper already holds against what it declares.</summary>
    public string BoundText => $"{Bound} / {Declared}";

    /// <summary>True when the candidate list was truncated, so the screen says so rather than implies it.</summary>
    public bool IsCandidateListTruncated => CandidateTotal > Candidates.Count;

    public string CandidateSummary => IsCandidateListTruncated
        ? $"Showing {Candidates.Count} of {CandidateTotal} servable versions"
        : $"{CandidateTotal} servable version{(CandidateTotal == 1 ? string.Empty : "s")}";

    /// <summary>
    /// The first unbound declared position, so the bind form's default is the paper's next slot rather
    /// than 1 — a default of 1 would collide on every bind after the first and the author would be told
    /// the position is taken by their own work.
    /// </summary>
    public int NextOpenPosition => Positions.FirstOrDefault(p => p.IsDeclared && !p.IsBound)?.DisplayOrder
        ?? Declared;
}

/// <summary>One position of the paper, and whether the paper already fills it.</summary>
public sealed record ExamAssemblyPositionRowViewModel(
    int DisplayOrder,
    bool IsDeclared,
    bool IsBound,
    string TopicCode,
    string CefrLevelLabel,
    int UnitNumber,
    int? ItemVersionId)
{
    public string SlotText => IsBound ? TopicCode : "Unbound";

    /// <summary>
    /// A position past the declared count. It is a real row and it is shown: hiding it would make the
    /// bound total disagree with the positions beside it.
    /// </summary>
    public bool IsBeyondDeclaration => !IsDeclared;
}

/// <summary>
/// One bindable version. <see cref="TierLabel"/> is where INV-6 becomes visible before the click
/// rather than at the refusal.
/// </summary>
public sealed record ExamAssemblyCandidateRowViewModel(
    int ItemId,
    int ItemVersionId,
    int VersionNumber,
    string TopicCode,
    string CefrLevelLabel,
    string ItemTypeLabel,
    string TierLabel,
    string Stem);

/// <summary>
/// Maps the read model onto the screen. Codes become text through the two registries and a state
/// becomes a name; no figure is recomputed.
/// </summary>
public static class ExamAssemblyPresentation
{
    /// <summary>
    /// The two tier codes, restated because WebMVC cannot see <c>BusinessObjects</c>. They are the
    /// schema's own vocabulary (<c>CK_Exams_ContentTier</c>); the service decides which tier a paper
    /// may draw, and this only labels what came back.
    /// </summary>
    private const string ProductionTier = "production";

    private const string DemoStockTier = "demo";

    public static ExamAssemblyViewModel From(
        ExamAssemblyDto dto,
        Func<string?, string> levelLabel,
        Func<string?, string> modalityLabel) =>
        new(
            dto.ExamId,
            dto.ExamName,
            dto.CertCode.ToUpperInvariant(),
            TierLabelFor(dto.ContentTier),
            string.Equals(dto.ContentTier, ProductionTier, StringComparison.Ordinal),
            dto.DurationMinutes,
            dto.FinishedAttemptCount,
            dto.Sections.Select(section => new ExamAssemblySectionRowViewModel(
                    section.SectionId,
                    section.Code,
                    section.Name,
                    modalityLabel(section.Modality.ToWireCode()),
                    section.DisplayOrder,
                    section.DeclaredCount,
                    section.BoundCount,
                    section.SupplyCount,
                    section.Shortfall,
                    section.Positions
                        .Select(position => new ExamAssemblyPositionRowViewModel(
                            position.DisplayOrder,
                            position.IsDeclared,
                            position.ItemVersionId is not null,
                            position.TopicCode ?? string.Empty,
                            position.CefrLevel is null ? string.Empty : levelLabel(position.CefrLevel),
                            position.UnitNumber ?? 1,
                            position.ItemVersionId))
                        .ToList(),
                    section.Candidates
                        .Select(candidate => new ExamAssemblyCandidateRowViewModel(
                            candidate.ItemId,
                            candidate.ItemVersionId,
                            candidate.VersionNumber,
                            candidate.TopicCode,
                            levelLabel(candidate.CefrLevel),
                            ItemTypeLabelFor(candidate.ItemType),
                            TierLabelFor(candidate.ContentTier),
                            candidate.Stem))
                        .ToList(),
                    section.CandidateTotal))
                .ToList());

    /// <summary>
    /// The two tiers in the domain's words. Never the stored code: "production" beside a paper reads
    /// as a stage of the build, and the tier is a supply fact.
    /// </summary>
    public static string TierLabelFor(string? tier) => tier switch
    {
        ProductionTier => "Production stock",
        DemoStockTier => "Demo stock",
        _ => tier ?? "Unknown tier",
    };

    /// <summary>
    /// D0.4's authored types in words. An unmapped code falls back to the code itself rather than to
    /// a sentence, because the type is the author's vocabulary and a wrong label is worse than a raw
    /// one on an internal screen.
    /// </summary>
    public static string ItemTypeLabelFor(string? itemType) => itemType switch
    {
        "short_exchange" => "Short exchange",
        "extended_dialogue" => "Extended dialogue",
        "monologue_talk" => "Monologue or talk",
        "sentence_cloze" => "Sentence cloze",
        "passage_comprehension" => "Passage comprehension",
        "text_insertion_cohesion" => "Text insertion",
        "descriptive_monologue" => "Descriptive monologue",
        "situational_negotiation" => "Situational negotiation",
        "argumentative_monologue" => "Argumentative monologue",
        "visual_data_synthesis" => "Visual data synthesis",
        "transactional_correspondence" => "Transactional correspondence",
        "persuasive_essay" => "Persuasive essay",
        "analytical_evaluation_essay" => "Analytical evaluation essay",
        _ => itemType ?? "Unknown type",
    };
}
