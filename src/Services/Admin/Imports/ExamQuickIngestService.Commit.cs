using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class ExamQuickIngestService
{

    /// <summary>
    /// #189: writes the pasted questions, then the pool stock they become.
    /// <para>
    /// The legacy rows come first because they are what a paper renders, and because the
    /// pool binding is placed by the position those rows give a question. Each paste group - a
    /// passage plus its children - becomes <em>one</em> stimulus row shared by all of its children,
    /// which is the difference between a shared passage and a copy per question.
    /// </para>
    /// <para>
    /// A question the pool cannot hold (three options, a multi-select, a typed answer) is still
    /// written in the legacy shape and named in the message. This is the expand half: a commit must
    /// not fail because the pool is narrower than the old formats.
    /// </para>
    /// </summary>
    public async Task<QuickIngestCommitResultDto> CommitStagedAsync(
        int examId,
        QuickIngestCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Commit request cannot be null.", 400);
        }

        if (db == null)
        {
            throw new InvalidOperationException("CompanionDbContext is required to commit staged questions.");
        }

        var exam = await db.Exams.Include(e => e.Cert).FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken)
            ?? throw new ExamNotFoundException(examId);

        // #157: this service inserts groups and questions itself, so the admin service's lock never
        // saw it. Both modes change what a finished attempt was scored against: append adds rows,
        // replace deletes them.
        await ExamStructureLock.RequireEditableAsync(db, examId, cancellationToken);

        var section = await db.Sections.FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken)
            ?? throw new AdminExamException("SectionNotFound", $"Section with id {request.SectionId} not found for certificate '{exam.Cert.Name}'.", 404);

        if (request.Questions == null || request.Questions.Count == 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "No questions provided to commit.", 400);
        }

        // Pre-validate questions
        for (var i = 0; i < request.Questions.Count; i++)
        {
            var q = request.Questions[i];
            if (string.IsNullOrWhiteSpace(q.Stem))
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} has an empty stem.", 400);
            }

            var blockingIssue = q.Issues?.FirstOrDefault(iss => iss.Severity == "error");
            if (blockingIssue != null)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} has validation error: {blockingIssue.Message}", 400);
            }

            var qType = q.QuestionType?.ToLowerInvariant() ?? "mcq";

            // #229 / D1: a per-question level on a paste is a claim the author made, so a typo in it is
            // refused here rather than silently replaced by the commit's own. Falling back on an
            // unparseable value is the same class of defect as defaulting a missing one.
            if (!string.IsNullOrWhiteSpace(q.CefrLevel)
                && !CefrLevel.TryParseAuthored(q.CefrLevel.Trim(), out _))
            {
                throw new AdminExamException(
                    ApiError.Codes.Validation,
                    $"Question {i + 1} states cefrLevel '{q.CefrLevel}', which is not an authored level. Content is authored at A1-C2; the pre-A1 floor is reportable only (D1).",
                    400);
            }

            if (qType == "free_response")
            {
                // Free response: options not required
            }
            else if (qType == "inline_completion")
            {
                if (string.IsNullOrWhiteSpace(q.AcceptedAnswersJson))
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} is inline_completion but has no accepted answers.", 400);
                }
            }
            else if (qType == "multi_select")
            {
                if (q.Options == null || q.Options.Count < 2)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have at least 2 options.", 400);
                }
                if (!q.Options.Any(o => o.IsCorrect))
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have at least one correct option.", 400);
                }
            }
            else // mcq / mcq_3
            {
                if (q.Options == null || q.Options.Count < 2)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have at least 2 options.", 400);
                }
                var correctCount = q.Options.Count(o => o.IsCorrect);
                if (correctCount != 1)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have exactly one correct option (found {correctCount}).", 400);
                }
            }
        }

        var topics = await TopicCatalog.LoadAsync(db, cancellationToken);

        // #229 / D1: the commit's level is required, and it is parsed once before the transaction so a
        // bad value is a refusal rather than a row written with a NULL level. This is the door the
        // ticket names: the old code wrote `q.DifficultyLevel is >= 1 and <= 5 ? ... : 3`, which is a
        // silent default in both directions - a missing number became 3, and nothing levelled the row.
        var commitLevel = CefrLevel.TryParseAuthored(request.CefrLevel?.Trim(), out var parsedCommitLevel)
            ? parsedCommitLevel
            : throw new AdminExamException(
                ApiError.Codes.Validation,
                $"cefrLevel '{request.CefrLevel}' is not an authored CEFR level. A commit authors content, so it states a level: A1, A2, B1, B2, C1, C2. The pre-A1 floor is reportable only, and there is no difficulty-to-level mapping (D1).",
                400);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var isReplace = string.Equals(request.Mode, "replace", StringComparison.OrdinalIgnoreCase);
        int currentDisplayOrder = 1;

        if (isReplace)
        {
            var existingGroups = await db.QuestionGroups
                .Include(g => g.QuestionGroups)
                    .ThenInclude(q => q.QuestionOptionQuestionNavigations)
                .Where(g => g.ExamId == examId && g.SectionId == request.SectionId)
                .ToListAsync(cancellationToken);

            var existingQuestionIds = existingGroups.SelectMany(g => g.QuestionGroups.Select(q => q.QuestionId)).ToList();

            var hasAnswers = await db.AttemptAnswers.AnyAsync(a => existingQuestionIds.Contains(a.QuestionId), cancellationToken)
                || await db.FreeResponses.AnyAsync(r => existingQuestionIds.Contains(r.QuestionId), cancellationToken);

            if (hasAnswers)
            {
                throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot replace section content because completed or active learner attempts exist.", 400);
            }

            // #189: a re-paste of a section replaces what the paper shows, so its bindings go with the
            // rows they were placed on. Leaving them would bind a version to a position that no longer
            // exists, which is a paper with a hole in it.
            var existingBindings = await db.ExamItems
                .Where(b => b.ExamId == examId && b.SectionId == request.SectionId)
                .ToListAsync(cancellationToken);
            if (existingBindings.Count > 0)
            {
                db.ExamItems.RemoveRange(existingBindings);
            }

            foreach (var g in existingGroups)
            {
                foreach (var q in g.QuestionGroups)
                {
                    var saved = await db.SavedQuestions.Where(s => s.QuestionId == q.QuestionId).ToListAsync(cancellationToken);
                    if (saved.Count > 0) db.SavedQuestions.RemoveRange(saved);

                    var pa = await db.PracticeAnswers.Where(p => p.QuestionId == q.QuestionId).ToListAsync(cancellationToken);
                    if (pa.Count > 0) db.PracticeAnswers.RemoveRange(pa);

                    db.QuestionOptions.RemoveRange(q.QuestionOptionQuestionNavigations);
                    db.Questions.Remove(q);
                }
                db.QuestionGroups.Remove(g);
            }

            await db.SaveChangesAsync(cancellationToken);
            currentDisplayOrder = 1;
        }
        else
        {
            var maxGroupOrder = await db.QuestionGroups
                .Where(g => g.ExamId == examId && g.SectionId == request.SectionId)
                .Select(g => (int?)g.DisplayOrder)
                .MaxAsync(cancellationToken) ?? 0;

            var maxQuestionOrder = await db.Questions
                .Where(q => q.ExamId == examId && q.SectionId == request.SectionId)
                .Select(q => (int?)q.DisplayOrder)
                .MaxAsync(cancellationToken) ?? 0;

            currentDisplayOrder = Math.Max(maxGroupOrder, maxQuestionOrder) + 1;
        }

        var categories = await db.QuestionCategories
            .Where(c => c.CertId == exam.CertId)
            .ToDictionaryAsync(c => c.Code, c => c.CategoryId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var groupsCreated = 0;
        var questionsCreated = 0;

        var poolNotes = new List<string>();
        var groupStimuli = new Dictionary<int, PoolImport.StimulusInput>();
        var writtenQuestions = new List<(int QuestionId, int GroupId, PoolContent Content, string Stem, QuickIngestParsedQuestionDto Source)>();

        QuestionGroup? activeGroup = null;
        string? activePassage = null;
        int? activeGroupNum = null;

        foreach (var q in request.Questions)
        {
            var needsNewGroup = activeGroup == null ||
                (!string.IsNullOrWhiteSpace(q.Passage) && !string.Equals(activePassage, q.Passage, StringComparison.Ordinal)) ||
                (q.GroupNumber.HasValue && q.GroupNumber != activeGroupNum);

            if (needsNewGroup)
            {
                activeGroup = new QuestionGroup
                {
                    ExamId = examId,
                    CertId = exam.CertId,
                    SectionId = request.SectionId,
                    DisplayOrder = currentDisplayOrder,
                    Passage = q.Passage,
                    Transcript = q.Transcript,
                    UnitNumber = 1
                };
                db.QuestionGroups.Add(activeGroup);
                await db.SaveChangesAsync(cancellationToken);
                groupsCreated++;

                activePassage = q.Passage;
                activeGroupNum = q.GroupNumber;

                // One stimulus per paste group, not one per child: that is what a shared passage is.
                groupStimuli[activeGroup.GroupId] = new PoolImport.StimulusInput(
                    PoolImport.Normalise(q.CefrLevel),
                    Passage: q.Passage,
                    Transcript: q.Transcript);
            }

            int? categoryId = null;
            if (!string.IsNullOrWhiteSpace(q.CategoryCode) && categories.TryGetValue(q.CategoryCode, out var cid))
            {
                categoryId = cid;
            }

            var qType = q.QuestionType?.ToLowerInvariant() ?? "mcq";

            var question = new Question
            {
                ExamId = examId,
                SectionId = request.SectionId,
                SkillId = section.SkillId,
                GroupId = activeGroup!.GroupId,
                Stem = q.Stem.Trim(),
                // #229 / D1: the level is the commit's own unless the paste states one per question,
                // and no difficulty is written beside it. Which of the two applies was decided and
                // validated before the transaction opened, so this cannot fall back on a typo.
                CefrLevel = string.IsNullOrWhiteSpace(q.CefrLevel) ? commitLevel : AuthoredLevelOrThrow(q.CefrLevel, q.Index),
                QuestionType = qType,
                Explanation = q.Explanation?.Trim(),
                CategoryId = categoryId,
                PrepSeconds = q.PrepSeconds,
                ResponseSeconds = q.ResponseSeconds,
                MinWords = q.MinWords,
                MaxWords = q.MaxWords,
                MaxSelections = q.MaxSelections,
                AllowNumber = q.AllowNumber,
                AcceptedAnswersJson = q.AcceptedAnswersJson,
                RequiredKeywords = q.RequiredKeywords?.Trim(),
                Hint = q.Hint?.Trim(),
                SampleAnswerOrImageDesc = q.SampleAnswerOrImageDesc?.Trim(),
                ReferenceKind = q.ReferenceKind?.ToCode(),
                DisplayOrder = currentDisplayOrder
            };

            db.Questions.Add(question);
            await db.SaveChangesAsync(cancellationToken);
            questionsCreated++;

            if (q.Options != null && q.Options.Count > 0)
            {
                foreach (var opt in q.Options)
                {
                    var option = new QuestionOption
                    {
                        QuestionId = question.QuestionId,
                        QuestionType = question.QuestionType,
                        Label = opt.Label.Trim().ToUpperInvariant(),
                        Text = opt.Text.Trim(),
                        IsCorrect = opt.IsCorrect,
                        Rationale = opt.Rationale?.Trim()
                    };
                    db.QuestionOptions.Add(option);
                }
            }

            // Resolved after the legacy row exists: the pool write binds by the position the paper
            // renders, and a position is a rank among rows, so it cannot be known before them.
            if (TryBuildPoolContent(topics, section.Modality, q, request, out var content, out var reason))
            {
                writtenQuestions.Add((question.QuestionId, activeGroup.GroupId, content!, q.Stem.Trim(), q));
            }
            else
            {
                poolNotes.Add($"Question {q.Index} (\"{Truncate(q.Stem)}\") not written to the pool - {reason}");
            }

            currentDisplayOrder++;
        }

        await db.SaveChangesAsync(cancellationToken);

        var poolWritten = 0;
        var poolStimuli = 0;
        var poolBound = 0;
        foreach (var groupId in writtenQuestions.Select(w => w.GroupId).Distinct())
        {
            var requests = writtenQuestions
                .Where(w => w.GroupId == groupId)
                .Select(w => new PoolImportWriter.Request(
                    w.QuestionId,
                    $"{request.SectionId}:{groupId}",
                    w.Content,
                    w.Stem,
                    w.Source.Explanation,
                    w.Source.SampleAnswerOrImageDesc,
                    w.Source.ReferenceKind?.ToCode(),
                    groupStimuli.TryGetValue(groupId, out var stimulus)
                        ? stimulus
                        : new PoolImport.StimulusInput(request.CefrLevel)))
                .ToList();

            var outcome = await PoolImportWriter.WriteAsync(
                db, exam, request.SectionId, section.Modality, requests, null, clock?.GetUtcNow() ?? DateTimeOffset.UtcNow, cancellationToken);
            poolWritten += outcome.ItemsWritten;
            poolStimuli += outcome.StimuliWritten;
            poolBound += outcome.Bound;
            poolNotes.AddRange(outcome.Notes);
        }

        exam.UpdatedAt = clock?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        var message = $"Successfully committed {questionsCreated} questions to section '{section.Name}'.";
        if (poolWritten > 0)
        {
            message += $" Wrote {poolWritten} pool items and {poolStimuli} shared stimuli, {poolBound} bound to this paper.";
        }

        return new QuickIngestCommitResultDto(
            Success: true,
            QuestionsCommitted: questionsCreated,
            GroupsCreated: groupsCreated,
            Message: poolNotes.Count == 0 ? message : $"{message} {string.Join(" ", poolNotes)}",
            PoolItemsWritten: poolWritten,
            PoolStimuliWritten: poolStimuli,
            PoolItemsBound: poolBound);
    }

    /// <summary>
    /// #229 / D1: a per-question level already proved authored by the pre-validation loop. It parses
    /// with the same call, so the loop and the write cannot disagree about what "authored" means.
    /// </summary>
    private static CefrLevel AuthoredLevelOrThrow(string? code, int questionIndex) =>
        CefrLevel.TryParseAuthored(code?.Trim(), out var level)
            ? level
            : throw new AdminExamException(
                ApiError.Codes.Validation,
                $"Question {questionIndex} states cefrLevel '{code}', which is not an authored level.",
                400);

    /// <summary>
    /// #189: the pool content for one pasted question, or the reason it cannot become pool stock.
    /// The level and topic are asked for once per commit and may be overridden per question, because
    /// the parser reads text, not a level - D1 forbids inventing one from the difficulty number.
    /// </summary>
    private static bool TryBuildPoolContent(
        TopicCatalog topics,
        string modality,
        QuickIngestParsedQuestionDto question,
        QuickIngestCommitRequest request,
        out PoolContent? content,
        out string reason)
    {
        var options = (question.Options ?? [])
            .Select(o => ((string?)o.Label, (string?)o.Text, o.IsCorrect, (string?)o.Rationale))
            .ToList();

        return PoolImport.TryBuildContent(
            topics,
            modality,
            question.ItemType,
            hasPassage: !string.IsNullOrWhiteSpace(question.Passage),
            question.TopicCode ?? request.TopicCode,
            question.CategoryCode,
            question.CefrLevel ?? request.CefrLevel,
            question.PlacementEligible,
            options,
            question.EvidenceStart,
            question.EvidenceEnd,
            question.EvidenceUnit,
            question.EvidenceQuote,
            out content,
            out reason);
    }

    private static string Truncate(string? stem) =>
        stem is null ? "(no stem)" : stem.Length > 60 ? stem[..57] + "..." : stem;
}
