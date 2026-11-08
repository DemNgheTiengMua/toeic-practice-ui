using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AdminExamService
{
    /// <summary>
    /// #227 / D20: a structural repair forks a new edition rather than mutating the live one.
    /// <para>
    /// <b>Why the whole exam is copied and not just its bindings.</b> An edition is the paper, and the
    /// paper is more than <c>ExamItems</c>: it is the exam row's duration, its groups' passages and
    /// audio, its questions' stems and keys. A fork that copied only the bindings would leave a new
    /// paper with no questions to bind onto, because a position is a rank over the <c>Questions</c>
    /// rows (<c>AttemptEngine.PlaceBindings</c>). Copying the groups and questions is therefore what
    /// makes the fork a paper at all.
    /// </para>
    /// <para>
    /// <b>What this deliberately does not copy.</b> Sections and skills are certificate-wide
    /// configuration, not paper structure: a fork points at the same <c>SectionId</c>s, because
    /// duplicating them would make two answers to "how many sections does this certificate have".
    /// The frozen <see cref="ExamEdition"/> row is not copied either - it is written by the publish
    /// trigger when the new edition goes live, and only when it goes live, so a draft fork carries no
    /// edition yet.
    /// </para>
    /// <para>
    /// <b>The new edition is a draft.</b> Forking is not publishing: the repair is made, the six
    /// invariants are cleared, and the fork goes live through the one publish door like any other
    /// paper. The live edition keeps its status until then, so learners in the catalog are unaffected
    /// by the mere existence of a fork.
    /// </para>
    /// </summary>
    public async Task<AdminForkEditionResultDto> ForkEditionAsync(
        int examId,
        ForkExamEditionRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = await db.Exams
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken)
            ?? throw new ExamNotFoundException(examId);

        // Only a locked paper needs forking: a draft can be repaired in place, and forking it would
        // leave two drafts with one name.
        var lockedByFinishedAttempts = await db.ExamAttempts
            .AnyAsync(a => a.ExamId == examId && (a.Status == "submitted" || a.Status == "graded"), cancellationToken);
        if (!lockedByFinishedAttempts)
        {
            throw new AdminExamException(
                ApiError.Codes.Validation,
                "This exam has no finished attempts, so its structure is still editable. Repair it in place; an edition is only forked when history would otherwise be rewritten.",
                400);
        }

        var name = request.Name is null ? null : Normalize(request.Name);
        if (name is null)
        {
            // A fork without a name would collide with the source on UX_Exams_Cert_Name, which is
            // unique per (certificate, name). Naming the pattern here rather than letting the index
            // refuse it makes the refusal a sentence.
            throw new AdminExamException(
                ApiError.Codes.Validation,
                "A forked edition needs its own name: the paper's name is unique per certificate, so the edition cannot extend the parent's.",
                400);
        }

        var nameTaken = await db.Exams.AnyAsync(
            e => e.CertId == source.CertId && e.Name == name && !e.IsDeleted, cancellationToken);
        if (nameTaken)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "An exam with this name already exists for this certificate.");
        }

        // The highest edition number in this paper's chain, so a third fork does not collide.
        var chain = await EditionChainAsync(source, cancellationToken);
        var editionNumber = chain.Max(e => e.EditionNumber) + 1;

        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var fork = new Exam
        {
            CertId = source.CertId,
            Name = name,
            // Draft: the repair happens here, and publishing is still one door.
            Status = ExamStatus.Draft.ToCode(),
            DurationMinutes = source.DurationMinutes,
            Purpose = source.Purpose,
            DeliveryMode = source.DeliveryMode,
            ExamFamily = source.ExamFamily,
            Description = source.Description,
            SourceName = source.SourceName,
            InternalNote = request.InternalNote is null ? null : Normalize(request.InternalNote),
            QuestionCount = source.QuestionCount,
            ContentTier = source.ContentTier,
            EditionNumber = editionNumber,
            SupersedesExamId = source.ExamId,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = null,
        };

        db.Exams.Add(fork);
        await db.SaveChangesAsync(cancellationToken);

        // Groups first, then questions, then bindings: each step needs the ids the previous one
        // generated, because every one of the three carries its exam id down by composite key.
        var sourceGroups = await db.QuestionGroups
            .AsNoTracking()
            .Where(g => g.ExamId == source.ExamId)
            .OrderBy(g => g.DisplayOrder)
            .ThenBy(g => g.GroupId)
            .ToListAsync(cancellationToken);

        var groupIdMap = new Dictionary<int, int>(sourceGroups.Count);
        foreach (var group in sourceGroups)
        {
            var copy = new QuestionGroup
            {
                ExamId = fork.ExamId,
                CertId = group.CertId,
                SectionId = group.SectionId,
                UnitNumber = group.UnitNumber,
                Passage = group.Passage,
                AudioPath = group.AudioPath,
                AudioStartMs = group.AudioStartMs,
                AudioEndMs = group.AudioEndMs,
                ImagePath = group.ImagePath,
                ImageAlt = group.ImageAlt,
                Transcript = group.Transcript,
                PrepSeconds = group.PrepSeconds,
                ResponseSeconds = group.ResponseSeconds,
                DisplayOrder = group.DisplayOrder,
            };
            db.QuestionGroups.Add(copy);
            await db.SaveChangesAsync(cancellationToken);
            groupIdMap[group.GroupId] = copy.GroupId;
        }

        var sourceQuestions = await db.Questions
            .AsNoTracking()
            .Where(q => q.ExamId == source.ExamId)
            .Include(q => q.QuestionOptionQuestionNavigations)
            .OrderBy(q => q.Section.Skill.DisplayOrder)
            .ThenBy(q => q.Section.DisplayOrder)
            .ThenBy(q => q.Group.DisplayOrder)
            .ThenBy(q => q.DisplayOrder)
            .ThenBy(q => q.QuestionId)
            .ToListAsync(cancellationToken);

        var questionIdMap = new Dictionary<int, int>(sourceQuestions.Count);
        foreach (var question in sourceQuestions)
        {
            var copy = new Question
            {
                GroupId = groupIdMap[question.GroupId],
                ExamId = fork.ExamId,
                SectionId = question.SectionId,
                SkillId = question.SkillId,
                Stem = question.Stem,
                CefrLevel = question.CefrLevel,
                QuestionType = question.QuestionType,
                Explanation = question.Explanation,
                CategoryId = question.CategoryId,
                PrepSeconds = question.PrepSeconds,
                ResponseSeconds = question.ResponseSeconds,
                MinWords = question.MinWords,
                MaxWords = question.MaxWords,
                MaxSelections = question.MaxSelections,
                AllowNumber = question.AllowNumber,
                AcceptedAnswersJson = question.AcceptedAnswersJson,
                RequiredKeywords = question.RequiredKeywords,
                Hint = question.Hint,
                SampleAnswerOrImageDesc = question.SampleAnswerOrImageDesc,
                ReferenceKind = question.ReferenceKind,
                DisplayOrder = question.DisplayOrder,
                UpdatedAt = null,
            };
            db.Questions.Add(copy);
            await db.SaveChangesAsync(cancellationToken);
            questionIdMap[question.QuestionId] = copy.QuestionId;

            // The option set is what the answer key is: a fork that copied a question without its
            // options would be a question with no right answer.
            foreach (var option in question.QuestionOptionQuestionNavigations.OrderBy(o => o.Label))
            {
                db.QuestionOptions.Add(new QuestionOption
                {
                    QuestionId = copy.QuestionId,
                    QuestionType = option.QuestionType,
                    Label = option.Label,
                    Text = option.Text,
                    IsCorrect = option.IsCorrect,
                    Rationale = option.Rationale,
                });
            }
        }

        // The bindings. A binding is placed by (section, 1-based position in that section), and the
        // position is the rank over the QUESTIONS of that section - not the binding's own
        // DisplayOrder, which the source paper may have set independently of the question order.
        // Recomputing it from the copied questions is what keeps the fork's positions meaning the
        // same thing the source's did, since the copy preserves the question order exactly.
        var sourceBindings = await db.ExamItems
            .AsNoTracking()
            .Where(b => b.ExamId == source.ExamId)
            .OrderBy(b => b.SectionId)
            .ThenBy(b => b.DisplayOrder)
            .ToListAsync(cancellationToken);

        foreach (var binding in sourceBindings)
        {
            db.ExamItems.Add(new ExamItem
            {
                ExamId = fork.ExamId,
                SectionId = binding.SectionId,
                CertId = binding.CertId,
                ItemVersionId = binding.ItemVersionId,
                ItemId = binding.ItemId,
                Modality = binding.Modality,
                DisplayOrder = binding.DisplayOrder,
                UnitNumber = binding.UnitNumber,
            });
        }

        // The per-section audio a listening paper cannot sit without.
        var sourceAudio = await db.ExamSectionAudios
            .AsNoTracking()
            .Where(a => a.ExamId == source.ExamId)
            .ToListAsync(cancellationToken);
        foreach (var audio in sourceAudio)
        {
            db.ExamSectionAudios.Add(new ExamSectionAudio
            {
                ExamId = fork.ExamId,
                SectionId = audio.SectionId,
                CertId = audio.CertId,
                AudioPath = audio.AudioPath,
                DurationSec = audio.DurationSec,
                SizeBytes = audio.SizeBytes,
            });
        }

        // The per-exam score scale, when the source has one. Without it the fork scores on the
        // certificate default, which is a different conversion from the one its learners sit.
        var sourceScales = await db.ScoreScales
            .AsNoTracking()
            .Where(s => s.ExamId == source.ExamId)
            .ToListAsync(cancellationToken);
        foreach (var scale in sourceScales)
        {
            db.ScoreScales.Add(new ScoreScale
            {
                CertId = scale.CertId,
                SkillId = scale.SkillId,
                ExamId = fork.ExamId,
                RawScore = scale.RawScore,
                ScaledScore = scale.ScaledScore,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AdminForkEditionResultDto(
            fork.ExamId,
            fork.Name,
            fork.EditionNumber,
            source.ExamId,
            source.EditionNumber,
            questionIdMap.Count,
            sourceBindings.Count);
    }

    /// <summary>
    /// Every edition of this paper, oldest first, following <c>SupersedesExamId</c> both ways: a
    /// paper forked from a paper forked from this one belongs to the same chain, and the edition
    /// number has to be unique along the whole of it or two forks collide.
    /// </summary>
    private async Task<List<Exam>> EditionChainAsync(Exam exam, CancellationToken cancellationToken)
    {
        var visited = new HashSet<int>();
        var pending = new Queue<int>();
        pending.Enqueue(exam.ExamId);

        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!visited.Add(id))
            {
                continue;
            }

            var predecessors = await db.Exams.AsNoTracking()
                .Where(e => e.ExamId == id && e.SupersedesExamId != null)
                .Select(e => e.SupersedesExamId!.Value)
                .ToListAsync(cancellationToken);
            var successors = await db.Exams.AsNoTracking()
                .Where(e => e.SupersedesExamId == id)
                .Select(e => e.ExamId)
                .ToListAsync(cancellationToken);

            foreach (var next in predecessors.Concat(successors))
            {
                pending.Enqueue(next);
            }
        }

        return await db.Exams.AsNoTracking()
            .Where(e => visited.Contains(e.ExamId))
            .OrderBy(e => e.EditionNumber)
            .ThenBy(e => e.ExamId)
            .ToListAsync(cancellationToken);
    }
}
