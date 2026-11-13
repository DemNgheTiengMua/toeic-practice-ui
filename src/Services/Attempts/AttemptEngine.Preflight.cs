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
        var (requiresAudio, requiresMic) = await InspectPreflightRequirementsAsync(
            examId, paperQuestions: null, placementScope: null, cancellationToken);
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

        PlacementScope? scope = EnumCodes.TryParse<PlacementScope>(attempt.PlacementScope, out var parsed) ? parsed : null;
        var paperQuestions = await LoadSittingQuestionsAsync(attempt.ExamId, attempt.AttemptId, scope, cancellationToken);
        var (requiresAudio, requiresMic) = await InspectPreflightRequirementsAsync(
            attempt.ExamId, paperQuestions, scope, cancellationToken);
        var audioUrls = await GetExamAudioUrlsAsync(attempt.ExamId, paperQuestions, scope, cancellationToken);
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
        var attempt = await LoadSittingAttemptAsync(
            db.ExamAttempts.Include(a => a.Exam).ThenInclude(e => e.Cert), attemptId, userId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (attempt.PreflightVerifiedAt == null)
        {
            attempt.PreflightVerifiedAt = now;
            // The candidate timer starts upon passing the preflight onboarding gate!
            attempt.StartedAt = now;
            // #214 / PLC-12: a placement is not timed, so passing the gate does not arm a clock. Its
            // only deadline is the outer bound, and it gets no phase deadline at all - resetting
            // ExpiresAt to now + DurationMinutes here would have re-armed the countdown the create
            // path had just removed.
            if (PlacementSitting.HasCountdown(attempt.Exam.Purpose))
            {
                attempt.ExpiresAt = now.AddMinutes(attempt.Exam.DurationMinutes);

                // #250: the phase clock is a property of the paper, so it comes from the same seam the
                // projection reads. A pool-only paper has no Questions row for this skill, and the old
                // query returned null for it - which silently fell through to the whole-sitting deadline.
                var paperQuestions = await LoadSittingQuestionsAsync(attempt.ExamId, attempt.AttemptId, cancellationToken);
                var firstQuestion = paperQuestions.FirstOrDefault(q => q.Section.SkillId == attempt.ActiveSkillId);

                var firstSkill = firstQuestion?.Section.Skill;
                var firstSection = firstQuestion?.Section;
                var firstGroupResponseSeconds = firstQuestion?.Group.ResponseSeconds;

                if (firstSection != null && string.Equals(firstSection.NavigationMode, "HYBRID_GROUP_LOCKED", StringComparison.OrdinalIgnoreCase) && firstGroupResponseSeconds.HasValue)
                {
                    attempt.PhaseDeadlineAt = now.UtcDateTime.AddSeconds(firstGroupResponseSeconds.Value);
                }
                else if (firstSkill?.DurationMinutes is int skillDur && skillDur > 0)
                {
                    attempt.PhaseDeadlineAt = now.UtcDateTime.AddMinutes(skillDur);
                }
                else
                {
                    attempt.PhaseDeadlineAt = attempt.ExpiresAt.UtcDateTime;
                }
            }
            else
            {
                attempt.ExpiresAt = PlacementSitting.ExpiresAt(now);
                attempt.PhaseDeadlineAt = null;
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

        PlacementScope? scope = EnumCodes.TryParse<PlacementScope>(attempt.PlacementScope, out var parsed) ? parsed : null;
        var paperQuestions = await LoadSittingQuestionsAsync(attempt.ExamId, attempt.AttemptId, scope, cancellationToken);
        var (requiresAudio, requiresMic) = await InspectPreflightRequirementsAsync(
            attempt.ExamId, paperQuestions, scope, cancellationToken);
        if (requiresAudio || requiresMic)
        {
            throw new AttemptEngineException(
                AttemptEngineException.PreflightRequired,
                "Preflight equipment and audio buffering verification must be completed before entering the exam sitting.");
        }
    }

    private async Task<(bool RequiresAudio, bool RequiresMicrophone)> InspectPreflightRequirementsAsync(
        int examId,
        IReadOnlyList<SittingQuestion>? paperQuestions,
        PlacementScope? placementScope,
        CancellationToken cancellationToken)
    {
        // #250: the sections a paper uses come from the paper itself, not from a Questions query. For a
        // pool-only paper the old query returned nothing, and the fallback then asked every section of
        // the CERTIFICATE for a microphone - a TOEIC reading paper would have demanded one.
        paperQuestions ??= await LoadSittingQuestionsAsync(examId, attemptId: null, placementScope, cancellationToken);
        var questionSectionIds = paperQuestions
            .Select(q => q.SectionId)
            .Distinct()
            .ToList();

        var listening = Modality.Listening.ToCode();
        var reading = Modality.Reading.ToCode();

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

            var sectionsQuery = db.Sections
                .AsNoTracking()
                .Where(s => s.CertId == certId);
            if (placementScope == PlacementScope.ListeningAndReading)
            {
                sectionsQuery = sectionsQuery.Where(s => s.Modality == listening || s.Modality == reading);
            }

            sections = await sectionsQuery
                .Include(s => s.Skill)
                .ToListAsync(cancellationToken);
        }

        var hasSectionAudioFlag = sections.Any(s => s.RequireAudio);
        // #269 / #289: audio comes from the sitting's own scoped paper when one exists, so a
        // Listening & Reading sitting never checks unserved Speaking/Writing items for audio or mic.
        bool hasPooledAudio;
        if (paperQuestions.Count > 0)
        {
            hasPooledAudio = paperQuestions.Any(q => !string.IsNullOrWhiteSpace(q.Group.AudioPath));
        }
        else if (placementScope == PlacementScope.ListeningAndReading)
        {
            hasPooledAudio = await db.ExamItems
                .AsNoTracking()
                .Where(i => i.ExamId == examId)
                .Join(db.Sections.AsNoTracking(), i => i.SectionId, sec => sec.SectionId, (i, sec) => new { i, sec })
                .Where(x => x.sec.Modality == listening || x.sec.Modality == reading)
                .Join(db.Items.AsNoTracking(), x => x.i.ItemId, item => item.ItemId, (x, item) => item)
                .Join(db.Stimuli.AsNoTracking(), item => item.StimulusId, s => s.StimulusId, (item, s) => s)
                .AnyAsync(s => !string.IsNullOrWhiteSpace(s.AudioPath), cancellationToken);
        }
        else
        {
            hasPooledAudio = await db.ExamItems
                .AsNoTracking()
                .Where(i => i.ExamId == examId)
                .Join(db.Items.AsNoTracking(), i => i.ItemId, item => item.ItemId, (i, item) => item)
                .Join(db.Stimuli.AsNoTracking(), item => item.StimulusId, s => s.StimulusId, (item, s) => s)
                .AnyAsync(s => !string.IsNullOrWhiteSpace(s.AudioPath), cancellationToken);
        }

        bool hasSectionUploads;
        if (questionSectionIds.Count > 0)
        {
            hasSectionUploads = await db.ExamSectionAudios
                .AnyAsync(a => a.ExamId == examId && questionSectionIds.Contains(a.SectionId), cancellationToken);
        }
        else if (placementScope == PlacementScope.ListeningAndReading)
        {
            hasSectionUploads = await db.ExamSectionAudios
                .Join(db.Sections.AsNoTracking(), a => a.SectionId, sec => sec.SectionId, (a, sec) => new { a, sec })
                .AnyAsync(x => x.a.ExamId == examId && (x.sec.Modality == listening || x.sec.Modality == reading), cancellationToken);
        }
        else
        {
            hasSectionUploads = await db.ExamSectionAudios.AnyAsync(a => a.ExamId == examId, cancellationToken);
        }

        var requiresAudio = hasSectionAudioFlag || hasSectionUploads || hasPooledAudio;

        // #160 / #289: the section's modality is the rule, and ListeningAndReading never requires a mic.
        var requiresMicrophone = placementScope != PlacementScope.ListeningAndReading
            && sections.Any(s => string.Equals(s.Modality, Modality.Speaking.ToCode(), StringComparison.OrdinalIgnoreCase));

        return (requiresAudio, requiresMicrophone);
    }

    private async Task<IReadOnlyList<string>> GetExamAudioUrlsAsync(
        int examId,
        IReadOnlyList<SittingQuestion>? paperQuestions,
        PlacementScope? placementScope,
        CancellationToken cancellationToken)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sectionIds = paperQuestions?.Select(q => q.SectionId).Distinct().ToList() ?? [];
        var listening = Modality.Listening.ToCode();
        var reading = Modality.Reading.ToCode();

        List<string> sectionAudios;
        if (sectionIds.Count > 0)
        {
            sectionAudios = await db.ExamSectionAudios
                .AsNoTracking()
                .Where(a => a.ExamId == examId && sectionIds.Contains(a.SectionId) && !string.IsNullOrWhiteSpace(a.AudioPath))
                .Select(a => a.AudioPath)
                .ToListAsync(cancellationToken);
        }
        else if (placementScope == PlacementScope.ListeningAndReading)
        {
            sectionAudios = await db.ExamSectionAudios
                .AsNoTracking()
                .Join(db.Sections.AsNoTracking(), a => a.SectionId, sec => sec.SectionId, (a, sec) => new { a, sec })
                .Where(x => x.a.ExamId == examId && (x.sec.Modality == listening || x.sec.Modality == reading) && !string.IsNullOrWhiteSpace(x.a.AudioPath))
                .Select(x => x.a.AudioPath)
                .ToListAsync(cancellationToken);
        }
        else
        {
            sectionAudios = await db.ExamSectionAudios
                .AsNoTracking()
                .Where(a => a.ExamId == examId && !string.IsNullOrWhiteSpace(a.AudioPath))
                .Select(a => a.AudioPath)
                .ToListAsync(cancellationToken);
        }

        foreach (var path in sectionAudios)
        {
            var normalized = MediaUrlHelper.NormalizeMediaUrl(path);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                urls.Add(normalized);
            }
        }

        IEnumerable<string> pooledAudios;
        if (paperQuestions is { Count: > 0 })
        {
            pooledAudios = paperQuestions
                .Select(q => q.Group.AudioPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct();
        }
        else if (placementScope == PlacementScope.ListeningAndReading)
        {
            pooledAudios = await db.ExamItems
                .AsNoTracking()
                .Where(i => i.ExamId == examId)
                .Join(db.Sections.AsNoTracking(), i => i.SectionId, sec => sec.SectionId, (i, sec) => new { i, sec })
                .Where(x => x.sec.Modality == listening || x.sec.Modality == reading)
                .Join(db.Items.AsNoTracking(), x => x.i.ItemId, item => item.ItemId, (x, item) => item)
                .Join(db.Stimuli.AsNoTracking(), item => item.StimulusId, s => s.StimulusId, (item, s) => s)
                .Where(s => !string.IsNullOrWhiteSpace(s.AudioPath))
                .Select(s => s.AudioPath!)
                .Distinct()
                .ToListAsync(cancellationToken);
        }
        else
        {
            pooledAudios = await db.ExamItems
                .AsNoTracking()
                .Where(i => i.ExamId == examId)
                .Join(db.Items.AsNoTracking(), i => i.ItemId, item => item.ItemId, (i, item) => item)
                .Join(db.Stimuli.AsNoTracking(), item => item.StimulusId, s => s.StimulusId, (item, s) => s)
                .Where(s => !string.IsNullOrWhiteSpace(s.AudioPath))
                .Select(s => s.AudioPath!)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        foreach (var path in pooledAudios)
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
