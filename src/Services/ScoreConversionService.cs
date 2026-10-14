using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services;

public class ScoreConversionException(string code, string message) : Exception(message)
{
    public const string InvalidData = "score_conversion_invalid";
    public const string CertificateNotFound = "certificate_not_found";
    public const string NonMonotonicScales = "non_monotonic_scales";
    public const string OverlappingBands = "overlapping_bands";
    public const string InvalidBandRange = "invalid_band_range";

    public string Code { get; } = code;
}

public interface IScoreConversionService
{
    Task<AdminScoreConversionView> GetScoreConversionAsync(string? certCode, int? examId = null, CancellationToken cancellationToken = default);

    Task<AdminScoreConversionView> UpdateScoreConversionAsync(int certId, UpdateScoreConversionCommand command, CancellationToken cancellationToken = default);
}

public sealed class ScoreConversionService(CompanionDbContext db) : IScoreConversionService
{
    public async Task<AdminScoreConversionView> GetScoreConversionAsync(string? certCode, int? examId = null, CancellationToken cancellationToken = default)
    {
        var certificates = await db.Certificates
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Code)
            .Select(c => new CertificateOption(c.Code, c.Name))
            .ToListAsync(cancellationToken);

        Certificate? cert = null;
        if (!string.IsNullOrWhiteSpace(certCode))
        {
            cert = await db.Certificates
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Code == certCode, cancellationToken);
        }

        cert ??= await db.Certificates
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Code)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ScoreConversionException(ScoreConversionException.CertificateNotFound, "No active certificates found.");

        string? examName = null;
        if (examId.HasValue)
        {
            examName = await db.Exams
                .AsNoTracking()
                .Where(e => e.ExamId == examId.Value && e.CertId == cert.CertId)
                .Select(e => e.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == cert.CertId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var existingScales = await db.ScoreScales
            .AsNoTracking()
            .Where(s => s.CertId == cert.CertId && s.ExamId == examId && s.SkillId != null)
            .ToListAsync(cancellationToken);

        var skillGroups = new List<SkillScoreScaleGroup>();
        foreach (var skill in skills)
        {
            var scalesForSkill = existingScales
                .Where(s => s.SkillId == skill.SkillId)
                .OrderBy(s => s.RawScore)
                .Select(s => new ScoreScaleRowItem(
                    s.ScaleId,
                    skill.SkillId,
                    skill.Code,
                    skill.Name,
                    s.RawScore,
                    s.ScaledScore))
                .ToList();

            // Default max questions for TOEIC is 100 per skill, or calculate from max raw score
            int maxQuestions = scalesForSkill.Count > 0 ? scalesForSkill.Max(s => s.RawScore) : 100;

            skillGroups.Add(new SkillScoreScaleGroup(
                skill.SkillId,
                skill.Code,
                skill.Name,
                maxQuestions,
                scalesForSkill));
        }

        var bands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == cert.CertId)
            .OrderBy(b => b.MinTotal)
            .Select(b => new ScoreBandConfigItem(
                b.BandId,
                b.Code,
                b.Name,
                b.MinTotal,
                b.MaxTotal,
                b.DisplayOrder))
            .ToListAsync(cancellationToken);

        return new AdminScoreConversionView(
            cert.CertId,
            cert.Code,
            cert.Name,
            certificates,
            examId,
            examName,
            skillGroups,
            bands);
    }

    public async Task<AdminScoreConversionView> UpdateScoreConversionAsync(int certId, UpdateScoreConversionCommand command, CancellationToken cancellationToken = default)
    {
        var cert = await db.Certificates
            .SingleOrDefaultAsync(c => c.CertId == certId, cancellationToken)
            ?? throw new ScoreConversionException(ScoreConversionException.CertificateNotFound, $"Certificate with ID {certId} not found.");

        // 1. Validate Scales Monotonicity
        if (command.Scales != null && command.Scales.Count > 0)
        {
            var bySkill = command.Scales.GroupBy(s => s.SkillId);
            foreach (var group in bySkill)
            {
                if (group.GroupBy(s => s.RawScore).Any(g => g.Count() > 1))
                {
                    throw new ScoreConversionException(ScoreConversionException.InvalidData, "Duplicate raw scores found for the same skill.");
                }

                if (group.Any(s => s.RawScore < 0 || s.ScaledScore < 0))
                {
                    throw new ScoreConversionException(ScoreConversionException.InvalidData, "Raw scores and scaled scores must be non-negative.");
                }

                var ordered = group.OrderBy(s => s.RawScore).ToList();
                for (int i = 0; i < ordered.Count - 1; i++)
                {
                    if (ordered[i + 1].ScaledScore < ordered[i].ScaledScore)
                    {
                        throw new ScoreConversionException(
                            ScoreConversionException.NonMonotonicScales,
                            $"Score scale must be monotonically non-decreasing. Raw score {ordered[i + 1].RawScore} has scaled score {ordered[i + 1].ScaledScore}, which is less than scaled score {ordered[i].ScaledScore} for raw score {ordered[i].RawScore}.");
                    }
                }
            }
        }

        // 2. Validate Bands: non-empty, min <= max, non-overlapping
        if (command.Bands != null && command.Bands.Count > 0)
        {
            foreach (var band in command.Bands)
            {
                if (string.IsNullOrWhiteSpace(band.Code) || string.IsNullOrWhiteSpace(band.Name))
                {
                    throw new ScoreConversionException(ScoreConversionException.InvalidData, "Band code and name cannot be empty.");
                }

                if (band.MinTotal < 0 || band.MaxTotal < 0)
                {
                    throw new ScoreConversionException(ScoreConversionException.InvalidData, "Band score thresholds cannot be negative.");
                }

                if (band.MaxTotal < band.MinTotal)
                {
                    throw new ScoreConversionException(
                        ScoreConversionException.InvalidBandRange,
                        $"Band '{band.Name}' has maximum score ({band.MaxTotal}) lower than minimum score ({band.MinTotal}).");
                }
            }

            var orderedBands = command.Bands.OrderBy(b => b.MinTotal).ThenBy(b => b.MaxTotal).ToList();
            for (int i = 0; i < orderedBands.Count - 1; i++)
            {
                if (orderedBands[i].MaxTotal >= orderedBands[i + 1].MinTotal)
                {
                    throw new ScoreConversionException(
                        ScoreConversionException.OverlappingBands,
                        $"Score bands '{orderedBands[i].Name}' and '{orderedBands[i + 1].Name}' have overlapping ranges [{orderedBands[i].MinTotal}, {orderedBands[i].MaxTotal}] and [{orderedBands[i + 1].MinTotal}, {orderedBands[i + 1].MaxTotal}].");
                }
            }
        }

        // 3. Persist Changes
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Update ScoreScales
            if (command.Scales != null)
            {
                var existingScales = await db.ScoreScales
                    .Where(s => s.CertId == certId && s.ExamId == command.ExamId)
                    .ToListAsync(cancellationToken);

                db.ScoreScales.RemoveRange(existingScales);

                foreach (var s in command.Scales)
                {
                    db.ScoreScales.Add(new ScoreScale
                    {
                        CertId = certId,
                        SkillId = s.SkillId,
                        ExamId = command.ExamId,
                        RawScore = s.RawScore,
                        ScaledScore = s.ScaledScore
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            // Update ScoreBands
            if (command.Bands != null && command.Bands.Count > 0)
            {
                var existingBands = await db.ScoreBands
                    .Where(b => b.CertId == certId)
                    .ToListAsync(cancellationToken);

                // Temporarily shift existing bands out of range to prevent trg_ScoreBands_NoOverlap false positives during in-place updates
                int tempShift = -20000;
                for (int i = 0; i < existingBands.Count; i++)
                {
                    existingBands[i].MinTotal = tempShift - (i * 20);
                    existingBands[i].MaxTotal = tempShift - (i * 20) + 1;
                }
                await db.SaveChangesAsync(cancellationToken);

                foreach (var b in command.Bands)
                {
                    var existing = existingBands.FirstOrDefault(x =>
                        (b.BandId.HasValue && x.BandId == b.BandId.Value) ||
                        string.Equals(x.Code, b.Code, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        existing.Code = b.Code;
                        existing.Name = b.Name;
                        existing.MinTotal = b.MinTotal;
                        existing.MaxTotal = b.MaxTotal;
                        existing.DisplayOrder = b.DisplayOrder;
                    }
                    else
                    {
                        db.ScoreBands.Add(new ScoreBand
                        {
                            CertId = certId,
                            Code = b.Code,
                            Name = b.Name,
                            MinTotal = b.MinTotal,
                            MaxTotal = b.MaxTotal,
                            DisplayOrder = b.DisplayOrder
                        });
                    }
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return await GetScoreConversionAsync(cert.Code, command.ExamId, cancellationToken);
    }
}
