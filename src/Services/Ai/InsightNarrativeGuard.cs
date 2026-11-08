using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BusinessObjects;

namespace Services;

/// <summary>
/// #244 / NFI-10 / D21: the guard between a generated narrative and a learner.
/// <para>
/// <b>It checks the narrative against the payload, not against a shape.</b> A validator that only looks
/// for "B1"-shaped strings passes "your writing is at B1" whenever B1 exists anywhere in the payload -
/// for another skill, or for the target band - and it passes "your weakest skill is speaking", which
/// quotes no number at all. So every citation is resolved to the row it claims to describe: a level
/// must belong to a skill the sentence names (or be the overall / the target band, named as such), an
/// unmeasured skill must not be given a level or a figure, a modality the certificate has no skill for
/// may not be named at all, and a skill named as the weakest must be the one the payload computed.
/// </para>
/// <para>
/// <b>A failure rejects the whole narrative, not the offending sentence.</b> A narrative is the unit the
/// model was asked to produce, and its sentences argue with each other: dropping sentence three leaves
/// a document whose remaining sentences were written to support a claim that is gone, and rewriting the
/// remainder would make this service a second author of prose. The generator reports failure, the queue
/// stores no narrative, and the learner sees the existing retry state - the invented sentence is never
/// shown, which is what the ticket asks for.
/// </para>
/// </summary>
public static class InsightNarrativeGuard
{
    /// <summary>CK_Skills_Modality's closed set. A narrative may only name a modality the payload has a skill for.</summary>
    private static readonly string[] AllModalities =
        [Modality.Listening.ToCode(), Modality.Reading.ToCode(), Modality.Speaking.ToCode(), Modality.Writing.ToCode()];

    /// <summary>Words that name a skill as the weakest. This is a claim about a computed fact, so a
    /// contradicting sentence is the one that uses these.</summary>
    private static readonly string[] LimitingClaims =
        ["weakest", "limiting", "holding you back", "biggest gap"];

    /// <summary>Adjectives that describe a skill's standing. Paired with an unmeasured skill they turn
    /// "we have not measured writing" into "your writing is strong". Verbs such as "practice" and
    /// "drill" are deliberately absent: recommending practice in an unmeasured skill claims nothing.</summary>
    private static readonly string[] StandingClaims =
        ["strong", "weak", "behind", "ahead", "falling short", "well developed", "underdeveloped"];

    /// <summary>Words that mark a level token as the overall or the goal rather than a skill's own.
    /// Without one of these, a level in a sentence that names skills is read as a claim about them.</summary>
    private static readonly string[] OverallOrGoalWords =
        ["overall", "target", "goal", "aim", "reach", "working towards", "working toward"];

