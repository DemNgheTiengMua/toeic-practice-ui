using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #208: the rubric and grader calibration studio.
/// <para>
/// <b>What this owns, and what it delegates.</b> It owns the grid's round trip (28 cells and three
/// anchors per criterion), the lock, and the translation of a ladder verdict onto the lock row. It owns
/// none of the ladder's arithmetic: <see cref="RubricCalibrationLadder.RunAsync"/> is #193's one
/// implementation, and this class calls it exactly as <see cref="GateFourIngestion"/> does. A second
/// copy of "is a segment 0.65 apart" would be a second answer to one question, and the copy that drifted
/// would be the one nothing asserts.
/// </para>
/// <para>
/// <b>The hole this closes.</b> Before #208 a rubric with all 28 cells could be locked while the ladder
/// had never run: the lock trigger checked shape, and <see cref="GateFourRules.LadderNotPlotted"/> -
/// the code that exists to name "the ladder did not run" - was reachable only from ingestion. Now the
/// lock carries the verdict (<c>GradingRubricLocks.LadderOutcome</c>) and the trigger refuses anything
/// but <c>accepted</c>, with one message per failure.
/// </para>
/// <para>
/// <b>A rubric band is not a certificate band.</b> <c>Band</c> here is 0-6 on the CEFR ladder the
/// descriptors are written on (D0.6); a certificate's <c>ScoreBands</c> row is a scored range with no
/// crosswalk to any level (PRN-03). Nothing in this file reads a ScoreBand.
/// </para>
/// </summary>
public sealed class RubricStudioService(
    CompanionDbContext db,
    IAiGrader grader,
    TimeProvider clock) : IRubricStudioService
{
    // ---- the grid ------------------------------------------------------------------------------

    public async Task<RubricStudioDto> GetAsync(int certId, int skillId, CancellationToken cancellationToken = default)
    {
        var (skill, cert) = await RequireSkillAsync(certId, skillId, cancellationToken);
        return await ReadAsync(skill, cert, cancellationToken);
    }

    public async Task<RubricStudioDto> SaveAsync(SaveRubricRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (skill, cert) = await RequireSkillAsync(request.CertId, request.SkillId, cancellationToken);

        if (await IsLockedAsync(skillId: skill.SkillId, cancellationToken))
        {
            throw Refused("This rubric is locked, so its descriptors cannot be changed. A locked rubric is the "
                + "standard every stored score was measured against; author a new one instead.");
        }

        var criteria = await db.GradingCriteria
            .Where(c => c.SkillId == skill.SkillId && c.CertId == skill.CertId)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(cancellationToken);

        var posted = request.Criteria ?? [];
        if (posted.Count != criteria.Count)
        {
            throw Refused($"This skill's rubric has {criteria.Count} criteria and the save posted {posted.Count}. "
                + "The grid is the whole rubric: a save that adds or drops a criterion would leave bands unwritten.");
        }

        // The refusal is before any write, and it names every hole at once: an author fixing a 4x7 grid
        // one band per round trip is the failure mode a per-cell refusal produces.
        var incomplete = new List<string>();
        foreach (var criterion in criteria)
        {
            var cell = posted.FirstOrDefault(p => p.CriterionId == criterion.CriterionId);
            if (cell is null)
            {
                incomplete.Add($"{criterion.Code} has no row in the save");
                continue;
            }

            var written = (cell.Cells ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c.Descriptor) && !string.IsNullOrWhiteSpace(c.BinaryGates))
                .Select(c => c.Band)
                .ToHashSet();
            var missing = RubricBands.All.Where(band => !written.Contains(band)).ToList();
            if (missing.Count > 0)
            {
                incomplete.Add($"{criterion.Code} is missing band {string.Join(", band ", missing)}");
            }
        }

        if (incomplete.Count > 0)
        {
            // The schema refuses this too (50046). Refusing here as well is not duplication: the door
            // is the trigger, and this is the sentence an author can act on without reading SQL Server.
            throw Refused("The rubric cannot be saved while a cell is empty: " + string.Join("; ", incomplete)
                + ". Every criterion needs a descriptor and a gate at every band, because a band with no "
                + "standard is a band the grader would have to invent.");
        }

        // One transaction: a criterion's grid and its anchors travel together, or the rubric half-exists
        // and the next read shows an author a grid they did not write.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var criterion in criteria)
        {
            var postedCriterion = posted.Single(p => p.CriterionId == criterion.CriterionId);
            await ReplaceCellsAsync(criterion.CriterionId, postedCriterion.Cells ?? [], cancellationToken);
            await ReplaceAnchorsAsync(criterion.CriterionId, postedCriterion.Anchors ?? [], cancellationToken);
        }

        // EF SaveChanges, never ExecuteSqlRaw: StaffAccountStampInterceptor stamps LastWrittenBy and
        // #201's strict catalog refuses a descriptor write that declares no author.
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ReadAsync(skill, cert, cancellationToken);
    }

    // ---- the ladder ----------------------------------------------------------------------------

    public async Task<RubricLadderDto> RunLadderAsync(
        RunRubricLadderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (skill, _) = await RequireSkillAsync(request.CertId, request.SkillId, cancellationToken);

        var criteria = await CriteriaForLadderAsync(skill.SkillId, cancellationToken);
        var responses = (request.Responses ?? [])
            .Select(r => new RubricLadderResponse(r.Band, r.ResponseText, r.IsVerboseTrap))
            .ToList();

        return await LadderAsync(criteria, responses, cancellationToken);
    }

    /// <summary>
    /// Runs the ladder and, only if it accepted, writes the verdict and the lock row in one save.
    /// <para>
    /// A refusal returns the verdict and writes nothing at all - not the lock, and not the verdict. A
    /// stored <c>separation</c> is a recorded fact about a rubric that is still being written; keeping
    /// it would make the lock trigger's job ambiguous once the author has fixed the rubric and re-run.
    /// </para>
    /// </summary>
    public async Task<RubricLadderDto> LockAsync(
        RunRubricLadderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (skill, _) = await RequireSkillAsync(request.CertId, request.SkillId, cancellationToken);

        if (await IsLockedAsync(skill.SkillId, cancellationToken))
        {
            throw Refused("This rubric is already locked. A locked rubric is the standard every stored score "
                + "was measured against, so it is not re-run and not replaced - author a new one.");
        }

        var criteria = await CriteriaForLadderAsync(skill.SkillId, cancellationToken);
        var responses = (request.Responses ?? [])
            .Select(r => new RubricLadderResponse(r.Band, r.ResponseText, r.IsVerboseTrap))
            .ToList();

        var ladder = await LadderAsync(criteria, responses, cancellationToken);
        if (!ladder.Accepted)
        {
            return ladder;
        }

        db.GradingRubricLocks.Add(new GradingRubricLock
        {
            SkillId = skill.SkillId,
            CertId = skill.CertId,
            LadderOutcome = RubricLadderOutcomes.Accepted,
            LadderReason = ladder.Reason ?? "The ladder separated every adjacent band and the trap stayed under its ceiling.",
            LadderRunAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);

        return ladder with { RanAt = clock.GetUtcNow() };
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>
    /// The ladder, through #193's one implementation, with its verdict read onto the schema's codes.
    /// <para>
    /// A ladder that cannot run is a <c>not_plotted</c> verdict rather than an exception: the studio
    /// screen has to render the reason, and a thrown exception in an action is a 500 the author reads as
    /// "the server broke" instead of "band 4's response is missing".
    /// </para>
    /// </summary>
    private async Task<RubricLadderDto> LadderAsync(
        IReadOnlyList<GradingCriterionDetail> criteria,
        IReadOnlyList<RubricLadderResponse> responses,
        CancellationToken cancellationToken)
    {
        RubricCalibrationResult result;
        try
        {
            result = await RubricCalibrationLadder.RunAsync(criteria, grader, responses, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            // The ladder's own refusal to run: no criteria, a missing benchmark band, no trap. Its
            // message names the missing thing, which is what the author has to fix.
            return new RubricLadderDto(
                RubricLadderOutcomes.NotPlotted, false,
                "The ladder could not be plotted: " + exception.Message,
                [], [], clock.GetUtcNow());
        }

        var outcome = result.Outcome switch
        {
            RubricCalibrationOutcome.Accepted => RubricLadderOutcomes.Accepted,
            RubricCalibrationOutcome.NotStrictlyMonotonic => RubricLadderOutcomes.NotMonotonic,
            RubricCalibrationOutcome.SeparationBelowThreshold => RubricLadderOutcomes.Separation,
            RubricCalibrationOutcome.VerboseTrapAboveCeiling => RubricLadderOutcomes.TrapCeiling,
            // GraderFailed is the default arm of #197's mapping too, and for the same reason: a ladder
            // over no usable grade has not been plotted, whatever else went wrong. Calling it a failure
            // of the rubric would send the author to rewrite descriptors the grader never read.
            _ => RubricLadderOutcomes.NotPlotted,
        };

        return new RubricLadderDto(
            outcome,
            result.Accepted,
            result.Refusal,
            result.ResponsibleCriteria,
            result.Ladder.Select(p => new RubricLadderPointDto(p.Band, p.Composite, p.IsVerboseTrap)).ToList(),
            clock.GetUtcNow());
    }

    /// <summary>
    /// The criteria as a grading call sees them, including the grid and the anchors. The ladder grades
    /// with the same <see cref="GradingCriterionDetail"/> a learner's response is graded with, so a
    /// rubric cannot be calibrated against one standard and used against another.
    /// </summary>
    private async Task<IReadOnlyList<GradingCriterionDetail>> CriteriaForLadderAsync(
        int skillId, CancellationToken cancellationToken)
    {
        var criteria = await db.GradingCriteria.AsNoTracking()
            .Where(c => c.SkillId == skillId)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(cancellationToken);

        var ids = criteria.Select(c => c.CriterionId).ToArray();
        var grids = await db.GradingCriterionDescriptors.AsNoTracking()
            .Where(d => ids.Contains(d.CriterionId))
            .GroupBy(d => d.CriterionId)
            .Select(g => new
            {
                CriterionId = g.Key,
                Bands = g.OrderBy(d => d.Band)
                         .Select(d => new GradingCriterionBandDescriptor(d.Band, d.Descriptor, d.BinaryGates))
                         .ToList(),
            })
            .ToDictionaryAsync(x => x.CriterionId, x => (IReadOnlyList<GradingCriterionBandDescriptor>)x.Bands, cancellationToken);

        var anchors = await db.GradingCriterionAnchors.AsNoTracking()
            .Where(a => ids.Contains(a.CriterionId))
            .GroupBy(a => a.CriterionId)
            .Select(g => new
            {
                CriterionId = g.Key,
                Bands = g.OrderBy(a => a.Band)
                         .Select(a => new GradingCriterionBandAnchor(a.Band, a.ResponseText))
                         .ToList(),
            })
            .ToDictionaryAsync(x => x.CriterionId, x => (IReadOnlyList<GradingCriterionBandAnchor>)x.Bands, cancellationToken);

        return criteria
            .Select(c => new GradingCriterionDetail(
                c.CriterionId, c.Code, c.Name, c.MaxScore, c.Weight, c.RubricDescription,
                grids.GetValueOrDefault(c.CriterionId),
                anchors.GetValueOrDefault(c.CriterionId)))
            .ToList();
    }

    private async Task<RubricStudioDto> ReadAsync(
        Skill skill, Certificate cert, CancellationToken cancellationToken)
    {
        var criteria = await db.GradingCriteria.AsNoTracking()
            .Where(c => c.SkillId == skill.SkillId)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(cancellationToken);

        var ids = criteria.Select(c => c.CriterionId).ToArray();

        var grids = await db.GradingCriterionDescriptors.AsNoTracking()
            .Where(d => ids.Contains(d.CriterionId))
            .GroupBy(d => d.CriterionId)
            .Select(g => new
            {
                CriterionId = g.Key,
                Cells = g.OrderBy(d => d.Band)
                         .Select(d => new RubricCellDto(d.Band, d.Descriptor, d.BinaryGates))
                         .ToList(),
            })
            .ToDictionaryAsync(x => x.CriterionId, x => (IReadOnlyList<RubricCellDto>)x.Cells, cancellationToken);

        var anchors = await db.GradingCriterionAnchors.AsNoTracking()
            .Where(a => ids.Contains(a.CriterionId))
            .GroupBy(a => a.CriterionId)
            .Select(g => new
            {
                CriterionId = g.Key,
                Bands = g.OrderBy(a => a.Band)
                         .Select(a => new RubricAnchorDto(a.Band, a.ResponseText))
                         .ToList(),
            })
            .ToDictionaryAsync(x => x.CriterionId, x => (IReadOnlyList<RubricAnchorDto>)x.Bands, cancellationToken);

        var lockRow = await db.GradingRubricLocks.AsNoTracking()
            .SingleOrDefaultAsync(l => l.SkillId == skill.SkillId, cancellationToken);

        return new RubricStudioDto(
            cert.CertId, cert.Code,
            skill.SkillId, skill.Code, skill.Name, skill.Modality,
            lockRow is not null,
            lockRow?.LockedAt,
            criteria.Select(c => new RubricCriterionDto(
                c.CriterionId, c.Code, c.Name, c.MaxScore, c.Weight, c.DisplayOrder,
                grids.GetValueOrDefault(c.CriterionId) ?? [],
                anchors.GetValueOrDefault(c.CriterionId) ?? [])).ToList(),
            // A lock recorded its verdict; an unlocked rubric has none to show. The screen paints
            // "the ladder has not run" from this null, which is the state that used to lock silently.
            lockRow?.LadderOutcome is null
                ? null
                : new RubricLadderDto(
                    lockRow.LadderOutcome, lockRow.LadderOutcome == RubricLadderOutcomes.Accepted,
                    lockRow.LadderReason, [], [], lockRow.LadderRunAt));
    }

    /// <summary>
    /// The grid as posted is the grid stored, on the same rule #192 chose for the wizard: bands the
    /// request omits are deleted, not merged, so a seven-band grid cannot keep an eighth row from an
    /// earlier save. The caller has already refused an incomplete grid, so "omits" here means "was never
    /// there", not "the author left it blank".
    /// </summary>
    private async Task ReplaceCellsAsync(
        int criterionId, IReadOnlyList<RubricCellDto> cells, CancellationToken cancellationToken)
    {
        var existing = await db.GradingCriterionDescriptors
            .Where(d => d.CriterionId == criterionId)
            .ToListAsync(cancellationToken);

        var incoming = cells
            .GroupBy(c => c.Band)
            .Select(g => g.First())
            .ToDictionary(c => c.Band);

        db.GradingCriterionDescriptors.RemoveRange(existing.Where(d => !incoming.ContainsKey(d.Band)));
        foreach (var band in incoming.Values.OrderBy(c => c.Band))
        {
            var row = existing.FirstOrDefault(d => d.Band == band.Band);
            if (row is null)
            {
                db.GradingCriterionDescriptors.Add(new GradingCriterionDescriptor
                {
                    CriterionId = criterionId,
                    Band = band.Band,
                    Descriptor = band.Descriptor.Trim(),
                    BinaryGates = band.BinaryGates.Trim(),
                });
            }
            else
            {
                row.Descriptor = band.Descriptor.Trim();
                row.BinaryGates = band.BinaryGates.Trim();
            }
        }
    }

    /// <summary>
    /// The anchors, on the same rule. Only bands 2, 3 and 4 are storable at all (CK_GCA_Band), so a
    /// posted anchor at any other band is refused by the table rather than silently kept.
    /// </summary>
    private async Task ReplaceAnchorsAsync(
        int criterionId, IReadOnlyList<RubricAnchorDto> anchors, CancellationToken cancellationToken)
    {
        var existing = await db.GradingCriterionAnchors
            .Where(a => a.CriterionId == criterionId)
            .ToListAsync(cancellationToken);

        var incoming = anchors
            .Where(a => !string.IsNullOrWhiteSpace(a.ResponseText))
            .GroupBy(a => a.Band)
            .Select(g => g.First())
            .ToDictionary(a => a.Band);

        db.GradingCriterionAnchors.RemoveRange(existing.Where(a => !incoming.ContainsKey(a.Band)));
        foreach (var band in incoming.Values.OrderBy(a => a.Band))
        {
            var row = existing.FirstOrDefault(a => a.Band == band.Band);
            if (row is null)
            {
                db.GradingCriterionAnchors.Add(new GradingCriterionAnchor
                {
                    CriterionId = criterionId,
                    Band = band.Band,
                    ResponseText = band.ResponseText.Trim(),
                });
            }
            else
            {
                row.ResponseText = band.ResponseText.Trim();
            }
        }
    }

    private async Task<bool> IsLockedAsync(int skillId, CancellationToken cancellationToken) =>
        await db.GradingRubricLocks.AnyAsync(l => l.SkillId == skillId, cancellationToken);

    /// <summary>
    /// The skill and its certificate, or a refusal that names which of the two is wrong. A rubric is
    /// certificate-scoped through <c>Skills(CertId, SkillId)</c>, so a mismatched pair is not an empty
    /// screen - it is a request that would read another certificate's rubric.
    /// </summary>
    private async Task<(Skill Skill, Certificate Cert)> RequireSkillAsync(
        int certId, int skillId, CancellationToken cancellationToken)
    {
        var skill = await db.Skills.AsNoTracking()
            .SingleOrDefaultAsync(s => s.SkillId == skillId && s.CertId == certId, cancellationToken);

        if (skill is null)
        {
            throw new AdminItemException(
                ApiError.Codes.NotFound,
                $"Skill {skillId} does not belong to certificate {certId}. A rubric is scoped to its own "
                + "certificate, so this pair names nothing.");
        }

        var cert = await db.Certificates.AsNoTracking()
            .SingleAsync(c => c.CertId == certId, cancellationToken);

        if (!CanonicalRubric.HasRubric(skill.Modality))
        {
            throw Refused($"'{skill.Name}' is a {skill.Modality} skill, and that skill is scored by its key, "
                + "not by descriptors. Only a speaking or writing skill has an analytic rubric to calibrate.");
        }

        return (skill, cert);
    }

    private static AdminItemException Refused(string message) =>
        new(ApiError.Codes.Validation, message);
}
