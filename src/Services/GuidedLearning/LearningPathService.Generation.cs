using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public sealed partial class LearningPathService
{

    public async Task<LearningPathSummaryView> GeneratePathAsync(int userId, string? targetBandCode = null, CancellationToken cancellationToken = default, bool archiveExisting = false)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        var existingActive = await db.LearnerPaths
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active", cancellationToken);
        if (existingActive != null)
        {
            if (archiveExisting)
            {
                existingActive.Status = "reset";
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new LearningPathException(LearningPathException.PathAlreadyActive, "An active learning path already exists for this certificate.");
            }
        }

        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == activeCertId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.TakenAt)
            .FirstOrDefaultAsync(cancellationToken);

        string? startingBand = latestPlacement?.ResultBandCode;
        if (string.IsNullOrEmpty(startingBand))
        {
            var latestMock = await db.ExamAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.CertId == activeCertId && a.Status == AttemptStatus.Graded.ToCode() && a.Exam.Purpose == ExamPurpose.Mock.ToCode() && a.BandCode != null)
                .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
            startingBand = latestMock?.BandCode;
        }

        var scoreBands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == activeCertId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        if (string.IsNullOrEmpty(startingBand))
        {
            startingBand = scoreBands.FirstOrDefault()?.Code ?? "A1";
        }

        string toBand = targetBandCode ?? "";
        if (string.IsNullOrEmpty(toBand))
        {
            var savedTarget = await db.TargetBands
                .AsNoTracking()
                .FirstOrDefaultAsync(tb => tb.UserId == userId && tb.CertId == activeCertId, cancellationToken);
            toBand = savedTarget?.TargetBandCode ?? "";
        }

        if (string.IsNullOrEmpty(toBand))
        {
            toBand = scoreBands.LastOrDefault()?.Code ?? "B2";
        }

        // Match template: exact (from, to) -> closest from -> any for cert
        var template = await db.PathTemplates
            .Where(t => t.CertId == activeCertId && t.FromBandCode == startingBand && t.ToBandCode == toBand)
            .FirstOrDefaultAsync(cancellationToken);

        template ??= await db.PathTemplates
            .Where(t => t.CertId == activeCertId && t.FromBandCode == startingBand)
            .FirstOrDefaultAsync(cancellationToken);

        template ??= await db.PathTemplates
            .Where(t => t.CertId == activeCertId)
            .OrderBy(t => t.TemplateId)
            .FirstOrDefaultAsync(cancellationToken);

        if (template == null)
        {
            throw new LearningPathException(LearningPathException.TemplateNotFound, "No learning path template found for this certificate.");
        }

        var learnerPath = new LearnerPath
        {
            UserId = userId,
            CertId = activeCertId,
            TemplateId = template.TemplateId,
            CurrentBandCode = startingBand,
            PlacementId = latestPlacement?.PlacementId,
            Status = "active",
            StartedAt = DateTimeOffset.UtcNow
        };

        try
        {
            db.LearnerPaths.Add(learnerPath);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Entry(learnerPath).State = EntityState.Detached;
            var concurrent = await db.LearnerPaths
                .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active", cancellationToken);
            if (concurrent is not null)
            {
                throw new LearningPathException(LearningPathException.PathAlreadyActive, "An active learning path already exists for this certificate.");
            }
            throw;
        }

        var modules = await db.PathModules
            .Include(m => m.PathModuleItemPathModules)
            .Where(m => m.TemplateId == template.TemplateId)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(cancellationToken);

        var certSectionIds = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == activeCertId)
            .Select(s => s.SectionId)
            .ToListAsync(cancellationToken);

        var weakSectionRows = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(w => w.UserId == userId && certSectionIds.Contains(w.SectionId))
            .ToListAsync(cancellationToken);

        var weakSections = weakSectionRows
            .OrderBy(w => (double)(w.Correct ?? 0) / Math.Max(1, w.Answered ?? 1))
            .Select(w => w.SectionId)
            .ToList();

        if (weakSections.Count > 0)
        {
            var moduleItemSectionIds = await db.PathModuleItems
                .AsNoTracking()
                .Where(pmi => pmi.TemplateId == template.TemplateId && pmi.RefSectionId.HasValue)
                .Select(pmi => new { pmi.ItemId, SectionId = pmi.RefSectionId!.Value })
                .ToListAsync(cancellationToken);

            var itemSectionMap = moduleItemSectionIds.ToDictionary(x => x.ItemId, x => x.SectionId);

            modules = modules
                .OrderBy(m =>
                {
                    var itemIds = m.PathModuleItemPathModules.Select(i => i.ItemId).ToList();
                    var minRank = 999;
                    foreach (var id in itemIds)
                    {
                        if (itemSectionMap.TryGetValue(id, out var secId))
                        {
                            var rank = weakSections.IndexOf(secId);
                            if (rank >= 0 && rank < minRank) minRank = rank;
                        }
                    }
                    return minRank;
                })
                .ThenBy(m => m.DisplayOrder)
                .ToList();
        }

        var allItems = modules
            .SelectMany(m => m.PathModuleItemPathModules.OrderBy(i => i.DisplayOrder))
            .ToList();

        bool isFirst = true;
        foreach (var item in allItems)
        {
            db.LearnerPathSteps.Add(new LearnerPathStep
            {
                LearnerPathId = learnerPath.LearnerPathId,
                PathModuleItemId = item.ItemId,
                TemplateId = template.TemplateId,
                Status = isFirst ? "available" : "locked",
                Score = null,
                StartedAt = null,
                CompletedAt = null
            });
            isFirst = false;
        }

        await db.SaveChangesAsync(cancellationToken);

        return await GetActivePathAsync(userId, cancellationToken);
    }
}
