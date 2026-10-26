using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    public async Task<bool> RequiresPreflightAsync(int examId, CancellationToken cancellationToken = default)
    {
        var (requiresAudio, requiresMic) = await InspectPreflightRequirementsAsync(examId, cancellationToken);
        return requiresAudio || requiresMic;
    }

    public async Task<PreflightStatusView> GetPreflightStatusAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam).ThenInclude(e => e.Cert)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        var (requiresAudio, requiresMic) = await InspectPreflightRequirementsAsync(attempt.ExamId, cancellationToken);
        var audioUrls = await GetExamAudioUrlsAsync(attempt.ExamId, cancellationToken);
        var requiresPreflight = requiresAudio || requiresMic;

        return new PreflightStatusView(
            AttemptId: attempt.AttemptId,
            ExamId: attempt.ExamId,
            ExamName: attempt.Exam.Name,
            CertCode: attempt.Exam.Cert.Code,
            CertName: attempt.Exam.Cert.Name,
            DurationMinutes: attempt.Exam.DurationMinutes,
            RequiresPreflight: requiresPreflight,
            RequiresAudio: requiresAudio,
            RequiresMicrophone: requiresMic,
            IsVerified: !requiresPreflight || attempt.PreflightVerifiedAt.HasValue,
            VerifiedAt: attempt.PreflightVerifiedAt,
            AudioUrls: audioUrls);
    }

    public async Task<PreflightStatusView> VerifyPreflightAsync(
        int attemptId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam).ThenInclude(e => e.Cert)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        var now = DateTimeOffset.UtcNow;
        if (attempt.PreflightVerifiedAt == null)
        {
            attempt.PreflightVerifiedAt = now;
            // The candidate timer starts upon passing the preflight onboarding gate!
            attempt.StartedAt = now;
            attempt.ExpiresAt = now.AddMinutes(attempt.Exam.DurationMinutes);

            var firstSkill = await db.Questions
                .Where(q => q.ExamId == attempt.ExamId && q.Section.SkillId == attempt.ActiveSkillId)
                .Select(q => q.Section.Skill)
                .FirstOrDefaultAsync(cancellationToken);
            var firstSection = await db.Questions
                .Where(q => q.ExamId == attempt.ExamId && q.Section.SkillId == attempt.ActiveSkillId)
                .Select(q => q.Section)
                .FirstOrDefaultAsync(cancellationToken);
            var firstGroup = attempt.ActiveGroupId.HasValue
                ? await db.QuestionGroups.SingleOrDefaultAsync(g => g.GroupId == attempt.ActiveGroupId.Value, cancellationToken)
                : null;

            if (firstSection != null && string.Equals(firstSection.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase) && firstGroup?.ResponseSeconds.HasValue == true)
            {
                attempt.PhaseDeadlineAt = now.UtcDateTime.AddSeconds(firstGroup.ResponseSeconds.Value);
            }
            else if (firstSkill?.DurationMinutes is int skillDur && skillDur > 0)
            {
                attempt.PhaseDeadlineAt = now.UtcDateTime.AddMinutes(skillDur);
            }
            else
            {
                attempt.PhaseDeadlineAt = attempt.ExpiresAt.UtcDateTime;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetPreflightStatusAsync(attemptId, userId, cancellationToken);
    }

    private async Task EnsurePreflightVerifiedAsync(ExamAttempt attempt, CancellationToken cancellationToken)
    {
        if (attempt.PreflightVerifiedAt != null)
        {
            return;
        }

        var (requiresAudio, requiresMic) = await InspectPreflightRequirementsAsync(attempt.ExamId, cancellationToken);
        if (requiresAudio || requiresMic)
        {
            throw new AttemptEngineException(
                AttemptEngineException.PreflightRequired,
                "Preflight equipment and audio buffering verification must be completed before entering the exam sitting.");
        }
    }

    private async Task<(bool RequiresAudio, bool RequiresMicrophone)> InspectPreflightRequirementsAsync(int examId, CancellationToken cancellationToken)
    {
        var questionSectionIds = await db.Questions
            .AsNoTracking()
            .Where(q => q.ExamId == examId)
            .Select(q => q.SectionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        List<Section> sections;
        if (questionSectionIds.Count > 0)
        {
            sections = await db.Sections
                .AsNoTracking()
                .Where(s => questionSectionIds.Contains(s.SectionId))
                .Include(s => s.Skill)
                .ToListAsync(cancellationToken);
        }
        else
        {
            var certId = await db.Exams
                .AsNoTracking()
                .Where(e => e.ExamId == examId)
                .Select(e => e.CertId)
                .SingleOrDefaultAsync(cancellationToken);

            sections = await db.Sections
                .AsNoTracking()
                .Where(s => s.CertId == certId)
                .Include(s => s.Skill)
                .ToListAsync(cancellationToken);
        }

        var hasSectionAudioFlag = sections.Any(s => s.RequireAudio);
        var hasUploadedAudio = await db.ExamSectionAudios.AnyAsync(a => a.ExamId == examId, cancellationToken)
            || await db.QuestionGroups.AnyAsync(g => g.ExamId == examId && !string.IsNullOrWhiteSpace(g.AudioPath), cancellationToken);
        var requiresAudio = hasSectionAudioFlag || hasUploadedAudio;

        var requiresMicrophone = sections.Any(s =>
            string.Equals(s.Modality, "speaking", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Skill?.Modality, "speaking", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Skill?.Code, "speaking", StringComparison.OrdinalIgnoreCase));

        return (requiresAudio, requiresMicrophone);
    }

    private async Task<IReadOnlyList<string>> GetExamAudioUrlsAsync(int examId, CancellationToken cancellationToken)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sectionAudios = await db.ExamSectionAudios
            .AsNoTracking()
            .Where(a => a.ExamId == examId && !string.IsNullOrWhiteSpace(a.AudioPath))
            .Select(a => a.AudioPath)
            .ToListAsync(cancellationToken);

        foreach (var path in sectionAudios)
        {
            var normalized = MediaUrlHelper.NormalizeMediaUrl(path);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                urls.Add(normalized);
            }
        }

        var groupAudios = await db.QuestionGroups
            .AsNoTracking()
            .Where(g => g.ExamId == examId && !string.IsNullOrWhiteSpace(g.AudioPath))
            .Select(g => g.AudioPath!)
            .ToListAsync(cancellationToken);

        foreach (var path in groupAudios)
        {
            var normalized = MediaUrlHelper.NormalizeMediaUrl(path);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                urls.Add(normalized);
            }
        }

        return urls.OrderBy(u => u).ToList();
    }
}
