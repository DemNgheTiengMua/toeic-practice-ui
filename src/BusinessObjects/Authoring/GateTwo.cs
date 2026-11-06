using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BusinessObjects;

/// <summary>
/// #195: gate two's rule names, in the same shape as gate one's. A refusal carries a code so a
/// caller can branch without reading English, and <see cref="GateTwoRefusal.Message"/> says the
/// same thing in words for the author who has to fix the item.
/// </summary>
public static class GateTwoRules
{
    /// <summary>D0.2: the stimulus's word count is inside its band's modality envelope.</summary>
    public const string WordCount = "gate2.length.word_count";

    /// <summary>D0.2: the Flesch-Kincaid grade is inside the band's reading envelope.</summary>
    public const string FleschKincaid = "gate2.readability.fk";

    /// <summary>D0.1/D0.2: mean length of T-unit, in words, inside the band's envelope.</summary>
    public const string MeanTUnitLength = "gate2.syntax.mltu";

    /// <summary>D0.1: clauses per T-unit inside the band's range.</summary>
    public const string ClausesPerTUnit = "gate2.syntax.clauses";

    /// <summary>D0.1: the share of tokens inside the band's list.</summary>
    public const string LexicalCoverage = "gate2.lexis.coverage";

    /// <summary>D17: an independent transcription differs from the written script.</summary>
    public const string WordErrorRate = "gate2.audio.wer";

    /// <summary>D17: no independent transcription was obtained, so the 0.0% rule was never applied.</summary>
    public const string TranscriptUnavailable = "gate2.audio.unverified";

    /// <summary>D17: audio with no written script - the transcript a reviewer reads does not exist.</summary>
    public const string MissingScript = "gate2.audio.script";

    /// <summary>Not a refusal: D0.2 states no bound for this text, so nothing was measured.</summary>
    public const string NotProfiled = "gate2.text.not_profiled";
}

/// <summary>#195: which column of D0.2 an item is judged against. Spoken and written input differ.</summary>
public enum GateTwoModality
{
    Listening,
    Reading,

    /// <summary>
    /// Speaking and writing stimuli. D0.2's columns for them are <em>learner output</em> demands,
    /// not authored-text properties, so gate two does not profile them; the rubric does.
    /// </summary>
    Productive,
}

/// <summary>
/// #195: whether the text is a stimulus (D0.2 owns its length and readability) or a standalone
/// slot such as <c>sentence_cloze</c> (D0.4 owns those: 15-45 words, and no FK bound).
/// </summary>
public enum GateTwoTextKind
{
    Stimulus,
    SentenceSlot,
}

