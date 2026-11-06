using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BusinessObjects;

/// <summary>
/// #194: gate one's rule names. A refusal carries one of these codes, so a caller can branch on the
/// rule without reading English, and <see cref="GateOneRefusal.Message"/> says the same rule in
/// words for the author who has to fix the item.
/// </summary>
public static class GateOneRules
{
    public const string FourOptions = "gate1.options.four";
    public const string MissingKey = "gate1.key.missing";
    public const string OptionsTooSimilar = "gate1.options.distance";
    public const string MetaOption = "gate1.options.meta";
    public const string KeyLengthParity = "gate1.key.length";
    public const string PartOfSpeechParallelism = "gate1.options.pos";
    public const string KeyCellBalance = "gate1.key.cell_balance";
    public const string UniqueStemKeyLemma = "gate1.content.unique_lemma";
    public const string Explanation = "gate1.feedback.explanation";
    public const string OptionRationale = "gate1.feedback.rationale";
}

/// <summary>
/// #194: the key letters already stored in one cell - (topic, level). Key balance is a property of
/// the cell and never of an item, so the caller counts the cell and passes it in. <c>A</c> is how
/// many stored keys are <c>A</c>, and so on.
/// </summary>
public readonly record struct GateOneCell(int A, int B, int C, int D)
{
    public int Total => A + B + C + D;

    public int CountOf(char label) => label switch
    {
        'A' => A,
        'B' => B,
        'C' => C,
        'D' => D,
        _ => 0,
    };

    public GateOneCell Add(char label) => label switch
    {
        'A' => this with { A = A + 1 },
        'B' => this with { B = B + 1 },
        'C' => this with { C = C + 1 },
        'D' => this with { D = D + 1 },
        _ => this,
    };

    /// <summary>Counts the letters of a cell's stored keys. A missing or unknown label is not a key.</summary>
    public static GateOneCell FromLabels(IEnumerable<string?> labels)
    {
        var cell = default(GateOneCell);
        foreach (var label in labels)
        {
            var code = label?.Trim().ToUpperInvariant();
            if (code is "A" or "B" or "C" or "D")
            {
                cell = cell.Add(code![0]);
            }
        }

        return cell;
    }
}

/// <summary>
/// #194: one item as authored - the scored columns of <c>ItemVersions</c>, plus the stimulus text the
/// lemma rule needs. All four options empty and no key is the productive shape, which gate one does
/// not judge: its rules are the multiple-choice rules.
/// </summary>
public sealed record GateOneItem(
    string Stem,
    string? Stimulus,
    string? OptionA,
    string? OptionB,
    string? OptionC,
    string? OptionD,
    string? KeyLabel,
    string? Explanation,
    string? RationaleA = null,
    string? RationaleB = null,
    string? RationaleC = null,
    string? RationaleD = null);

/// <summary>#194: one rule broken, named by code and by sentence.</summary>
public sealed record GateOneRefusal(string Rule, string Message);

/// <summary>#194: something the author must be told even though this item is not refused.</summary>
public sealed record GateOneNotice(string Rule, string Message);

/// <summary>#194: gate one's verdict on one item. A notice never blocks; only a refusal does.</summary>
public sealed record GateOneVerdict(
    IReadOnlyList<GateOneRefusal> Refusals,
    IReadOnlyList<GateOneNotice> Notices)
{
    public bool Accepted => Refusals.Count == 0;
}

/// <summary>
/// #194: gate one - structural and item hygiene (CNT-35, D17). A pure function: no database, no
/// clock, no file, no network. Every rule names itself rather than answering a bare "invalid",
/// because an author told only "rejected" uploads the same defect again.
/// <para>
/// Two rules need context an item does not carry, and both are explicit in the signature:
/// <list type="bullet">
/// <item><b>Key balance is per cell, not per item.</b> The item cannot be judged alone, so
/// <see cref="GateOneCell"/> - the keys already stored in the item's (topic, level) cell - comes in
/// beside it. <see cref="ReviewCell"/> answers the half of the rule no item can fix: an imbalanced
/// cell is <em>reported</em>, never silently accepted, and an item is refused only when keying it
/// differently would actually repair the cell.</item>
/// <item><b>Part-of-speech parallelism is checked only where grammar is decidable.</b> No tagger is
/// installed in this solution and none was added: a suffix guess tags "supervisor" as an agent noun
/// because of -or and refuses real content. The check therefore recognises a noun phrase by its
/// determiner and a prepositional phrase by its preposition, and <em>reports</em> an option whose
/// shape it cannot decide instead of inventing a verdict for it.</item>
/// </list>
/// </para>
/// </summary>
public static class GateOne
{
    /// <summary>D17: pairwise Levenshtein distance <em>strictly greater than</em> 2.</summary>
    public const int MinimumPairwiseDistance = 3;

