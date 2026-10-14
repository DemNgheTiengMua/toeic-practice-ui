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

                if (orderedBands[i].DisplayOrder >= orderedBands[i + 1].DisplayOrder)
                {
                    throw new ScoreConversionException(
                        ScoreConversionException.InvalidData,
                        $"Score band '{orderedBands[i + 1].Name}' with higher score range must have a greater DisplayOrder ({orderedBands[i + 1].DisplayOrder}) than '{orderedBands[i].Name}' ({orderedBands[i].DisplayOrder}).");
                }
            }
        }

        // 3. Persist Changes
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Update ScoreScales only when non-empty scales list is provided
            if (command.Scales != null && command.Scales.Count > 0)
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

            // Update ScoreBands atomically without negative temp-shifts
            if (command.Bands != null && command.Bands.Count > 0)
            {
                var existingBands = await db.ScoreBands
                    .Where(b => b.CertId == certId)
                    .ToListAsync(cancellationToken);

                var updates = new List<(int BandId, string Code, string Name, int MinTotal, int MaxTotal, int DisplayOrder)>();
                var inserts = new List<ScoreBand>();

                foreach (var b in command.Bands)
                {
                    var existing = existingBands.FirstOrDefault(x =>
                        (b.BandId.HasValue && x.BandId == b.BandId.Value) ||
                        string.Equals(x.Code, b.Code, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        updates.Add((existing.BandId, b.Code, b.Name, b.MinTotal, b.MaxTotal, b.DisplayOrder));
                    }
                    else
                    {
                        inserts.Add(new ScoreBand
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

                var updatedBandIds = updates.Select(u => u.BandId).ToHashSet();
                var toDelete = existingBands.Where(b => !updatedBandIds.Contains(b.BandId)).ToList();
                if (toDelete.Count > 0)
                {
                    db.ScoreBands.RemoveRange(toDelete);
                    await db.SaveChangesAsync(cancellationToken);
                }

                // If updating existing bands, execute a single multi-row UPDATE join so trg_ScoreBands_NoOverlap
                // sees all final non-overlapping ranges at once without trigger violations or negative values.
                if (updates.Count > 0)
                {
                    var parameters = new List<object>();
                    var selectClauses = new List<string>();
                    for (int i = 0; i < updates.Count; i++)
                    {
                        var u = updates[i];
                        int p0 = i * 6;
                        selectClauses.Add($"SELECT {{{p0}}} AS BandId, {{{p0 + 1}}} AS Code, {{{p0 + 2}}} AS Name, {{{p0 + 3}}} AS MinTotal, {{{p0 + 4}}} AS MaxTotal, {{{p0 + 5}}} AS DisplayOrder");
                        parameters.Add(u.BandId);
                        parameters.Add(u.Code);
                        parameters.Add(u.Name);
                        parameters.Add(u.MinTotal);
                        parameters.Add(u.MaxTotal);
                        parameters.Add(u.DisplayOrder);
                    }

                    var sql = $"""
                        UPDATE b
                        SET b.Code = v.Code,
                            b.Name = v.Name,
                            b.MinTotal = v.MinTotal,
                            b.MaxTotal = v.MaxTotal,
                            b.DisplayOrder = v.DisplayOrder
                        FROM ScoreBands b
                        JOIN ({string.Join(" UNION ALL ", selectClauses)}) v
                          ON b.BandId = v.BandId
                        """;

                    await db.Database.ExecuteSqlRawAsync(sql, parameters.ToArray(), cancellationToken);
                }

                if (inserts.Count > 0)
                {
                    db.ScoreBands.AddRange(inserts);
                    await db.SaveChangesAsync(cancellationToken);
                }
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
