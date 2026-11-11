using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class PracticeService
{

    public async Task<PracticeSummary> GetSummaryAsync(int userId, int sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.PracticeSessions
            .Include(s => s.Section)
            .Include(s => s.Topic)
            .Where(s => s.SessionId == sessionId && s.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SessionNotFound, $"Practice session {sessionId} was not found.");

        // D3: reading a session does not finish it. This was a write on a GET - "finish it or ensure
        // answers are final" - so opening the summary mid-drill (or prefetching it) closed the session
        // underneath the learner, and their next submission was refused because the session was no
        // longer in progress. The transition belongs to the sitting: a learner who answers the last
        // question finishes there, and only there. A read reports what it found.
        // D3: reading a session does not finish it.
        //
        // This used to be "if the session is in progress, finish it and stamp FinishedAt". Both the
        // summary page and the AI-insight panel read an unfinished session on the way in, so opening
        // the summary mid-drill (or prefetching it) closed the session underneath the learner: their
        // next submission was then refused because the session was no longer in progress. The
        // transition belongs to the sitting - a learner who answers the last question finishes there,
        // and only there. A read reports the state it found.

        var answers = await db.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.SessionId == sessionId)
            .OrderBy(pa => pa.DisplayOrder)
            .ToListAsync(cancellationToken);

        // #187: this is the criterion that has to be observed. Each answer renders the version it
        // pinned — not the item's current version, and not whatever the item says today. A correction
        // after the sitting creates version 2; this read does not look at it.
        var pinned = PlacePinnedVersions(answers, await LoadPinnedVersionsAsync(answers, cancellationToken));
        var stimuli = await LoadStimuliAsync(pinned.Values, cancellationToken);

        var outcomes = answers.Select(a =>
        {
            var version = pinned.GetValueOrDefault(a.AnswerId);

            if (version is not null)
            {
                // A version stores labels A-D with no option rows, so the chosen and the key texts
                // are read from its four columns. An answer with no label chose nothing.
                var chosenLabel = AttemptEngine.LabelIndex(a.SelectedLabel) >= 0 ? a.SelectedLabel : null;
                var stimulus = stimuli.GetValueOrDefault(version.ItemId);

                return new QuestionOutcome(
                    a.DisplayOrder,
                    null,
                    null,
                    version.Stem,
                    chosenLabel,
                    PinnedOptionText(version, chosenLabel),
                    version.KeyLabel,
                    PinnedOptionText(version, version.KeyLabel),
                    a.IsCorrect,
                    a.HintUsed,
                    version.Explanation,
                    stimulus?.AudioPath,
                    // #269: the pinned branch dropped the audio slice while the take projection read it,
                    // so the SUMMARY of a pool drill lost the slice the learner had just listened to.
                    stimulus?.AudioStartMs,
                    stimulus?.AudioEndMs,
                    stimulus?.Transcript,
                    AttemptEngine.IsProductive(version) ? QuestionType.FreeResponse.ToCode() : RenderTypeOf(version),
                    a.ResponseText,
                    a.SelectedOptionIdsJson,
                    AcceptedAnswersJson: null,
                    version.ItemVersionId);
            }

            // Every answer pins a version, so a row whose version is gone has nothing to render.
            return new QuestionOutcome(
                a.DisplayOrder,
                null,
                null,
                string.Empty,
                null,
                null,
                null,
                null,
                a.IsCorrect,
                a.HintUsed,
                null,
                null,
                null,
                null,
                null,
                QuestionType.Mcq.ToCode(),
                a.ResponseText,
                a.SelectedOptionIdsJson,
                AcceptedAnswersJson: null,
                null);
        }).ToList();

        var (code, name) = ScopeLabels(session);
        var total = answers.Count;
        // #253: the same definition the review screen and the take page use, so the summary's
        // answered count and the navigator cannot disagree about one attempt.
        var answered = answers.Count(a =>
            AnswerContent.IsAnswered(a.SelectedOptionId, a.SelectedLabel, a.ResponseText, a.SelectedOptionIdsJson));
        var correct = answers.Count(a => a.IsCorrect == true);
        // An answered-but-ungraded pool answer is neither correct nor incorrect yet; counting it as
        // incorrect would report a miss the learner never made.
        var incorrect = answers.Count(a => a.IsCorrect == false);
        var skipped = total - answered;
        var accuracy = answered > 0 ? (int)Math.Round((double)correct / answered * 100) : 0;
        var hintUsedTotal = answers.Count(a => a.HintUsed);
        var correctWithHint = answers.Count(a => a.HintUsed && a.IsCorrect == true);
        var incorrectWithHint = answers.Count(a => a.HintUsed && a.IsCorrect == false);

        return new PracticeSummary(
            session.SessionId,
            code,
            name,
            EnumCodes.Parse<Modality>(session.Modality),
            session.StartedAt,
            session.FinishedAt,
            total,
            answered,
            correct,
            incorrect,
            skipped,
            accuracy,
            hintUsedTotal,
            correctWithHint,
            incorrectWithHint,
            outcomes);
    }
}