    /// <summary>The key's word count may not deviate from the distractor mean by more than 25%.</summary>
    public const int KeyLengthTolerancePercent = 25;

    /// <summary>D17: each letter holds between 20% and 30% of a cell's keys.</summary>
    public const int KeyShareFloorPercent = 20;

    public const int KeyShareCeilingPercent = 30;

    private static readonly string[] Labels = ["A", "B", "C", "D"];

    /// <summary>
    /// A meta-option is an option that can be chosen without reading the stem: it answers about the
    /// other options. It is matched as a whole phrase, because a substring match on "all" or "above"
    /// refuses "All staff must sign in" - a perfectly good distractor - and a rule that refuses real
    /// content is worse than no rule.
    /// </summary>
    private static readonly Regex MetaOptionPattern = new(
        @"^(?:(?:all|none)\s+of\s+(?:the\s+)?(?:above|these|those|them)(?:\s+(?:answers?|options?|statements?|choices?|items?))?"
        + @"|(?:both\s+[a-d]\s+and|either\s+[a-d]\s+or)\s+[a-d]"
        + @"|neither\s+[a-d]\s+nor\s+[a-d]"
        + @"|[a-d]\s+(?:and|or)\s+[a-d])$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Words that carry no content: closed classes, auxiliaries, pronouns and quantifiers. A lemma
    /// held in common by the stem and the key only gives the item away when it is a content word.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "but", "nor", "if", "then", "than", "so", "such", "as",
        "of", "to", "in", "on", "at", "by", "for", "from", "with", "without", "into", "onto",
        "about", "over", "under", "after", "before", "during", "between", "through", "above",
        "below", "up", "down", "out", "off", "again", "once", "very", "too", "also", "only", "just",
        "is", "are", "was", "were", "be", "been", "being", "am", "do", "does", "did", "done",
        "have", "has", "had", "can", "could", "may", "might", "must", "shall", "should", "will",
        "would", "not", "no", "there", "here", "this", "that", "these", "those", "it", "its",
        "he", "him", "his", "she", "her", "they", "them", "their", "we", "us", "our", "you", "your",
        "i", "me", "my", "who", "whom", "whose", "which", "what", "when", "where", "why", "how",
        "all", "any", "some", "every", "each", "most", "more", "both", "either", "neither", "other",
    };