/// <summary>
/// #195: one numeric bound. Inclusive at both ends unless it says otherwise, because D0.1 and D0.2
/// write three of their endpoints as strict ("&gt;20.0", "&gt;2.0", "&gt;14.0") and a range that
/// quietly widened those to "&gt;=" admits a value the specification refuses.
/// </summary>
public sealed record GateTwoBound(
    double Minimum,
    double Maximum,
    bool IncludesMinimum = true,
    bool IncludesMaximum = true)
{
    public static GateTwoBound Between(double minimum, double maximum) => new(minimum, maximum);

    /// <summary>"&gt;x" as D0.1 writes it: the endpoint itself is outside.</summary>
    public static GateTwoBound Above(double minimum) => new(minimum, double.PositiveInfinity, IncludesMinimum: false);

    /// <summary>"&lt;=x" as D0.1 writes it: the endpoint itself is inside.</summary>
    public static GateTwoBound AtMost(double maximum) => new(double.NegativeInfinity, maximum);

    public bool Contains(double value) =>
        (IncludesMinimum ? value >= Minimum : value > Minimum)
        && (IncludesMaximum ? value <= Maximum : value < Maximum);

    /// <summary>
    /// D0.2's listening envelope is the reading one shortened by a fixed share. An unbounded end
    /// stays unbounded, and the finite end moves: C2's reading "&gt;20.0" becomes "&gt;16.5", which
    /// is what "15-20% shorter MLTU" means at a band whose floor is a strict one.
    /// </summary>
    public GateTwoBound ShortenedBy(double share) => this with
    {
        Minimum = double.IsInfinity(Minimum) ? Minimum : Minimum * (1 - share),
        Maximum = double.IsInfinity(Maximum) ? Maximum : Maximum * (1 - share),
    };

    /// <summary>The finite end of the bound, for a caller comparing two bands' envelopes.</summary>
    public double FiniteMaximum => double.IsInfinity(Maximum) ? Minimum : Maximum;

    public string Describe()
    {
        if (double.IsInfinity(Maximum))
        {
            return $"{(IncludesMinimum ? "at least " : "more than ")}{Compact(Minimum)}";
        }

        if (double.IsInfinity(Minimum))
        {
            return $"at most {Compact(Maximum)}";
        }

        return $"{Compact(Minimum)}-{Compact(Maximum)}";
    }

    private static string Compact(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>
/// #195: the whole of one band's profile. This record and <see cref="GateTwo.Profiles"/> are the
/// only places these numbers are written, and <c>GateTwoTests</c> asserts every one of them against
/// the D0.1 and D0.2 tables by parsing the specification - so a threshold edited here without the
/// spec, or the spec without here, fails a test rather than drifting.
/// </summary>
public sealed record GateTwoBounds(
    int MinimumWords,
    int MaximumWords,
    GateTwoBound MeanTUnitLength,
    GateTwoBound? ClausesPerTUnit,
    GateTwoBound? FleschKincaid,
    int? LexicalListSize,
    double? MinimumLexicalCoverage,
    int AwlSublistCeiling)
{
    /// <summary>"Unrestricted" (C2) is a claim, not an absent check: nothing is refused for lexis.</summary>
    public bool HasLexicalBound => MinimumLexicalCoverage is not null;
}

/// <summary>#195: what a text actually measures, before any bound is applied.</summary>
public sealed record GateTwoText(
    int Words,
    int Sentences,
    int Syllables,
    int TUnits,
    int Clauses,
    int InListTokens)
{
    /// <summary>
    /// D0.1's MLTU: words per <b>T-unit</b>, not per sentence. The two are the same number only
    /// until the text coordinates or subordinates a clause, and then they are not.
    /// </summary>
    public double MeanTUnitLength => TUnits == 0 ? 0 : (double)Words / TUnits;

    public double ClausesPerTUnit => TUnits == 0 ? 0 : (double)Clauses / TUnits;

    /// <summary>
    /// Flesch-Kincaid grade: <c>0.39 * words/sentences + 11.8 * syllables/words - 15.59</c>. Its
    /// denominator is <b>sentences</b>, which is exactly why MLTU cannot borrow it.
    /// </summary>
    public double FleschKincaid =>
        Words == 0 || Sentences == 0
            ? 0
            : (0.39 * Words / Sentences) + (11.8 * Syllables / Words) - 15.59;

    /// <summary>D0.1's "share of tokens inside the list": every token is a denominator unit.</summary>
    public double LexicalCoverage => Words == 0 ? 1 : (double)InListTokens / Words;
}

/// <summary>#195: one rule broken, named by code and by the number that broke it.</summary>
public sealed record GateTwoRefusal(string Rule, string Message);

/// <summary>#195: something the author must be told even though the item is not refused.</summary>
public sealed record GateTwoNotice(string Rule, string Message);

/// <summary>#195: one measurement gate two took, so the admin screen can show the number it judged.</summary>
public sealed record GateTwoMeasurement(string Name, string Value);

/// <summary>#195: gate two's verdict. A notice never blocks; only a refusal does.</summary>
public sealed record GateTwoVerdict(
    IReadOnlyList<GateTwoRefusal> Refusals,
    IReadOnlyList<GateTwoNotice> Notices,
    IReadOnlyList<GateTwoMeasurement> Measurements)
{
    public bool Accepted => Refusals.Count == 0;
}

/// <summary>
/// #195: an independent transcription of an audio item's file. <b>Unavailable is a case, not a
/// null:</b> D17's rule is word error rate 0.0%, and a verifier that never ran cannot satisfy it.
/// Modelling "we did not check" as a boolean would let a caller default to passing.
/// </summary>
public abstract record GateTwoAsr
{
    private GateTwoAsr()
    {
    }

    /// <summary>What an independent model heard, transcribed.</summary>
    public sealed record Transcribed(string Text) : GateTwoAsr;

    /// <summary>Why no transcription was obtained - a missing credential, an unreachable service.</summary>
    public sealed record Unavailable(string Reason) : GateTwoAsr;
}

/// <summary>
/// #195: what a word-error comparison found. <b>Edits, not a percentage, is the fact:</b> D17's
/// threshold is 0.0%, and a rate rounded to one decimal place turns one error in 160 words into
/// 0.0% - the value the rule accepts.
/// </summary>
public sealed record GateTwoWordError(
    double Rate,
    int Substitutions,
    int Deletions,
    int Insertions,
    int HeardWords,
    int ReferenceWords,
    string Differences)
{
    public int Edits => Substitutions + Deletions + Insertions;
}

/// <summary>#195: one item as authored, in the terms gate two measures.</summary>
public sealed record GateTwoItem(
    string? Text,
    CefrLevel Band,
    GateTwoModality Modality,
    GateTwoTextKind Kind = GateTwoTextKind.Stimulus,
    GateTwoAsr? Asr = null);

/// <summary>#195: one line of the embedded word list.</summary>
public sealed record GateTwoWord(string Headword, CefrLevel Level, int Rank);

/// <summary>
/// #195: gate two - linguistic and acoustic profile (D0.1, D0.2, D17). A pure function: no
/// database, no clock, no file, no network, and no AI call (level classification is gate four).
/// <para>
/// <b>How far the approximation goes.</b> Three of D0.1's quantities need a parser this solution
/// does not have, and each is computed by a stated heuristic rather than left to a guess:
/// <list type="bullet">
/// <item><b>T-unit segmentation</b> is marker-based, and its two halves are counted differently.
/// A <em>clause</em> boundary is a sentence end, a semicolon, a comma, or a subordinator/relative
/// marker (<i>because, when, if, although, which, who</i>...). A <em>T-unit</em> boundary is a
/// sentence end, a semicolon, or a coordinator (<i>and, but, or, so, yet, nor</i>) whose right-hand
/// side is a clause: at least two tokens, a finite verb somewhere, and no finite verb first
/// (<i>and the dog ran</i> counts; <i>and opened the office</i> is a shared subject and <i>and milk
/// and eggs</i> is a noun list). It therefore <b>under-counts</b> a coordinated clause whose finite
/// verb is an irregular form absent from the list, and <b>over-counts</b> a coordinated subject
/// (<i>and the staff were tired</i> is not a second T-unit). Both err towards refusing a legitimate
/// item rather than passing a defective one, which is the only acceptable direction here.</item>
/// <item><b>Syllables</b> are vowel groups with a silent-final-e correction, not a pronouncing
/// dictionary. Flesch-Kincaid is a proxy in the first place; this makes it a proxy of a proxy, and
/// a text of jargon will read easier than it is.</item>
/// <item><b>Inflection folding</b> is a suffix rule (<i>-s, -es, -ed, -ing, -ies</i>), so an
/// irregular form (<i>bought</i> for <i>buy</i>) counts as off-list and makes coverage a floor
/// rather than an estimate.</item>
/// </list>
/// <b>What gate two does not measure.</b> D0.1's "≤3% B2 tokens" at B1 is a second lexical
/// constraint this ticket did not ask for and the profile does not carry. D0.2's words-per-minute
/// needs a duration for the stimulus, which <c>Stimuli</c> does not store, and is likewise out of
/// this gate; the ASR rule below is the acoustic half instead.
/// </para>
/// </summary>
public static class GateTwo
{
    /// <summary>
    /// D0.2: "roughly 15-20% shorter MLTU" for listening. One share is chosen rather than a range,
    /// because a range would mean two items at the same band judged by different arithmetic. The
    /// gap is asserted to stay inside the specification's window by <c>GateTwoTests</c>.
    /// </summary>
    public const double ListeningSubordinationGap = 0.175;

    /// <summary>D0.4: a standalone <c>sentence_cloze</c> stimulus is 15-45 words.</summary>
    public const int SentenceSlotMinimumWords = 15;

    public const int SentenceSlotMaximumWords = 45;

    /// <summary>D17: one mispronounced or clipped word breaks every learner who reads the script.</summary>
    public const double MaximumWordErrorRate = 0.0;

    /// <summary>
    /// D0.1 read down its "Lexical profile" column and D0.2 down "Listening" and "Reading". The
    /// listening length and syntax come from the reading values by the gap above, so the two
    /// envelopes cannot be edited out of step with each other.
    /// </summary>
    private static readonly IReadOnlyDictionary<CefrLevel, Envelope> Table =
        new Dictionary<CefrLevel, Envelope>
        {
            //        listening    reading     FK low  FK high  list  share  AWL
            [CefrLevel.A1] = new((40, 65), (60, 100), 1.5, 3.0, 800, 95.0, 0),
            [CefrLevel.A2] = new((60, 95), (120, 180), 3.5, 5.5, 1800, 92.0, 0),
            [CefrLevel.B1] = new((110, 185), (220, 300), 6.0, 8.0, 3000, 90.0, 0),
            [CefrLevel.B2] = new((150, 240), (320, 450), 8.5, 11.0, 5000, 88.0, 5),
            [CefrLevel.C1] = new((240, 340), (450, 600), 11.5, 14.0, 7769, 85.0, 10),
            // D0.2 writes C2's FK as ">14.0": a floor, not a range, hence FkLow == FkHigh.
            [CefrLevel.C2] = new((260, 360), (500, 650), 14.0, 14.0, null, null, 0),
        };

    /// <summary>One row of D0.2 and D0.1's lexical column, as the specification states them.</summary>
    private sealed record Envelope(
        (int Low, int High) Listening,
        (int Low, int High) Reading,
        double FkLow,
        double FkHigh,
        int? LexicalListSize,
        double? MinimumLexicalCoverage,
        int AwlSublistCeiling);

    /// <summary>D0.1's MLTU column, and its clauses-per-T-unit column where that row states one.</summary>
    private static readonly IReadOnlyDictionary<CefrLevel, (GateTwoBound Mltu, GateTwoBound? Clauses)> Syntax =
        new Dictionary<CefrLevel, (GateTwoBound, GateTwoBound?)>
        {
            [CefrLevel.A1] = (GateTwoBound.Between(5.0, 7.5), null),
            [CefrLevel.A2] = (GateTwoBound.Between(7.5, 10.5), GateTwoBound.AtMost(1.3)),
            [CefrLevel.B1] = (GateTwoBound.Between(10.5, 13.5), GateTwoBound.Between(1.3, 1.6)),
            [CefrLevel.B2] = (GateTwoBound.Between(13.5, 17.0), GateTwoBound.Between(1.6, 2.0)),
            [CefrLevel.C1] = (GateTwoBound.Between(17.0, 21.0), GateTwoBound.Above(2.0)),
            // C2 states MLTU ">20.0" and names structural devices instead of a clause ratio.
            [CefrLevel.C2] = (GateTwoBound.Above(20.0), null),
        };

    /// <summary>
    /// The profile for one band and one modality, or <c>null</c> where D0.2 states none - which is
    /// speaking and writing, whose columns are learner-output demands.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The reportable floor, which content may not be authored at, or a band the table omits.
    /// </exception>
    public static GateTwoBounds? Profile(CefrLevel band, GateTwoModality modality)
    {
        if (band.IsReportableFloor)
        {
            throw new ArgumentException(
                $"'{band.Code}' is the reportable floor; content is authored at A1-C2 only.", nameof(band));
        }

        if (modality == GateTwoModality.Productive)
        {
            return null;
        }

        if (!Table.TryGetValue(band, out var row) || !Syntax.TryGetValue(band, out var syntax))
        {
            throw new ArgumentException($"'{band.Code}' carries no gate two profile.", nameof(band));
        }

        var listening = modality == GateTwoModality.Listening;
        var words = listening ? row.Listening : row.Reading;
        var fleschKincaid = row.FkLow == row.FkHigh
            ? GateTwoBound.Above(row.FkLow)      // "FK >14.0" at C2
            : GateTwoBound.Between(row.FkLow, row.FkHigh);

        return new GateTwoBounds(
            words.Low,
            words.High,
            listening ? syntax.Mltu.ShortenedBy(ListeningSubordinationGap) : syntax.Mltu,
            syntax.Clauses,
            // D0.2 bounds readability for written input. Listening carries wpm instead, and wpm
            // needs a duration the stimulus does not store.
            listening ? null : fleschKincaid,
            row.LexicalListSize,
            row.MinimumLexicalCoverage,
            row.AwlSublistCeiling);
    }

    /// <summary>Measures a text in gate two's terms. Coverage is resolved against <paramref name="band"/>.</summary>
    public static GateTwoText Measure(string? text, CefrLevel band)
    {
        var tokens = Tokens(text);
        if (tokens.Count == 0)
        {
            return new GateTwoText(0, 0, 0, 0, 0, 0);
        }

        var sentences = 0;
        var tUnits = 0;
        var clauses = 0;
        var syllables = 0;

        foreach (var sentence in Sentences(text!))
        {
            var units = Segmentation(sentence);
            sentences++;
            tUnits += units.TUnits;
            clauses += units.Clauses;
        }

        foreach (var token in tokens)
        {
            syllables += Syllables(token);
        }

        var inList = tokens.Count(token => Covers(token, band));
        return new GateTwoText(tokens.Count, sentences, syllables, tUnits, clauses, inList);
    }

    /// <summary>
    /// Whether a token is inside the word list at <paramref name="band"/>, counting the band's AWL
    /// sublists and folding a simple inflection to its headword. Public because the coverage rule is
    /// the one place a wrong denominator is invisible: an author needs to be able to ask.
    /// <para>
    /// A band with no list - C2, whose profile is "unrestricted" - covers every token. The other
    /// reading is that it covers none, which reports C2 coverage as 0% and would refuse the band's
    /// own target register if the bound were ever switched on; <c>Covers</c> is the seam an author
    /// calls, so it answers about the band rather than about a list that does not exist.
    /// </para>
    /// </summary>
    public static bool Covers(string token, CefrLevel band)
    {
        if (!Table.TryGetValue(band, out var row) || row.LexicalListSize is null)
        {
            return true;
        }

        var word = Normalise(token);
        if (word.Length == 0)
        {
            return true;   // punctuation and digits are not a vocabulary claim either way
        }

        foreach (var candidate in Foldings(word))
        {
            if (IsInList(candidate, row.LexicalListSize, row.AwlSublistCeiling))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Judges one item. Every refusal names the measurement that failed, because an author told
    /// only "out of band" uploads the same item again.
    /// </summary>
    public static GateTwoVerdict Review(GateTwoItem item)
    {
        var refusals = new List<GateTwoRefusal>();
        var notices = new List<GateTwoNotice>();
        var measurements = new List<GateTwoMeasurement>();

        if (item.Modality == GateTwoModality.Productive)
        {
            notices.Add(new GateTwoNotice(
                GateTwoRules.NotProfiled,
                "a speaking or writing prompt is not profiled by gate two: D0.2's columns for those skills are learner-output demands, which the rubric measures, not properties of the prompt."));
            return new GateTwoVerdict(refusals, notices, measurements);
        }

        var text = item.Text?.Trim();
        if (item.Modality == GateTwoModality.Listening && string.IsNullOrEmpty(text))
        {
            refusals.Add(new GateTwoRefusal(
                GateTwoRules.MissingScript,
                "this listening stimulus has audio and no script. The transcript is what a learner reads in review, so its absence is a defect of its own (D17)."));
            return new GateTwoVerdict(refusals, notices, measurements);
        }

        if (string.IsNullOrEmpty(text))
        {
            refusals.Add(new GateTwoRefusal(
                GateTwoRules.WordCount,
                $"the text holds 0 words; {item.Band.Code} {Describe(item.Modality)} allows {Counts(item)}."));
            return new GateTwoVerdict(refusals, notices, measurements);
        }

        var measured = Measure(text, item.Band);
        var profile = Profile(item.Band, item.Modality)!;

        measurements.Add(new GateTwoMeasurement("words", measured.Words.ToString(CultureInfo.InvariantCulture)));
        measurements.Add(new GateTwoMeasurement("sentences", measured.Sentences.ToString(CultureInfo.InvariantCulture)));
        measurements.Add(new GateTwoMeasurement("t_units", measured.TUnits.ToString(CultureInfo.InvariantCulture)));
        measurements.Add(new GateTwoMeasurement("clauses", measured.Clauses.ToString(CultureInfo.InvariantCulture)));
        measurements.Add(new GateTwoMeasurement("mltu", Round(measured.MeanTUnitLength)));
        measurements.Add(new GateTwoMeasurement("clauses_per_t_unit", Round(measured.ClausesPerTUnit)));
        measurements.Add(new GateTwoMeasurement("flesch_kincaid", Round(measured.FleschKincaid)));
        measurements.Add(new GateTwoMeasurement("coverage", Round(measured.LexicalCoverage * 100)));

        CheckWordCount(item, measured, refusals);

        // The acoustic rule is about the recording, not about the shape of the text, so it is not
        // inside the sentence-slot branch below: a listening item is checked against its audio
        // whatever kind its text is.
        if (item.Modality == GateTwoModality.Listening)
        {
            CheckTranscription(item, text, refusals);
        }

        if (item.Kind == GateTwoTextKind.SentenceSlot)
        {
            // D0.4's whole envelope for a standalone cloze is its 15-45 word slot. The band's MLTU
            // envelope cannot apply to it: a one-sentence cloze's MLTU is its word count, so every
            // band's range would refuse every legal cloze. The lexical profile still does apply.
            CheckLexical(item, measured, profile, refusals);
            return new GateTwoVerdict(refusals, notices, measurements);
        }

        CheckMeanTUnitLength(item, measured, profile, refusals);
        CheckClausesPerTUnit(item, measured, profile, refusals);
        CheckFleschKincaid(item, measured, profile, refusals);
        CheckLexical(item, measured, profile, refusals);

        return new GateTwoVerdict(refusals, notices, measurements);
    }

    // ----- the rules -----------------------------------------------------------------------

    private static void CheckWordCount(GateTwoItem item, GateTwoText measured, List<GateTwoRefusal> refusals)
    {
        var (minimum, maximum, source) = item.Kind == GateTwoTextKind.SentenceSlot
            ? (SentenceSlotMinimumWords, SentenceSlotMaximumWords, "D0.4's standalone sentence_cloze slot")
            : (Profile(item.Band, item.Modality)!.MinimumWords, Profile(item.Band, item.Modality)!.MaximumWords,
                $"D0.2's {Describe(item.Modality)} envelope for {item.Band.Code}");

        if (measured.Words < minimum || measured.Words > maximum)
        {
            refusals.Add(new GateTwoRefusal(
                GateTwoRules.WordCount,
                $"the text is {measured.Words} words; {source} allows {minimum}-{maximum}. Length is not a style preference here: a stimulus outside its band's envelope cannot support an item at that band (D0.3)."));
        }
    }

    private static void CheckMeanTUnitLength(
        GateTwoItem item, GateTwoText measured, GateTwoBounds profile, List<GateTwoRefusal> refusals)
    {
        var bound = profile.MeanTUnitLength;
        if (bound.Contains(measured.MeanTUnitLength))
        {
            return;
        }

        var where = measured.MeanTUnitLength < bound.Minimum ? "below" : "above";
        refusals.Add(new GateTwoRefusal(
            GateTwoRules.MeanTUnitLength,
            $"the mean length of T-unit is {Round(measured.MeanTUnitLength)} words ({measured.Words} words in {measured.TUnits} T-unit(s)), {where} {item.Band.Code} {Describe(item.Modality)}'s {bound.Describe()}. Mean sentence length is a different number: this text has {measured.Sentences} sentence(s) (D0.1)."));
    }

    private static void CheckClausesPerTUnit(
        GateTwoItem item, GateTwoText measured, GateTwoBounds profile, List<GateTwoRefusal> refusals)
    {
        if (profile.ClausesPerTUnit is not { } bound || bound.Contains(measured.ClausesPerTUnit))
        {
            return;
        }

        refusals.Add(new GateTwoRefusal(
            GateTwoRules.ClausesPerTUnit,
            $"the text carries {Round(measured.ClausesPerTUnit)} clause(s) per T-unit ({measured.Clauses} clause(s) in {measured.TUnits} T-unit(s)); {item.Band.Code} allows {bound.Describe()}. Clause density is what separates subordination from sentence length (D0.1)."));
    }

    private static void CheckFleschKincaid(
        GateTwoItem item, GateTwoText measured, GateTwoBounds profile, List<GateTwoRefusal> refusals)
    {
        if (profile.FleschKincaid is not { } bound || bound.Contains(measured.FleschKincaid))
        {
            return;
        }

        var where = measured.FleschKincaid < bound.Minimum ? "below" : "above";
        refusals.Add(new GateTwoRefusal(
            GateTwoRules.FleschKincaid,
            $"the Flesch-Kincaid grade is {Round(measured.FleschKincaid)} ({measured.Words} words in {measured.Sentences} sentence(s), {measured.Syllables} syllable(s)), {where} {item.Band.Code} reading's {bound.Describe()} (D0.2)."));
    }

    private static void CheckLexical(
        GateTwoItem item, GateTwoText measured, GateTwoBounds profile, List<GateTwoRefusal> refusals)
    {
        if (!profile.HasLexicalBound || measured.LexicalCoverage * 100 >= profile.MinimumLexicalCoverage)
        {
            return;
        }

        var offList = Tokens(item.Text)
            .Where(token => !Covers(token, item.Band))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();

        var listName = profile.AwlSublistCeiling > 0
            ? $"the first {profile.LexicalListSize} headwords plus AWL sublists 1-{profile.AwlSublistCeiling}"
            : $"the first {profile.LexicalListSize} headwords";

        refusals.Add(new GateTwoRefusal(
            GateTwoRules.LexicalCoverage,
            $"{measured.InListTokens} of {measured.Words} tokens ({Round(measured.LexicalCoverage * 100)}%) are inside {listName}; {item.Band.Code} requires at least {Round(profile.MinimumLexicalCoverage!.Value)}%. Every token counts, function words included. Off-list: {string.Join(", ", offList.Select(w => $"\"{w}\""))}{(offList.Count == 8 ? ", ..." : string.Empty)}."));
    }

    private static void CheckTranscription(GateTwoItem item, string script, List<GateTwoRefusal> refusals)
    {
        if (item.Asr is not GateTwoAsr.Transcribed transcribed)
        {
            var reason = item.Asr is GateTwoAsr.Unavailable unavailable
                ? unavailable.Reason
                : "no independent transcription was requested";

            refusals.Add(new GateTwoRefusal(
                GateTwoRules.TranscriptUnavailable,
                $"the audio was not verified against its script ({reason}). D17 requires an independent transcription to agree at a word error rate of {MaximumWordErrorRate * 100:0.#}%, and a check that did not run cannot pass: a single mispronounced or clipped word breaks every learner who reads the transcript."));
            return;
        }

        var error = WordErrorRate(transcribed.Text, script);
        if (error.Rate <= MaximumWordErrorRate)
        {
            return;
        }

        var detail = error.Insertions == 0 && error.Substitutions == 0
            ? $"{error.Deletions} word(s) of the script were not in the recording"
            : error.Deletions == 0 && error.Insertions == 0
                ? $"{error.Substitutions} word(s) were heard differently"
                : $"{error.Substitutions} word(s) heard differently, {error.Deletions} missing, {error.Insertions} extra";

        refusals.Add(new GateTwoRefusal(
            GateTwoRules.WordErrorRate,
            $"the independent transcription disagrees with the written script at a word error rate of {Round(error.Rate * 100)}% ({error.Edits} edit(s) over {error.ReferenceWords} reference word(s)); D17 requires {MaximumWordErrorRate * 100:0.#}%. {detail}: {error.Differences}."));
    }

    /// <summary>
    /// Word error rate over token sequences: substitutions + deletions + insertions, divided by the
    /// reference's word count. Case and punctuation are normalised first, because a transcription
    /// that differs only in capitals heard the same words; a one-character spelling difference is a
    /// different word heard. The edits are counted, not rounded: rounding a 1-in-160 error to one
    /// decimal place gives 0.0%, and D17 refuses it.
    /// </summary>
    public static GateTwoWordError WordErrorRate(string hypothesis, string reference)
    {
        var heard = Tokens(hypothesis).Select(Normalise).Where(word => word.Length > 0).ToList();
        var script = Tokens(reference).Select(Normalise).Where(word => word.Length > 0).ToList();
        if (script.Count == 0)
        {
            return new GateTwoWordError(heard.Count == 0 ? 0 : 1, 0, 0, 0, heard.Count, script.Count,
                heard.Count == 0 ? "the script is empty" : "the script is empty but words were heard");
        }

        var cost = new int[heard.Count + 1, script.Count + 1];
        for (var i = 0; i <= heard.Count; i++)
        {
            cost[i, 0] = i;
        }

        for (var j = 0; j <= script.Count; j++)
        {
            cost[0, j] = j;
        }

        for (var i = 1; i <= heard.Count; i++)
        {
            for (var j = 1; j <= script.Count; j++)
            {
                var substitution = cost[i - 1, j - 1] + (heard[i - 1] == script[j - 1] ? 0 : 1);
                cost[i, j] = Math.Min(Math.Min(cost[i - 1, j] + 1, cost[i, j - 1] + 1), substitution);
            }
        }

        // Walk the table back to say *what* differs: "3 words missing" sends an author to the end of
        // the recording, and "1 word heard differently" names the word.
        var substitutions = 0;
        var deletions = 0;
        var insertions = 0;
        var differences = new List<string>();
        for (int i = heard.Count, j = script.Count; i > 0 || j > 0;)
        {
            if (i > 0 && j > 0 && heard[i - 1] == script[j - 1])
            {
                i--;
                j--;
            }
            else if (i > 0 && j > 0 && cost[i, j] == cost[i - 1, j - 1] + 1)
            {
                substitutions++;
                if (differences.Count < 6)
                {
                    differences.Add($"heard \"{heard[i - 1]}\" for \"{script[j - 1]}\"");
                }

                i--;
                j--;
            }
            else if (j > 0 && cost[i, j] == cost[i, j - 1] + 1)
            {
                deletions++;
                if (differences.Count < 6)
                {
                    differences.Add($"missing \"{script[j - 1]}\"");
                }

                j--;
            }
            else
            {
                insertions++;
                if (differences.Count < 6)
                {
                    differences.Add($"extra \"{heard[i - 1]}\"");
                }

                i--;
            }
        }

        var edits = substitutions + deletions + insertions;
        return new GateTwoWordError(
            (double)edits / script.Count,
            substitutions,
            deletions,
            insertions,
            heard.Count,
            script.Count,
            differences.Count == 0 ? "none" : string.Join("; ", differences));
    }

    // ----- the word list -----------------------------------------------------------------------

    /// <summary>The embedded list, ordered by level then frequency, so "the first N" is a band.</summary>
    public static IReadOnlyList<GateTwoWord> WordList { get; } = LoadWordList();

    /// <summary>The AWL headwords and their sublist, 1-10.</summary>
    public static IReadOnlyDictionary<string, int> AwlSublist { get; } = LoadAwlSublists();

    private static readonly Dictionary<string, int> WordIndex =
        WordList.Select((entry, index) => (entry.Headword, index))
            .GroupBy(pair => pair.Headword, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);

    private static bool IsInList(string word, int? listSize, int awlCeiling) =>
        (listSize is int size && WordIndex.TryGetValue(word, out var index) && index < size)
        || (awlCeiling > 0 && AwlSublist.TryGetValue(word, out var sublist) && sublist <= awlCeiling);

    private static IReadOnlyList<GateTwoWord> LoadWordList()
    {
        var lines = ReadResource("cefr-wordlists.txt");
        var words = new List<GateTwoWord>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 2 || !CefrLevel.TryParseAuthored(parts[1], out var level))
            {
                continue;
            }

            var rank = parts.Length > 2 && int.TryParse(parts[2], out var parsed) ? parsed : int.MaxValue;
            words.Add(new GateTwoWord(parts[0], level, rank));
        }

        if (words.Count == 0)
        {
            throw new InvalidOperationException("the embedded CEFR word list is empty");
        }

        return words;
    }

    private static IReadOnlyDictionary<string, int> LoadAwlSublists()
    {
        var sublists = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in ReadResource("awl-sublists.txt"))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var sublist))
            {
                sublists.TryAdd(parts[0], sublist);
            }
        }

        return sublists;
    }

    private static List<string> ReadResource(string name)
    {
        var assembly = typeof(GateTwo).Assembly;
        var resource = Array.Find(assembly.GetManifestResourceNames(),
            candidate => candidate.EndsWith(name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"'{name}' is not embedded in {assembly.GetName().Name}; BusinessObjects.csproj must list it as an EmbeddedResource.");

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line.TrimEnd());
        }

        return lines;
    }

    // ----- measures --------------------------------------------------------------------------

    private static readonly Regex TokenPattern = new(
        @"[A-Za-z]+(?:['’-][A-Za-z]+)*|[0-9]+(?:\.[0-9]+)?", RegexOptions.CultureInvariant);

    /// <summary>Every word-ish token, in order. Punctuation and a lone hyphen are not tokens.</summary>
    internal static List<string> Tokens(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : TokenPattern.Matches(text).Select(match => match.Value).ToList();

    /// <summary>
    /// Sentences: split after a run of <c>. ! ?</c> that is followed by whitespace or the end of the
    /// text. A capital letter after it is <em>not</em> required, because a lower-case opener is a
    /// real sentence in authored copy and requiring one merged nine sentences into nine-of-sixteen.
    /// The cost is that an abbreviation ("Mr. Smith") splits a sentence, which inflates the count and
    /// with it the Flesch-Kincaid grade; the T-unit measures do not read the sentence count.
    /// </summary>
    internal static List<string> Sentences(string text)
    {
        var sentences = new List<string>();
        var builder = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            builder.Append(text[index]);
            if (text[index] is not ('.' or '!' or '?'))
            {
                continue;
            }

            var next = index + 1;
            while (next < text.Length && text[next] is ' ' or '\t' or '\r' or '\n' or '"' or '\'' or ')')
            {
                builder.Append(text[next]);
                next++;
            }

            sentences.Add(builder.ToString());
            builder.Clear();
            index = next - 1;
        }

        if (builder.ToString().Trim().Length > 0)
        {
            sentences.Add(builder.ToString());
        }

        return sentences.Where(sentence => Tokens(sentence).Count > 0).ToList();
    }

    /// <summary>
    /// Openers that start a clause but never a T-unit: they subordinate it to what precedes.
    /// "that" and "which" are here as relative markers; a "that" opening an independent clause is
    /// rare enough in authored stimulus text to prefer the subordinating reading.
    /// </summary>
    private static readonly HashSet<string> Subordinators = new(StringComparer.Ordinal)
    {
        "because", "when", "whenever", "while", "if", "unless", "although", "though", "since",
        "after", "before", "until", "whereas", "whether", "so that", "in order", "which", "who",
        "whom", "whose", "where", "why", "as", "once", "even",
    };

    /// <summary>
    /// Coordinators that can join two independent clauses. Whether they do depends on what follows,
    /// which is what <see cref="Segmentation"/> decides.
    /// </summary>
    private static readonly HashSet<string> Coordinators = new(StringComparer.Ordinal)
    {
        "and", "but", "or", "so", "yet", "nor",
    };

    /// <summary>
    /// Finite verb forms a coordinated clause needs before it can be its own T-unit. Auxiliaries
    /// plus the irregular past forms, which no suffix rule reaches; a regular verb is caught by
    /// its <c>-ed</c> or <c>-s</c> ending.
    /// </summary>
    private static readonly HashSet<string> FiniteVerbs = new(StringComparer.Ordinal)
    {
        "am", "is", "are", "was", "were", "be", "been", "being", "has", "have", "had", "do",
        "does", "did", "can", "could", "will", "would", "shall", "should", "may", "might", "must",
        "sat", "ran", "ate", "went", "saw", "came", "took", "made", "said", "got", "gave", "found",
        "thought", "told", "became", "left", "felt", "brought", "began", "kept", "held", "wrote",
        "stood", "heard", "let", "meant", "set", "met", "paid", "spoke", "led", "grew", "lost",
        "fell", "sent", "built", "understood", "drew", "broke", "spent", "cut", "rose", "drove",
        "bought", "wore", "chose", "sold", "fought", "taught", "caught", "read", "put", "ran",
        "sang", "swam", "threw", "flew", "knew", "grew", "blew", "won", "slept", "woke", "rode",
    };

    /// <summary>
    /// One sentence's clause and T-unit count. A clause boundary is a sentence end, a semicolon, or
    /// a marker. A <b>T-unit</b> boundary is a sentence end, a semicolon, or a coordinated part that
    /// is itself a clause.
    /// <para>
    /// A coordinated part is a clause when it holds a finite verb and does not open with one: "and
    /// the dog ran" and "and dogs sat" qualify, while "and opened the office" and "and sat down"
    /// share their subject with the first T-unit and must not double it. A part of one token is
    /// never a clause ("bread and milk and eggs"), which is what stops a noun list from becoming
    /// clause density.
    /// </para>
    /// <para>
    /// <b>Known limit.</b> A coordinated <em>subject</em> ("and milk were sold") arrives here as a
    /// second clause and adds a T-unit that does not exist, lowering MLTU. That errs towards
    /// refusing a legitimate item, which is the direction to be wrong in only because the opposite
    /// quietly passes the defect the gate exists to catch. Independently, "and eggs" ends in
    /// <c>-s</c> and looks finite to <see cref="IsFinite"/>; the one-token rule is what rules it
    /// out, and a two-token coordinated noun phrase such as "and new eggs" would not be.
    /// </para>
    /// </summary>
    /// <summary>Words, and the two punctuation marks that can end a clause.</summary>
    private static readonly Regex SegmentPattern = new(
        @"[A-Za-z]+(?:['’-][A-Za-z]+)*|[0-9]+(?:\.[0-9]+)?|[;,]",
        RegexOptions.CultureInvariant);

    private static (int TUnits, int Clauses) Segmentation(string sentence)
    {
        var parts = new List<(bool IsCoordination, List<string> Tokens)>();
        var current = new List<string>();
        var isCoordination = false;

        foreach (var raw in SegmentPattern.Matches(sentence).Select(match => match.Value.ToLowerInvariant()))
        {
            // A comma and a semicolon end a clause when one has already started: the comma is the
            // boundary a fronted subordinate clause is written with ("Because the room is painted,
            // the reading area is upstairs"), and without it the two clauses would merge. A comma
            // inside a list leaves a one-token part, which IsClause refuses.
            var boundary = raw is "," or ";" || Coordinators.Contains(raw) || Subordinators.Contains(raw);
            if (!boundary)
            {
                current.Add(raw);
                continue;
            }

            if (current.Count > 0)
            {
                parts.Add((isCoordination, current));
                current = [];
            }

            // A coordinator that opens a part makes it a coordinate clause; a subordinator makes it
            // dependent, and punctuation neither.
            isCoordination = Coordinators.Contains(raw);
        }

        if (current.Count > 0)
        {
            parts.Add((isCoordination, current));
        }

        if (parts.Count == 0)
        {
            return (0, 0);
        }

        var tUnits = 1;
        var clauses = 1;
        for (var index = 1; index < parts.Count; index++)
        {
            var (coordination, tokens) = parts[index];
            if (!IsClause(tokens))
            {
                continue;   // a coordinated noun or verb phrase adds neither a clause nor a T-unit
            }

            clauses++;
            if (coordination)
            {
                tUnits++;
            }
        }

        return (tUnits, clauses);
    }

    /// <summary>
    /// A clause needs at least two tokens (a subject and a verb), a finite verb somewhere, and no
    /// finite verb in first position - verb-first is the shared-subject continuation.
    /// </summary>
    private static bool IsClause(List<string> tokens) =>
        tokens.Count >= 2 && !IsFinite(tokens[0]) && tokens.Any(IsFinite);

    private static bool IsFinite(string token)
    {
        if (FiniteVerbs.Contains(token))
        {
            return true;
        }

        if (token.EndsWith("ed", StringComparison.Ordinal) && token.Length > 3)
        {
            return true;
        }

        return token.EndsWith('s') && token.Length > 3
            && !token.EndsWith("ss", StringComparison.Ordinal)
            && !token.EndsWith("us", StringComparison.Ordinal)
            && !token.EndsWith("is", StringComparison.Ordinal);
    }

    private static readonly Regex VowelGroup = new("[aeiouy]+", RegexOptions.CultureInvariant);

    /// <summary>Vowel groups with a silent final <c>-e</c> dropped. A pronouncing dictionary would be better; none is installed.</summary>
    internal static int Syllables(string token)
    {
        var word = token.ToLowerInvariant();
        if (word.Length == 0 || char.IsDigit(word[0]))
        {
            // a number is spoken as its digits; one syllable is the closest this counter gets
            return 1;
        }

        var groups = VowelGroup.Matches(word).Count;
        if (groups > 1 && word.EndsWith('e') && !word.EndsWith("le", StringComparison.Ordinal))
        {
            groups--;
        }

        return Math.Max(1, groups);
    }

    private static string Normalise(string token)
    {
        var builder = new StringBuilder(token.Length);
        foreach (var c in token.ToLowerInvariant())
        {
            if (char.IsLetter(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Inflection folding, not stemming: a few unambiguous English suffixes.</summary>
    internal static IEnumerable<string> Foldings(string word)
    {
        yield return word;

        if (word.Length >= 5 && word.EndsWith("ies", StringComparison.Ordinal))
        {
            yield return word[..^3] + "y";
        }

        foreach (var suffix in new[] { "ing", "ed", "es", "s" })
        {
            if (word.Length - suffix.Length < 3 || !word.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var stem = word[..^suffix.Length];
            yield return stem;

            // "stopped" -> "stop", "running" -> "run"
            if (stem.Length > 3 && stem[^1] == stem[^2])
            {
                yield return stem[..^1];
            }

            // "making" -> "make", "hoped" -> "hope"
            if (suffix is "ing" or "ed")
            {
                yield return stem + "e";
            }
        }
    }

    private static string Describe(GateTwoModality modality) =>
        modality == GateTwoModality.Listening ? "listening" : "reading";

    private static string Counts(GateTwoItem item) => item.Kind == GateTwoTextKind.SentenceSlot
        ? $"{SentenceSlotMinimumWords}-{SentenceSlotMaximumWords} (D0.4)"
        : $"{Profile(item.Band, item.Modality)!.MinimumWords}-{Profile(item.Band, item.Modality)!.MaximumWords} (D0.2)";

    private static string Round(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