    /// <summary>A level token: A1-C2 and the reportable floor. A certificate band code ("B2" on a score
    /// table) is the same characters, which is exactly why a token alone proves nothing.
    /// <para>
    /// <b>The token must be a whole word.</b> A certificate code or a hex digest contains level-shaped
    /// substrings by chance ("PR<b>a2</b>f1"), and a lookbehind that only excluded letters let "PR4a2"
    /// through - so an honest narrative naming its certificate was rejected roughly one run in ten,
    /// depending on the random code. Digits are excluded on both sides for the same reason.
    /// </para>
    /// </summary>
    private static readonly Regex LevelToken = new(
        @"(?<![\p{L}\p{N}_])(pre_a1|[abc][12])(?![\p{L}\p{N}_])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumberToken = new(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);

    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+|\r?\n+", RegexOptions.Compiled);

    private static readonly Regex WordBoundary = new(@"[\p{L}\p{N}_]+", RegexOptions.Compiled);

    /// <summary>
    /// Returns null when the narrative may be shown, or the reason it may not. The reason names the
    /// offending value, so a failure is diagnosable from the stored <c>FailureReason</c>.
    /// </summary>
    public static string? Rejection(AiInsightGenerationRequest request, string? narrative)
    {
        if (string.IsNullOrWhiteSpace(narrative))
        {
            return null;
        }

        // The figures are checked whether or not the caller attached a payload, so a future builder
        // that forgets to attach one loses the skill checks but cannot lose the number check. A guard
        // that quietly passes everything when its input is missing is the failure mode this ticket is
        // about, one level up.
        var allowedNumbers = AllowedNumbers(request);
        foreach (Match token in NumberToken.Matches(WithoutLevelTokens(narrative)))
        {
            if (!TryParse(token.Value, out var value) || !allowedNumbers.Contains(value))
            {
                return $"Narrative cites the number '{token.Value}', which is not in the payload.";
            }
        }

        // The skill and level claims need the payload. Every builder in the service attaches one.
        if (request.Facts is not { } facts)
        {
            return null;
        }

        var allowedLevels = AllowedLevels(facts);

        foreach (Match token in LevelToken.Matches(narrative))
        {
            if (!allowedLevels.Contains(token.Value))
            {
                return $"Narrative cites the level '{token.Value}', which the payload does not report.";
            }
        }

        foreach (var sentence in SentenceSplit.Split(narrative))
        {
            if (string.IsNullOrWhiteSpace(sentence))
            {
                continue;
            }

            var reason = CheckSentence(sentence, facts);
            if (reason is not null)
            {
                return reason;
            }
        }

        return null;
    }

    private static string? CheckSentence(string sentence, InsightFacts facts)
    {
        var lower = sentence.ToLowerInvariant();
        var mentioned = facts.Skills.Where(skill => Names(lower, skill)).ToList();

        // Criterion 3: a modality this certificate has no skill for. The payload's skill set is the
        // only list that can say "listening" is not part of this certificate at all.
        var examined = facts.Skills.Select(s => s.Modality).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var modality in AllModalities)
        {
            if (!examined.Contains(modality) && ContainsWord(lower, modality))
            {
                return $"Narrative names '{modality}', which certificate {facts.CertCode} does not examine.";
            }
        }

        var levels = LevelToken.Matches(sentence).Select(m => m.Value).ToList();
        var numbers = NumberToken.Matches(WithoutLevelTokens(sentence)).ToList();
        var namesOverallOrGoal = ContainsAny(lower, OverallOrGoalWords);

        if (mentioned.Count == 0)
        {
            // The sentence names no skill, so a level in it is the overall or the goal. It must be
            // reported as one: "your writing is at B1" has this shape whenever the skill is implied
            // rather than typed, and a guard that skips unnamed sentences never sees it.
            foreach (var level in levels)
            {
                var isOverallOrGoal =
                    namesOverallOrGoal &&
                    (string.Equals(facts.Overall?.Code, level, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(facts.TargetBandCode, level, StringComparison.OrdinalIgnoreCase));

                if (!isOverallOrGoal)
                {
                    return $"Narrative cites the level '{level}' without naming the skill it belongs to; " +
                           $"the overall is '{facts.Overall?.Code ?? "none"}'.";
                }
            }

            return null;
        }

        // A level belongs to a skill the sentence names, unless it is the overall or the goal and the
        // sentence says so. "Reading is at B1" where reading is B2 fails even when B1 is the overall.
        foreach (var level in levels)
        {
            if (mentioned.Any(skill => string.Equals(skill.Level?.Code, level, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var isOverallOrGoal =
                namesOverallOrGoal &&
                (string.Equals(facts.Overall?.Code, level, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(facts.TargetBandCode, level, StringComparison.OrdinalIgnoreCase));

            if (!isOverallOrGoal)
            {
                return $"Narrative gives the level '{level}' to " +
                       $"{string.Join(", ", mentioned.Select(m => m.SkillName))}, whose payload level is different.";
            }
        }

        // #211's distinction, as a guard: an unmeasured skill is absent, never present at the bottom
        // band. A level, a figure or a standing adjective attached to one is the fabricated-bottom-band
        // failure the payload exists to prevent.
        var claimsStanding = levels.Count > 0 || numbers.Count > 0 || ContainsAny(lower, StandingClaims);
        if (claimsStanding)
        {
            var unmeasured = mentioned.Where(skill => skill.Level is null).ToList();
            if (unmeasured.Count > 0 &&
                !lower.Contains("not measured") &&
                !lower.Contains("no measurement"))
            {
                return $"Narrative describes {string.Join(", ", unmeasured.Select(s => s.SkillName))}, " +
                       "which has no measurement in the payload.";
            }
        }

        // The overall and its limiting skill are computed once, in the payload. A sentence that names a
        // skill as the weakest is a second computation of the same fact.
        if (ContainsAny(lower, LimitingClaims))
        {
            if (facts.LimitingSkillCode is null)
            {
                return "Narrative claims a weakest skill, but the payload reports no measurement to derive one from.";
            }

            if (!mentioned.Any(skill => string.Equals(skill.SkillCode, facts.LimitingSkillCode, StringComparison.OrdinalIgnoreCase)))
            {
                return $"Narrative names {string.Join(", ", mentioned.Select(s => s.SkillName))} as the weakest; " +
                       $"the payload names '{facts.LimitingSkillCode}'.";
            }
        }

        return null;
    }

    private static HashSet<string> AllowedLevels(InsightFacts facts)
    {
        var levels = facts.Skills
            .Where(skill => skill.Level is not null)
            .Select(skill => skill.Level!.Value.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (facts.Overall is { } overall)
        {
            levels.Add(overall.Code);
        }

        // PRN-03: the target is a band, and its code can read like a level. It is a payload token, not
        // a level claim - a narrative may name it because it is the goal the learner set.
        if (facts.TargetBandCode is { } targetBand)
        {
            levels.Add(targetBand);
        }

        return levels;
    }

    /// <summary>
    /// Every number the request can put in front of the model, plus the values the code derives from
    /// those fields to write its own prose. The payload is the request, so a number the prompt could
    /// show and a number the narrator may repeat cannot drift apart.
    /// <para>
    /// <b>This is a whitelist, not a range.</b> Allowing "any number up to the largest count" would
    /// accept "9 of 12 correct" for a payload that says 3 of 5: the invented figure would sit inside the
    /// range the code could have produced. The derivations below mirror
    /// <see cref="FakeAiInsightGenerator"/> exactly, and that mirror is the cost of not loosening the
    /// check - a wrong sum here rejects a correct narrative, which the tests make visible.
    /// </para>
    /// </summary>
    private static HashSet<decimal> AllowedNumbers(AiInsightGenerationRequest request)
    {
        var allowed = new HashSet<decimal>();

        Add(allowed, request.TotalQuestions, request.AnsweredCount, request.CorrectCount,
            request.HintUsedCount, request.CorrectWithHintCount, request.Questions.Count);

        var sectionAnswered = 0;
        var sectionCorrect = 0;
        foreach (var section in request.SectionAccuracies)
        {
            Add(allowed, section.Answered, section.Correct, section.AccuracyPercentage);
            Add(allowed, Percent(section.Correct, section.Answered));
            sectionAnswered += section.Answered;
            sectionCorrect += section.Correct;
        }

        foreach (var category in request.CategoryAccuracies)
        {
            Add(allowed, category.Answered, category.Correct, category.AccuracyPercentage);
            Add(allowed, Percent(category.Correct, category.Answered));
        }

        Add(allowed, sectionAnswered, sectionCorrect, request.SectionAccuracies.Count, request.CategoryAccuracies.Count);

        // Reference numbers a narrative legitimately names: a code or name carrying a digit ("Part 5",
        // "P5", "VSTEP 6"), and the 0-100 scale every position is stated on. These are payload tokens,
        // not figures about the learner.
        AddDigits(allowed, request.CertName, request.CurrentBandCode, request.TargetBandCode, request.TargetBandName);
        foreach (var section in request.SectionAccuracies)
        {
            AddDigits(allowed, section.SectionCode, section.SectionName);
        }

        foreach (var category in request.CategoryAccuracies)
        {
            AddDigits(allowed, category.CategoryCode, category.CategoryName);
        }

        if (request.Facts is { } facts)
        {
            AddDigits(allowed, facts.CertCode, facts.TargetBandCode, facts.TargetBandName);
            AddDigits(allowed, facts.Skills.SelectMany(s => new[] { s.SkillCode, s.SkillName }).ToArray());
            Add(allowed, 0, 100);
        }

        // The deterministic narrator's own arithmetic over the payload's rows.
        var missed = request.Questions.Where(q => q.IsCorrect == false).ToList();
        Add(allowed, missed.Count);
        foreach (var group in missed.GroupBy(q => new { q.CategoryId, q.CategoryName }))
        {
            Add(allowed, group.Count());
        }

        var correctWithHint = request.CorrectWithHintCount ?? request.Questions.Count(q => q.HintUsed && q.IsCorrect == true);
        var lucky = CountLuckyGuesses(request);
        Add(allowed, correctWithHint, lucky, Math.Max(0, request.CorrectCount - correctWithHint - lucky));

        Add(allowed, Percent(request.CorrectCount, request.AnsweredCount));
        Add(allowed, Percent(request.AnsweredCount, request.TotalQuestions));
        if (request.SectionAccuracies.Count > 0)
        {
            Add(allowed, (int)Math.Round((decimal)request.SectionAccuracies.Average(s => s.AccuracyPercentage), 0));
        }

        if (request.Facts is { } skillFacts)
        {
            foreach (var skill in skillFacts.Skills)
            {
                Add(allowed, skill.PositionInBand);
                if (skill.ContinuousCoordinate is { } coordinate)
                {
                    allowed.Add(coordinate);
                }
            }
        }

        foreach (var evidence in request.WritingEvidences ?? [])
        {
            Add(allowed, evidence.WordCount);
            foreach (var score in evidence.Scores)
            {
                allowed.Add(score.Score);
                allowed.Add(score.MaxScore);
            }
        }

        // The narrator states its own threshold as a rule ("below 45% prior accuracy"), so the
        // threshold itself is a payload-adjacent constant rather than a claim about the learner.
        Add(allowed, FakeAiInsightGenerator.WeakCategoryAccuracyThreshold);

        // A prior insight is a stored fact too: the narrative may compare against its figures and its
        // date. Parsing its prose is the only form in which those numbers exist.
        if (request.PreviousInsight is { } previous)
        {
            foreach (Match token in NumberToken.Matches(previous.Narrative ?? string.Empty))
            {
                if (TryParse(token.Value, out var value))
                {
                    allowed.Add(value);
                }
            }

            allowed.Add(previous.CreatedAt.Day);
            allowed.Add(previous.CreatedAt.Month);
            allowed.Add(previous.CreatedAt.Year % 100);
        }

        return allowed;
    }

    /// <summary>The narrator's "fortunate guess" count: a correct answer with no hint, in a category
    /// whose prior accuracy is weak. Recomputed here so a narrative may quote it.</summary>
    private static int CountLuckyGuesses(AiInsightGenerationRequest request) =>
        request.Questions.Count(q =>
            q.IsCorrect == true &&
            !q.HintUsed &&
            q.CategoryId.HasValue &&
            request.CategoryAccuracies.Any(ca =>
                ca.CategoryId == q.CategoryId.Value &&
                ca.Answered >= 2 &&
                ca.AccuracyPercentage < FakeAiInsightGenerator.WeakCategoryAccuracyThreshold));

    private static void Add(HashSet<decimal> allowed, params int?[] values)
    {
        foreach (var value in values)
        {
            if (value is { } number)
            {
                allowed.Add(number);
            }
        }
    }

    /// <summary>A code or name may carry a digit ("Part 5", "P5"); the digit is a label, not a count.</summary>
    private static void AddDigits(HashSet<decimal> allowed, params string?[] labels)
    {
        foreach (var label in labels)
        {
            if (label is null)
            {
                continue;
            }

            foreach (Match token in NumberToken.Matches(label))
            {
                if (TryParse(token.Value, out var value))
                {
                    allowed.Add(value);
                }
            }
        }
    }

    private static int? Percent(int part, int whole) =>
        whole > 0 ? (int)Math.Round((decimal)part / whole * 100, 0) : null;

    /// <summary>Removes level tokens so "B1" does not contribute a bare 1 to the number check.</summary>
    private static string WithoutLevelTokens(string text) => LevelToken.Replace(text, " ");

    private static bool Names(string lowerSentence, InsightSkillFact skill) =>
        ContainsWord(lowerSentence, skill.SkillName) ||
        ContainsWord(lowerSentence, skill.SkillCode) ||
        ContainsWord(lowerSentence, skill.Modality);

    private static bool ContainsWord(string lowerSentence, string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        foreach (Match token in WordBoundary.Matches(lowerSentence))
        {
            if (string.Equals(token.Value, word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the sentence contains <paramref name="phrase"/> as whole words.
    /// <para>
    /// <b>Why not <c>string.Contains</c>.</b> "aim" is inside <em>claim</em>, <em>claims</em> and
    /// <em>reclaim</em>; "reach" is inside <em>reached</em>; "weak" is inside <em>weaker</em>. A sentence
    /// saying "you can claim your result" was therefore read as naming the goal, which loosened the
    /// level-attribution check this guard exists to tighten, and "you reached reading at B1" was read as
    /// the goal rather than as a skill at a level.
    /// </para>
    /// <para>
    /// Multi-word phrases ("holding you back", "working towards") are matched as consecutive tokens, so
    /// the same rule covers both shapes.
    /// </para>
    /// </summary>
    private static bool ContainsPhrase(string lowerSentence, string phrase) =>
        HasTokenRun(Tokens(lowerSentence), Tokens(phrase));

    private static bool ContainsAny(string lowerSentence, IEnumerable<string> phrases) =>
        phrases.Any(phrase => ContainsPhrase(lowerSentence, phrase));

    private static List<string> Tokens(string text) =>
        WordBoundary.Matches(text).Select(m => m.Value).ToList();

    private static bool HasTokenRun(List<string> sentence, List<string> phrase)
    {
        if (phrase.Count == 0 || phrase.Count > sentence.Count)
        {
            return false;
        }

        for (var start = 0; start <= sentence.Count - phrase.Count; start++)
        {
            var matched = true;
            for (var offset = 0; offset < phrase.Count; offset++)
            {
                if (!string.Equals(sentence[start + offset], phrase[offset], StringComparison.Ordinal))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }


    private static bool TryParse(string token, out decimal value) =>
        decimal.TryParse(token.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