    private static readonly HashSet<string> Determiners = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "this", "that", "these", "those", "my", "your", "his", "her", "its",
        "our", "their", "some", "any", "every", "each", "no", "another", "most", "both", "all",
        "either", "neither",
    };

    private static readonly HashSet<string> Prepositions = new(StringComparer.Ordinal)
    {
        "in", "on", "at", "by", "for", "with", "from", "under", "over", "after", "before",
        "during", "about", "into", "onto", "through", "between", "among", "without", "within",
        "against", "across", "along", "below", "above", "beside", "beyond", "toward", "towards",
        "upon", "via",
    };

    /// <summary>
    /// Judges one item. <paramref name="cell"/> is the cell the item is joining, <em>without</em>
    /// this item: the rule is about the cell the item makes, not the one it found.
    /// </summary>
    public static GateOneVerdict Review(GateOneItem item, GateOneCell cell)
    {
        var refusals = new List<GateOneRefusal>();
        var notices = new List<GateOneNotice>();

        if (IsBlank(item.Explanation))
        {
            refusals.Add(new GateOneRefusal(
                GateOneRules.Explanation,
                "the item has no explanation. Every item carries a non-empty one, because a learner told only that they were wrong learns nothing (CNT-35)."));
        }

        var options = OptionsOf(item);
        if (options.Count == 0 && string.IsNullOrWhiteSpace(item.KeyLabel))
        {
            // A spoken or written task: gate one's rules are the multiple-choice rules, and a
            // rationale per option has no option to belong to.
            return new GateOneVerdict(refusals, notices);
        }

        if (options.Count != 4)
        {
            refusals.Add(new GateOneRefusal(
                GateOneRules.FourOptions,
                $"the item has {options.Count} option(s). A pool item has exactly four, which is what holds the 25% guessing floor still (CNT-06)."));
            return new GateOneVerdict(refusals, notices);
        }

        CheckDistinctness(options, refusals);
        CheckMetaOptions(options, refusals);

        var key = NormaliseKey(item.KeyLabel);
        if (key is null)
        {
            refusals.Add(new GateOneRefusal(
                GateOneRules.MissingKey,
                "four options and no key. Exactly one of A-D is correct (CNT-06)."));
            return new GateOneVerdict(refusals, notices);
        }

        var keyedIndex = Array.IndexOf(Labels, key);
        CheckKeyLength(options, keyedIndex, refusals);
        CheckParallelism(options, refusals, notices);
        CheckStemKeyLemma(item, options, keyedIndex, refusals, notices);
        CheckCellBalance(cell, key[0], refusals, notices);
        CheckRationales(item, refusals);

        return new GateOneVerdict(refusals, notices);
    }

    /// <summary>
    /// Reports a cell whose key distribution is outside the band, or a cell too small or too oddly
    /// sized to be split at all. An imbalanced cell is never silently accepted: it is the half of
    /// the rule no item can fix, and the only way it gets repaired is if somebody is told.
    /// </summary>
    public static IReadOnlyList<GateOneNotice> ReviewCell(GateOneCell cell)
    {
        if (cell.Total == 0)
        {
            return [];
        }

        if (!CanBeBalanced(cell.Total))
        {
            return [new GateOneNotice(GateOneRules.KeyCellBalance, CannotBalanceMessage(cell.Total))];
        }

        var outside = Labels
            .Where(label => !ShareInBand(cell.CountOf(label[0]), cell.Total))
            .ToList();
        if (outside.Count == 0)
        {
            return [];
        }

        var detail = string.Join(", ", outside.Select(label =>
            $"{label} holds {cell.CountOf(label[0])} of {cell.Total} keys ({Percent(cell.CountOf(label[0]), cell.Total)}%)"));
        return
        [
            new GateOneNotice(
                GateOneRules.KeyCellBalance,
                $"this cell's key distribution is outside the {KeyShareFloorPercent}-{KeyShareCeilingPercent}% band: {detail}. Key balance is per cell, so it is repaired across the cell rather than by any one item (CNT-35)."),
        ];
    }

    // ----- the rules -------------------------------------------------------------------------

    private static void CheckDistinctness(List<LabelledOption> options, List<GateOneRefusal> refusals)
    {
        for (var i = 0; i < options.Count; i++)
        {
            for (var j = i + 1; j < options.Count; j++)
            {
                // Every pair, not neighbouring pairs: the two options a learner confuses most can
                // sit at opposite ends of the list.
                var distance = Levenshtein(
                    NormaliseText(options[i].Text),
                    NormaliseText(options[j].Text));
                if (distance < MinimumPairwiseDistance)
                {
                    refusals.Add(new GateOneRefusal(
                        GateOneRules.OptionsTooSimilar,
                        $"options {options[i].Label} and {options[j].Label} are only {distance} character edit(s) apart; every pair must differ by more than {MinimumPairwiseDistance - 1} (CNT-35)."));
                    return;
                }
            }
        }
    }

    private static void CheckMetaOptions(List<LabelledOption> options, List<GateOneRefusal> refusals)
    {
        foreach (var option in options)
        {
            if (MetaOptionPattern.IsMatch(NormaliseForPattern(option.Text)))
            {
                refusals.Add(new GateOneRefusal(
                    GateOneRules.MetaOption,
                    $"option {option.Label} is a meta-option (\"{option.Text.Trim()}\"): it can be chosen without reading the stem or the stimulus, which measures test technique rather than the construct (CNT-35)."));
                return;
            }
        }
    }

    private static void CheckKeyLength(
        List<LabelledOption> options,
        int keyedIndex,
        List<GateOneRefusal> refusals)
    {
        var keyWords = WordCount(options[keyedIndex].Text);
        var distractorWords = options
            .Where((_, index) => index != keyedIndex)
            .Sum(option => WordCount(option.Text));

        // "|k - mean| <= 25% of the mean" with the mean's division by three multiplied out:
        // 4 * |3k - S| <= S. Integers only, and exactly 25% is inside the tolerance.
        if (4 * Math.Abs((3 * keyWords) - distractorWords) <= distractorWords)
        {
            return;
        }

        refusals.Add(new GateOneRefusal(
            GateOneRules.KeyLengthParity,
            $"the key has {keyWords} word(s) and the three distractors average {MeanWords(distractorWords)}: that is more than {KeyLengthTolerancePercent}% from the mean, which makes the longest option the answer far more often than chance (CNT-35)."));
    }

    private static void CheckParallelism(
        List<LabelledOption> options,
        List<GateOneRefusal> refusals,
        List<GateOneNotice> notices)
    {
        var shapes = options
            .Select(option => (option.Label, Shape: ShapeOf(option.Text)))
            .ToList();
        var determined = shapes.Where(s => s.Shape != OptionShape.Undetermined).ToList();
        var undetermined = shapes
            .Where(s => s.Shape == OptionShape.Undetermined)
            .Select(s => s.Label)
            .ToList();

        if (determined.Count == 0)
        {
            notices.Add(new GateOneNotice(
                GateOneRules.PartOfSpeechParallelism,
                $"none of options {string.Join(", ", undetermined)} opens with a word whose grammatical shape this check can decide, so parallel structure was not judged. Reported rather than guessed: a wrong guess refuses a legal item."));
            return;
        }

        var majority = determined
            .GroupBy(s => s.Shape)
            .OrderByDescending(group => group.Count())
            .First();
        var minority = determined.Where(s => s.Shape != majority.Key).Select(s => s.Label).ToList();

        if (minority.Count > 0)
        {
            var detail = string.Join(", ", minority.Select(label =>
                $"{label} opens as a {Describe(shapes.First(s => s.Label == label).Shape)}"));
            var others = string.Join(", ", determined.Where(s => s.Shape == majority.Key).Select(s => s.Label));
            refusals.Add(new GateOneRefusal(
                GateOneRules.PartOfSpeechParallelism,
                $"{detail}, while {others} open as a {Describe(majority.Key)}. Options must be grammatically parallel, or one stands out as the shape of the answer (CNT-35)."));
        }

        if (undetermined.Count > 0)
        {
            notices.Add(new GateOneNotice(
                GateOneRules.PartOfSpeechParallelism,
                $"option(s) {string.Join(", ", undetermined)} open with a word that decides no grammatical shape, so parallel structure was not judged for them. Reported rather than guessed: a wrong guess refuses a legal item."));
        }
    }

    /// <summary>
    /// The stem and the key sharing a content word that appears nowhere in the stimulus or the
    /// distractors lets a learner answer by matching words instead of reading. Inflections are folded,
    /// because "renews" in the passage does answer "renew" in the stem - refusing that would refuse
    /// real content.
    /// </summary>
    private static void CheckStemKeyLemma(
        GateOneItem item,
        List<LabelledOption> options,
        int keyedIndex,
        List<GateOneRefusal> refusals,
        List<GateOneNotice> notices)
    {
        var stemContent = ContentLemmas(item.Stem);
        if (stemContent.Count == 0)
        {
            return;
        }

        var keyContent = ContentLemmas(options[keyedIndex].Text);
        var shared = stemContent.Keys.Where(keyContent.ContainsKey).ToList();
        if (shared.Count == 0)
        {
            return;
        }

        // With no stimulus text the rule cannot distinguish "the stem and key repeat a word the
        // passage also uses" from "they repeat a word nothing else uses": silence is not evidence, and
        // refusing on it would refuse every standalone cloze and every audio item whose transcript is
        // not carried. Reported instead, so the gap is visible rather than waived.
        if (IsBlank(item.Stimulus))
        {
            notices.Add(new GateOneNotice(
                GateOneRules.UniqueStemKeyLemma,
                "the item carries no stimulus text, so the shared-word rule was not judged: a stem and key that repeat a word cannot be told from a legitimate one without the passage or transcript to compare against."));
            return;
        }

        var elsewhere = new HashSet<string>(AllLemmas(item.Stimulus), StringComparer.Ordinal);
        foreach (var option in options.Where((_, index) => index != keyedIndex))
        {
            elsewhere.UnionWith(AllLemmas(option.Text));
        }

        var unique = shared.Where(lemma => !elsewhere.Contains(lemma)).ToList();
        if (unique.Count == 0)
        {
            return;
        }

        var words = string.Join(", ", unique.Select(lemma => $"\"{stemContent[lemma]}\""));
        refusals.Add(new GateOneRefusal(
            GateOneRules.UniqueStemKeyLemma,
            $"{words} appear(s) in both the stem and the key and nowhere in the stimulus or the distractors, so the item can be answered by matching words instead of reading (CNT-35)."));
    }

    private static void CheckCellBalance(
        GateOneCell cell,
        char keyedLabel,
        List<GateOneRefusal> refusals,
        List<GateOneNotice> notices)
    {
        if (cell.Total == 0)
        {
            // The first item in a cell: there is no distribution to balance yet, and reporting that
            // on every first item is noise rather than a finding.
            return;
        }

        var after = cell.Add(keyedLabel);
        if (!CanBeBalanced(after.Total))
        {
            notices.Add(new GateOneNotice(GateOneRules.KeyCellBalance, CannotBalanceMessage(after.Total)));
            return;
        }

        if (AllLettersInBand(after))
        {
            return;
        }

        // Refused only when keying it differently would repair the cell. When no letter would, the
        // cell is broken by its history rather than by this choice, and refusing the item for a state
        // its author cannot see would be a false refusal - so it is reported instead.
        var alternatives = Labels.Where(label => AllLettersInBand(cell.Add(label[0]))).ToList();
        if (alternatives.Count > 0)
        {
            refusals.Add(new GateOneRefusal(
                GateOneRules.KeyCellBalance,
                $"keying this item {keyedLabel} puts {keyedLabel} at {Percent(after.CountOf(keyedLabel), after.Total)}% of the cell's {after.Total} keys, outside the {KeyShareFloorPercent}-{KeyShareCeilingPercent}% band. Keying it {string.Join(" or ", alternatives)} would keep the cell inside the band (CNT-35)."));
            return;
        }

        notices.Add(new GateOneNotice(
            GateOneRules.KeyCellBalance,
            $"the cell is outside the {KeyShareFloorPercent}-{KeyShareCeilingPercent}% band whichever letter this item takes ({Detail(after)}), so this item is not refused for a state it did not cause. Reported because an imbalanced cell is never silently accepted (CNT-35)."));
    }

    private static void CheckRationales(GateOneItem item, List<GateOneRefusal> refusals)
    {
        var missing = new List<string>();
        foreach (var (label, rationale) in new[]
                 {
                     ("A", item.RationaleA), ("B", item.RationaleB),
                     ("C", item.RationaleC), ("D", item.RationaleD),
                 })
        {
            if (IsBlank(rationale))
            {
                missing.Add(label);
            }
        }

        if (missing.Count > 0)
        {
            refusals.Add(new GateOneRefusal(
                GateOneRules.OptionRationale,
                $"option(s) {string.Join(", ", missing)} carry no rationale. Every option - the key and each distractor - says why it is what it is, because the rationale is what teaches after the answer (CNT-35)."));
        }
    }

    // ----- helpers ---------------------------------------------------------------------------

    private static bool AllLettersInBand(GateOneCell cell) =>
        Labels.All(label => ShareInBand(cell.CountOf(label[0]), cell.Total));

    private static bool ShareInBand(int count, int total) =>
        total > 0
        && count * 100 >= total * KeyShareFloorPercent
        && count * 100 <= total * KeyShareCeilingPercent;

    /// <summary>
    /// Whether four integers can each land inside [20%, 30%] and still sum to the total. Ten keys
    /// can (3/3/2/2); eleven cannot, and a cell that cannot be balanced must be reported as such
    /// rather than as an imbalance nobody could fix.
    /// </summary>
    private static bool CanBeBalanced(int total)
    {
        if (total <= 0)
        {
            return false;
        }

        var low = (total * KeyShareFloorPercent + 99) / 100;
        var high = total * KeyShareCeilingPercent / 100;
        return low <= high && 4 * low <= total && total <= 4 * high;
    }

    private static string CannotBalanceMessage(int total) =>
        $"key balance cannot be judged for a cell of {total}: four letters cannot each hold "
        + $"{KeyShareFloorPercent}-{KeyShareCeilingPercent}% of {total} keys. Reported as not yet "
        + "assessable rather than as an imbalance, because no key choice could fix it (CNT-35).";

    private static string Detail(GateOneCell cell) =>
        string.Join(", ", Labels.Select(label => $"{label} {cell.CountOf(label[0])}/{cell.Total}"));

    private static int Percent(int count, int total) =>
        total == 0 ? 0 : (int)Math.Round(count * 100.0 / total);

    private static string MeanWords(int distractorWordSum) =>
        Math.Round(distractorWordSum / 3.0, 1).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Describe(OptionShape shape) =>
        shape == OptionShape.Nominal ? "noun phrase" : "prepositional phrase";

    private enum OptionShape
    {
        Undetermined,
        Nominal,
        Prepositional,
    }

    private static OptionShape ShapeOf(string text)
    {
        var first = FirstWord(text);
        if (first.Length == 0)
        {
            return OptionShape.Undetermined;
        }

        if (Determiners.Contains(first))
        {
            return OptionShape.Nominal;
        }

        return Prepositions.Contains(first) ? OptionShape.Prepositional : OptionShape.Undetermined;
    }

    private static string FirstWord(string text)
    {
        var tokens = text.ToLowerInvariant().Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 0
            ? string.Empty
            : tokens[0].Trim('.', ',', ';', ':', '!', '?', '"', '\'', '(', ')');
    }

    private static int WordCount(string text) =>
        text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static List<LabelledOption> OptionsOf(GateOneItem item) =>
        new[]
            {
                ("A", item.OptionA), ("B", item.OptionB), ("C", item.OptionC), ("D", item.OptionD),
            }
            .Where(option => !IsBlank(option.Item2))
            .Select(option => new LabelledOption(option.Item1, option.Item2!.Trim()))
            .ToList();

    private static string? NormaliseKey(string? keyLabel)
    {
        var key = keyLabel?.Trim().ToUpperInvariant();
        return key is "A" or "B" or "C" or "D" ? key : null;
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>Trimmed, lowercased, whitespace-collapsed: what the character distance compares.</summary>
    private static string NormaliseText(string text) =>
        string.Join(' ', text.ToLowerInvariant().Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Lowercased, punctuation reduced to spaces, so "Neither A nor B." and "neither a nor b" agree.</summary>
    private static string NormaliseForPattern(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>Content lemmas of a text, folded, keeping one original form per lemma for the message.</summary>
    private static Dictionary<string, string> ContentLemmas(string? text)
    {
        var content = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var word in Words(text))
        {
            if (StopWords.Contains(word))
            {
                continue;
            }

            var lemma = Fold(word);
            if (lemma.Length >= 2)
            {
                content.TryAdd(lemma, word);
            }
        }

        return content;
    }

    private static IEnumerable<string> AllLemmas(string? text) =>
        Words(text).Select(Fold);

    private static IEnumerable<string> Words(string? text) =>
        IsBlank(text)
            ? []
            : Regex.Split(text!.ToLowerInvariant(), "[^a-z]+").Where(word => word.Length > 0);

    /// <summary>
    /// Inflection folding, not stemming: a few English suffixes, applied only where they are
    /// unambiguous. It exists so "permits" in the passage matches "permit" in the stem. Over-matching
    /// only makes the gate more permissive, which is the safe direction for a refusal.
    /// </summary>
    private static string Fold(string word)
    {
        if (word.Length <= 4)
        {
            return word;
        }

        if (word.EndsWith("ies", StringComparison.Ordinal))
        {
            return word[..^3] + "y";
        }

        foreach (var suffix in new[] { "ing", "ed", "es", "s" })
        {
            if (word.Length - suffix.Length >= 3 && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                var stem = word[..^suffix.Length];
                return stem.Length > 3 && stem[^1] == stem[^2] ? stem[..^1] : stem;
            }
        }

        return word;
    }

    /// <summary>
    /// Wagner-Fischer edit distance over characters. Case and collapsed whitespace are normalised
    /// first: two options that differ only in capitals are one option written twice, and a
    /// case-sensitive distance would accept them.
    /// </summary>
    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>An option that survived <see cref="OptionsOf"/>: it has a label and non-blank text.</summary>
    private sealed record LabelledOption(string Label, string Text);
}
