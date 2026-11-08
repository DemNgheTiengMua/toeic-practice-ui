"""#237: prove LearningPathService reads the stored mastery fact rather than recomputing it.

Restores the pre-#237 body of LoadCellMasteryAsync (count answers, apply the legacy 85%/10 rule) and
confirms A_stored_mastered_cell_... goes red. Then restores the file from memory.

Run from the repo root:
    python docs/schema-tests/prove-237/prove-guard-237-recompute.py
"""
import pathlib, subprocess

PATH = pathlib.Path("src/Services/GuidedLearning/LearningPathService.Coordinates.cs")

NEW_BODY = """        var state = await db.UserCellMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.TopicId == topicId && m.CefrLevel == level)
            .Select(m => m.State)
            .FirstOrDefaultAsync(cancellationToken);

        return state is null ? MasteryState.Untested : EnumCodes.Parse<MasteryState>(state);"""

OLD_BODY = """        var counts = await db.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.Session.UserId == userId
                && pa.IsCorrect != null
                && ((pa.ItemId != null
                        && db.Items.Any(i => i.ItemId == pa.ItemId
                            && i.TopicId == topicId && i.CefrLevel == level))
                    || (pa.CardId != null
                        && pa.Session.TopicId == topicId
                        && pa.Session.CefrLevel == level)))
            .GroupBy(pa => 1)
            .Select(g => new { Answered = g.Count(), Correct = g.Count(a => a.IsCorrect == true) })
            .FirstOrDefaultAsync(cancellationToken);

        if (counts is null || counts.Answered == 0)
        {
            return MasteryState.Untested;
        }

        var accuracy = (int)Math.Round((double)counts.Correct / counts.Answered * 100);

        return accuracy >= MasterySchedule.MinAccuracyToMastered && counts.Answered >= MasterySchedule.MinAnswersToMastered
            ? MasteryState.Mastered
            : accuracy >= MasterySchedule.MinAccuracyToReviewing && counts.Answered >= MasterySchedule.MinAnswersToReviewing
                ? MasteryState.Reviewing
                : MasteryState.Learning;"""

text = PATH.read_text(encoding="utf-8")
if NEW_BODY not in text:
    print("!! the stored-fact body is not present; has it drifted?")
    raise SystemExit(2)

PATH.write_text(text.replace(NEW_BODY, OLD_BODY), encoding="utf-8")
try:
    r = subprocess.run(
        ["dotnet", "test", "tests/Services.Tests",
         "--filter", "FullyQualifiedName~A_stored_mastered_cell"],
        capture_output=True, text=True,
    )
    out = r.stdout + r.stderr
    failed = [ln.strip() for ln in out.splitlines() if ln.strip().startswith("Failed Services.Tests")]
    print("=== recompute restored (pre-#237 behaviour)")
    for f in failed:
        print("  FAIL", f)
    print("  VERDICT:", "RED as expected" if failed else "!! GREEN - the test does not prove the source")
finally:
    PATH.write_text(text, encoding="utf-8")
    print("  restored", PATH)

