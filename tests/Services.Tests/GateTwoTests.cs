using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BusinessObjects;

namespace Services.Tests;

/// <summary>
/// #195: gate two is a pure function, so its rules are checked here without a database.
/// <para>
/// Two groups of tests. The first <b>derives the expected numbers from the specification itself</b>:
/// it parses the D0.1 and D0.2 tables out of <c>docs/specs/2026-11-04-cefr-rebuild-spec.md</c> and
/// asserts <see cref="GateTwo.Profile"/> against them. That is the anti-drift guard the ticket asks
/// for - a threshold edited in code but not in the spec, or the other way round, fails here, and no
/// literal in this file can quietly become the real specification.
/// </para>
/// <para>
/// The second group tests the arithmetic, and every one of them was written by asking "what input
/// would satisfy this guard while being wrong?" For gate two that input is a text whose <em>mean
/// sentence length</em> sits in the band while its <em>mean T-unit length</em> does not, or a text
/// whose coverage is high only because the denominator was the content words. Both have their own
/// test below.
/// </para>
/// </summary>
public sealed class GateTwoTests
{
    // -----------------------------------------------------------------------------------------
    // the specification's own tables
    // -----------------------------------------------------------------------------------------

    private static readonly string SpecPath = Path.Combine(
        DatabaseFixture.RepoRootPath, "docs", "specs", "2026-11-04-cefr-rebuild-spec.md");

    private static string SpecText => File.ReadAllText(SpecPath);

    /// <summary>One row of D0.1 or D0.2, as the spec wrote it.</summary>
    private sealed record SpecRow(string Cell, IReadOnlyList<string> Columns);

    private static IReadOnlyList<SpecRow> SpecTable(string heading)
    {
        var text = SpecText;
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"the spec no longer carries '{heading}'");

        var rows = new List<SpecRow>();
        var seenTable = false;
        foreach (var line in text[start..].Split('\n').Skip(1))
        {
            if (!line.StartsWith('|'))
            {
                if (seenTable)
                {
                    break;
                }

                continue;   // the blank line between a heading and its table
            }

            seenTable = true;
            var cells = line.Split('|').Select(c => c.Trim()).ToList();
            cells.RemoveAt(0);
            cells.RemoveAt(cells.Count - 1);
            if (cells.Count == 0 || cells.All(c => c.Length == 0 || c.All(ch => ch is '-' or ':')))
            {
                continue;
            }

            var band = Regex.Replace(cells[0], "[*]", string.Empty);
            if (CefrLevel.TryParseAuthored(band, out _))
            {
                rows.Add(new SpecRow(band, cells));
            }
        }

        Assert.Equal(6, rows.Count);
        return rows;
    }

    /// <summary>"60–95" or "1.5–3.0"; the spec uses an en dash, and tolerates a hyphen.</summary>
    private static (double Low, double High) Range(string text)
    {
        var match = Regex.Match(text, @"([0-9]+(?:\.[0-9]+)?)\s*[-–]\s*([0-9]+(?:\.[0-9]+)?)");
        Assert.True(match.Success, $"'{text}' carries no range");
        return (double.Parse(match.Groups[1].Value), double.Parse(match.Groups[2].Value));
    }

    private static double Number(string text, string pattern)
    {
        var match = Regex.Match(text, pattern);
        Assert.True(match.Success, $"'{text}' carries no {pattern}");
        return double.Parse(match.Groups[1].Value);
    }

    /// <summary>
    /// D0.2: the word-count envelope, per band and per modality. A listening script and a reading
    /// passage at the same band are different lengths on purpose, and a gate that read one table for
    /// both would refuse every valid A2 dialogue or wave through a 400-word A2 notice.
    /// </summary>
    [Theory]
    [InlineData("A1")]
    [InlineData("A2")]
    [InlineData("B1")]
    [InlineData("B2")]
    [InlineData("C1")]
    [InlineData("C2")]
    public void Word_counts_match_the_D02_modality_envelope(string band)
    {
        CefrLevel.TryParseAuthored(band, out var level);
        var row = SpecTable("#### D0.2 The modality envelope").Single(r => r.Cell == band);

        var listening = Range(Regex.Match(row.Columns[1], @"[0-9]+[-–][0-9]+ words").Value);
        var reading = Range(Regex.Match(row.Columns[2], @"[0-9]+[-–][0-9]+ words").Value);

        var heard = GateTwo.Profile(level, GateTwoModality.Listening)!;
        Assert.Equal((int)listening.Low, heard.MinimumWords);
        Assert.Equal((int)listening.High, heard.MaximumWords);

        var seen = GateTwo.Profile(level, GateTwoModality.Reading)!;
        Assert.Equal((int)reading.Low, seen.MinimumWords);
        Assert.Equal((int)reading.High, seen.MaximumWords);

        // D0.2: spoken input is shorter than written input at the identical band. If the two
        // envelopes ever coincide, one of them was transcribed from the wrong column.
        Assert.True(heard.MaximumWords < seen.MaximumWords,
            $"{band}: listening ({heard.MinimumWords}-{heard.MaximumWords}) must be shorter than reading ({seen.MinimumWords}-{seen.MaximumWords})");
    }

    /// <summary>
    /// D0.2: the Flesch-Kincaid grade of a reading text. It is bounded at every band but the last,
    /// where the specification writes "FK &gt;14.0" - a sentence the band's own floor makes true, and
    /// one a closed range would silently drop.
    /// </summary>
    [Theory]
    [InlineData("A1")]
    [InlineData("A2")]
    [InlineData("B1")]
    [InlineData("B2")]
    [InlineData("C1")]
    [InlineData("C2")]
    public void Flesch_kincaid_matches_the_D02_reading_envelope(string band)
    {
        CefrLevel.TryParseAuthored(band, out var level);
        var row = SpecTable("#### D0.2 The modality envelope").Single(r => r.Cell == band);
        var cell = row.Columns[2];

        var bound = GateTwo.Profile(level, GateTwoModality.Reading)!.FleschKincaid;
        Assert.NotNull(bound);

        if (Regex.IsMatch(cell, @"FK\s*>\s*[0-9]"))
        {
            Assert.Equal(Number(cell, @"FK\s*>\s*([0-9.]+)"), bound!.Minimum);
            Assert.False(bound.IncludesMinimum);
            Assert.True(double.IsInfinity(bound.Maximum));
        }
        else
        {
            var (low, high) = Range(Regex.Match(cell, @"FK(?: grade)?\s*[0-9.]+[-–][0-9.]+").Value);
            Assert.Equal(low, bound!.Minimum);
            Assert.Equal(high, bound.Maximum);
        }

        // D0.2 bounds readability for written input only. Listening carries wpm instead, and wpm
        // needs a duration this schema does not carry for a stimulus.
        Assert.Null(GateTwo.Profile(level, GateTwoModality.Listening)!.FleschKincaid);
    }

    /// <summary>
    /// D0.1 and D0.2 together: the reading MLTU is the level claim table's range, and the listening
    /// MLTU is that range shortened by the subordination gap D0.2 states as "roughly 15-20%".
    /// </summary>
    [Theory]
    [InlineData("A1")]
    [InlineData("A2")]
    [InlineData("B1")]
    [InlineData("B2")]
    [InlineData("C1")]
    [InlineData("C2")]
    public void Mean_t_unit_length_matches_D01_and_is_shortened_for_listening(string band)
    {
        CefrLevel.TryParseAuthored(band, out var level);
        var row = SpecTable("#### D0.1 The level claim table").Single(r => r.Cell == band);
        var cell = row.Columns[2];

        var reading = GateTwo.Profile(level, GateTwoModality.Reading)!.MeanTUnitLength;
        var listening = GateTwo.Profile(level, GateTwoModality.Listening)!.MeanTUnitLength;

        if (Regex.IsMatch(cell, @"MLTU\s*>\s*[0-9]"))
        {
            Assert.Equal(Number(cell, @"MLTU\s*>\s*([0-9.]+)"), reading.Minimum);
            Assert.False(reading.IncludesMinimum);
            Assert.True(double.IsInfinity(reading.Maximum));
        }
        else
        {
            var (low, high) = Range(Regex.Match(cell, @"MLTU\s*[0-9.]+[-–][0-9.]+").Value);
            Assert.Equal(low, reading.Minimum);
            Assert.Equal(high, reading.Maximum);
        }

        // "roughly 15-20% shorter MLTU", with one share chosen rather than two bounds, because a
        // range here would mean two items at the same band judged by different arithmetic. The gap
        // is asserted to stay inside the specification's window.
        var gap = 1 - (listening.FiniteMaximum / reading.FiniteMaximum);
        Assert.InRange(gap, 0.15, 0.20);
    }

    /// <summary>
    /// D0.1: clauses per T-unit. Two bands state no number and this asserts the gap rather than
    /// inventing one - A1 constrains coordination to a closed list instead of counting clauses, and
    /// C2 names structural devices instead of a ratio.
    /// </summary>
    [Theory]
    [InlineData("A1", null)]
    [InlineData("A2", "≤1.3")]
    [InlineData("B1", "1.3–1.6")]
    [InlineData("B2", "1.6–2.0")]
    [InlineData("C1", ">2.0")]
    [InlineData("C2", null)]
    public void Clauses_per_t_unit_match_D01(string band, string? cell)
    {
        CefrLevel.TryParseAuthored(band, out var level);
        var bound = GateTwo.Profile(level, GateTwoModality.Reading)!.ClausesPerTUnit;

        if (cell is null)
        {
            var syntactic = SpecTable("#### D0.1 The level claim table").Single(r => r.Cell == band).Columns[2];
            Assert.DoesNotContain("clauses/T-unit", syntactic);
            Assert.Null(bound);
            return;
        }

        Assert.NotNull(bound);
        if (cell.StartsWith('>'))
        {
            Assert.Equal(double.Parse(cell[1..]), bound!.Minimum);
            Assert.False(bound.IncludesMinimum);
        }
        else if (cell.StartsWith('≤'))
        {
            Assert.Equal(double.Parse(cell[1..]), bound!.Maximum);
            Assert.Equal(double.NegativeInfinity, bound.Minimum);
        }
        else
        {
            var (low, high) = Range(cell);
            Assert.Equal(low, bound!.Minimum);
            Assert.Equal(high, bound.Maximum);
        }
    }

    /// <summary>
    /// D0.1: the lexical profile. The list size is the "top N" the row names, and where a row names
    /// no N - C1's "A1-C1" - the list is every headword up to that level, which is a count the word
    /// list itself answers.
    /// </summary>
    [Theory]
    [InlineData("A1", 800, 95.0, 95.0, 0)]
    [InlineData("A2", 1800, 92.0, 92.0, 0)]
    [InlineData("B1", 3000, 90.0, 90.0, 0)]
    [InlineData("B2", 5000, 88.0, 88.0, 5)]
    [InlineData("C1", 7769, 85.0, 85.0, 10)]
    [InlineData("C2", null, null, null, 0)]
    public void Lexical_bounds_match_D01(string band, int? listSize, double? minimum, double? _, int awl)
    {
        CefrLevel.TryParseAuthored(band, out var level);
        var cell = SpecTable("#### D0.1 The level claim table").Single(r => r.Cell == band).Columns[1];

        var reading = GateTwo.Profile(level, GateTwoModality.Reading)!;
        Assert.Equal(listSize, reading.LexicalListSize);
        Assert.Equal(minimum, reading.MinimumLexicalCoverage);
        Assert.Equal(awl, reading.AwlSublistCeiling);

        // the share the specification wrote, read from the row rather than from this test's literal
        var stated = Regex.Match(cell, @"≥\s*([0-9.]+)%");
        if (stated.Success)
        {
            Assert.Equal(double.Parse(stated.Groups[1].Value), reading.MinimumLexicalCoverage);
        }

        // the "top N" the row names, where it names one
        var top = Regex.Match(cell, @"top\s*([0-9,]+)");
        if (top.Success)
        {
            Assert.Equal(int.Parse(top.Groups[1].Value.Replace(",", string.Empty)), reading.LexicalListSize);
        }

        // "unrestricted" is a claim, not an absent check: nothing is refused for its vocabulary
        if (cell.StartsWith("Unrestricted", StringComparison.Ordinal))
        {
            Assert.False(reading.HasLexicalBound);
        }
    }

    /// <summary>
    /// The word list the coverage rule divides by must actually hold the words the bands name, or
    /// every A1 text is refused. This is the check that catches a list shipped truncated, in the
    /// wrong order, or accidentally sorted so that "the first 800" is 800 rare words.
    /// </summary>
    [Fact]
    public void The_word_list_is_ordained_by_level_and_the_first_800_are_A1_words()
    {
        var words = GateTwo.WordList;

        Assert.True(words.Count > 8000, $"the word list holds only {words.Count} headwords");
        Assert.Equal("you", words[0].Headword);
        Assert.All(words.Take(800), entry => Assert.Equal(CefrLevel.A1, entry.Level));

        // a common word a learner meets in week one, and a rare one that must not be in the core
        Assert.Contains(words.Take(800), entry => entry.Headword == "holiday");
        Assert.DoesNotContain(words.Take(800), entry => entry.Headword == "ubiquitous");

        // the list is the only source of the sizes the profile uses
        Assert.Equal(7769, words.Count(entry => entry.Level <= CefrLevel.C1));
        Assert.Equal(570, GateTwo.AwlSublist.Count);
        Assert.Equal(60, GateTwo.AwlSublist.Count(entry => entry.Value == 1));
    }

    // -----------------------------------------------------------------------------------------
    // the arithmetic, and the input that would satisfy each guard while being wrong
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// N sentences of <paramref name="wordsPerSentence"/> one-syllable, in-list words. Every dial
    /// gate two reads is therefore the caller's: words = sentences x wordsPerSentence, T-units =
    /// sentences, MLTU = wordsPerSentence, clauses/T-unit = 1.0, coverage = 100%.
    /// </summary>
    private static string Cat(int sentences, int wordsPerSentence)
    {
        var sentence = string.Join(' ', Enumerable.Repeat("cat", wordsPerSentence));
        return string.Join(' ', Enumerable.Repeat(sentence + ".", sentences));
    }

    private static GateTwoRefusal? Refusal(GateTwoVerdict verdict, string rule) =>
        verdict.Refusals.FirstOrDefault(r => r.Rule == rule);

    private static string Rules(GateTwoVerdict verdict) =>
        string.Join(", ", verdict.Refusals.Select(r => r.Rule));

    private static GateTwoItem Item(
        string? text,
        string band = "B1",
        GateTwoModality modality = GateTwoModality.Reading,
        GateTwoTextKind kind = GateTwoTextKind.Stimulus,
        GateTwoAsr? asr = null)
    {
        CefrLevel.TryParseAuthored(band, out var level);
        return new GateTwoItem(text, level, modality, kind, asr);
    }

    /// <summary>
    /// A B1 listening script that satisfies every D0.1 and D0.2 bound: 160 words (D0.2 listening is
    /// 110-185), MLTU 10.67 (listening A1-C2 is the reading range shortened by 17.5%, so 8.66-11.14),
    /// 1.33 clauses per T-unit, and 100% coverage of the first 3,000 headwords. Every acoustic test
    /// starts here, so only the transcription can refuse it.
    /// </summary>
    private const string B1Listening =
        "Good morning, and thank you for calling the city library. "
        + "The library opens at nine and it closes at six on most days. "
        + "Because the main room is being painted, the reading area is upstairs this week. "
        + "You can return your books at the front desk, which is near the main door. "
        + "If you have an old card, please bring it with you when you come. "
        + "The new card costs nothing for students, because the city pays for them. "
        + "When you join, you may borrow four books at one time. "
        + "The library keeps the books for three weeks, so please bring them back on time. "
        + "You can call us again if you need more help. "
        + "Our staff will show you where the history books are. "
        + "The computer room is on the second floor, and it is quiet there. "
        + "It is free to use the computers for one hour each day. "
        + "We look forward to seeing you at the library soon.";

    /// <summary>
    /// A1 reading: 75 words (D0.2 reading is 60-100), 5 sentences, 10 T-units so MLTU 7.5 sits on
    /// A1's ceiling, FK 2.06 inside 1.5-3.0, and 100% coverage of the first 800 headwords. Every
    /// rule is satisfied, so a test that changes one number knows which rule refuses it.
    /// </summary>
    private const string A1Reading =
        "The cat is in the big box and the dog is on the old bed. "
        + "The cat is in the big box and the dog is on the old bed. "
        + "The cat is in the big box and the dog is on the old bed. "
        + "The cat is in the big box and the dog is on the old bed. "
        + "The cat is in the big box and the dog is on the old bed.";

    /// <summary>
    /// The trap named in the ticket: mean sentence length is not mean T-unit length. Eight
    /// ten-word sentences sit in the middle of A2's 7.5-10.5 band by that measure - mean sentence
    /// length 7.5 - and eight five-word sentences that coordinate two clauses each add sixteen
    /// T-units, so the text's 120 words divide into 24 T-units: MLTU 5.0, well below the band's
    /// floor. A check built on <c>Words / Sentences</c> accepts it.
    /// </summary>
    [Fact]
    public void Mean_sentence_length_inside_the_band_does_not_rescue_a_mean_t_unit_length_below_it()
    {
        // 8 x 9 words in one T-unit each, then 8 x 6 words in two T-units each
        var longer = "The small cat sat on the old red mat.";
        var shorter = "Cats ran, but the dogs sat.";
        var text = string.Join(' ',
            Enumerable.Range(0, 8).Select(_ => longer)
                      .Concat(Enumerable.Range(0, 8).Select(_ => shorter)));

        var measured = GateTwo.Measure(text, CefrLevel.A2);

        Assert.Equal(120, measured.Words);
        Assert.Equal(16, measured.Sentences);
        Assert.Equal(24, measured.TUnits);
        Assert.Equal(7.5, (double)measured.Words / measured.Sentences, 3);   // the wrong denominator
        Assert.Equal(5.0, measured.MeanTUnitLength, 3);                      // the right one

        var verdict = GateTwo.Review(Item(text, "A2"));

        // the word count is inside A2's 120-180 envelope, so only the T-unit rule can refuse it
        Assert.Null(Refusal(verdict, GateTwoRules.WordCount));

        var refusal = Refusal(verdict, GateTwoRules.MeanTUnitLength);
        Assert.NotNull(refusal);
        Assert.Contains("T-unit", refusal!.Message, StringComparison.Ordinal);
        Assert.Contains("5", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The boundary of the same rule, both ways: 7 words per T-unit is outside A2's floor and 8 is
    /// inside it. A rule written with the boundary on the wrong side refuses real items.
    /// </summary>
    [Fact]
    public void The_mean_t_unit_length_floor_is_inclusive_at_A2()
    {
        Assert.Null(Refusal(GateTwo.Review(Item(Cat(1, 8), "A2")), GateTwoRules.MeanTUnitLength));

        var below = Refusal(GateTwo.Review(Item(Cat(1, 7), "A2")), GateTwoRules.MeanTUnitLength);
        Assert.NotNull(below);
        Assert.Contains("7.5", below!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ceiling, both ways: 10 words per T-unit is inside A2 and 11 is outside it. The unit is one
    /// word wide here, which is the only way an off-by-one in a floating-point ceiling shows.
    /// </summary>
    [Fact]
    public void The_mean_t_unit_length_ceiling_is_inclusive_at_A2()
    {
        Assert.Null(Refusal(GateTwo.Review(Item(Cat(1, 10), "A2")), GateTwoRules.MeanTUnitLength));

        var above = Refusal(GateTwo.Review(Item(Cat(1, 11), "A2")), GateTwoRules.MeanTUnitLength);
        Assert.NotNull(above);
        Assert.Contains("10.5", above!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A T-unit is an independent clause plus its dependents, so subordination adds a <em>clause</em>
    /// without adding a <em>T-unit</em>: 7 words, 1 T-unit, 2 clauses. A counter that treated every
    /// clause boundary as a T-unit boundary reports 3.5 words and half the clause density.
    /// </summary>
    [Fact]
    public void A_subordinate_clause_adds_a_clause_but_not_a_t_unit()
    {
        var measured = GateTwo.Measure("The cat sat because the dog ran.", CefrLevel.A2);

        Assert.Equal(7, measured.Words);
        Assert.Equal(1, measured.TUnits);
        Assert.Equal(2, measured.Clauses);
        Assert.Equal(7.0, measured.MeanTUnitLength, 3);
        Assert.Equal(2.0, measured.ClausesPerTUnit, 3);
    }

    /// <summary>
    /// The same distinction on a text long enough for D0.1's clause bounds to bite: three sentences,
    /// each one independent clause with one subordinate clause, are three T-units and six clauses.
    /// A counter that counted every clause as a T-unit reports nine and an MLTU one third of the
    /// truth, which passes a B1 passage that is really C1 by density.
    /// </summary>
    [Fact]
    public void Subordination_does_not_multiply_the_t_unit_count()
    {
        var measured = GateTwo.Measure(
            "The cat sat because the dog ran. The dog ate because the cat was late. "
            + "The cat ran because the dog barked.", CefrLevel.A2);

        // 7 + 8 + 7 words
        Assert.Equal(22, measured.Words);
        Assert.Equal(3, measured.TUnits);
        Assert.Equal(6, measured.Clauses);
        Assert.InRange(measured.MeanTUnitLength, 7.33, 7.34);
        Assert.Equal(2.0, measured.ClausesPerTUnit, 3);
    }

    /// <summary>Coordination of two independent clauses is two T-units, so MLTU halves.</summary>
    [Fact]
    public void A_coordinated_independent_clause_adds_a_t_unit()
    {
        var measured = GateTwo.Measure("The cat sat, but the cat ran.", CefrLevel.A2);

        Assert.Equal(7, measured.Words);
        Assert.Equal(2, measured.TUnits);
        Assert.Equal(2, measured.Clauses);
        Assert.Equal(3.5, measured.MeanTUnitLength, 3);
        Assert.Equal(1.0, measured.ClausesPerTUnit, 3);
    }

    /// <summary>
    /// A coordinating conjunction joining two noun phrases is not a clause boundary. Counting every
    /// "and" as one turns "bread and milk" into two clauses and refuses an A2 text for clause
    /// density it does not have.
    /// </summary>
    [Fact]
    public void A_list_joined_by_and_is_not_a_clause_boundary()
    {
        var measured = GateTwo.Measure("The cat bought bread and milk and eggs.", CefrLevel.A2);

        Assert.Equal(1, measured.TUnits);
        Assert.Equal(1, measured.Clauses);
    }

    /// <summary>
    /// The clause-density boundary, which is the clearest place the specification's two inequalities
    /// meet: B2 writes "1.6-2.0" and C1 writes "&gt;2.0". A 7-word text with one T-unit and two
    /// clauses is exactly 2.0 clauses/T-unit - inside B2, outside C1. A gate that mirrored the
    /// strict "&gt;" as "&gt;=" accepts for C1 the exact density the specification refuses. Only the
    /// clause rule is asserted here; the fixture is deliberately not a legal item.
    /// </summary>
    [Fact]
    public void The_clause_density_ceiling_of_one_band_is_the_exclusive_floor_of_the_next()
    {
        const string text = "The cat sat because the cat ran.";

        Assert.Null(Refusal(GateTwo.Review(Item(text, "B2")), GateTwoRules.ClausesPerTUnit));

        var c1 = Refusal(GateTwo.Review(Item(text, "C1")), GateTwoRules.ClausesPerTUnit);
        Assert.NotNull(c1);
        Assert.Contains("more than 2", c1!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The word-count boundary, both ways, on the A2 reading envelope: 120 words is the floor and
    /// 119 is one short of it; 180 is the ceiling and 181 is over. An author who trims a passage to
    /// exactly the bound must not be refused.
    /// </summary>
    [Fact]
    public void The_A2_reading_word_count_envelope_is_inclusive_at_both_ends()
    {
        Assert.Null(Refusal(GateTwo.Review(Item(Cat(12, 10), "A2")), GateTwoRules.WordCount));
        Assert.Null(Refusal(GateTwo.Review(Item(Cat(18, 10), "A2")), GateTwoRules.WordCount));

        Assert.NotNull(Refusal(GateTwo.Review(Item(Cat(119, 1), "A2")), GateTwoRules.WordCount));
        Assert.NotNull(Refusal(GateTwo.Review(Item(Cat(181, 1), "A2")), GateTwoRules.WordCount));
    }

    /// <summary>
    /// Listening and reading differ at the same band, and the discriminating word count is the one
    /// between the two ceilings. A1 listening is 40-65 words and A1 reading is 60-100, so an 80-word
    /// text is legal as reading and too long as listening. A gate reading one envelope for both
    /// refuses every valid A1 reading passage or waves through an 80-word "notice" as audio.
    /// </summary>
    [Fact]
    public void A_word_count_between_the_two_A1_envelopes_is_refused_only_as_listening()
    {
        var text = Cat(80, 1);

        Assert.Null(Refusal(
            GateTwo.Review(Item(text, "A1", GateTwoModality.Reading)), GateTwoRules.WordCount));

        var heard = Refusal(
            GateTwo.Review(Item(text, "A1", GateTwoModality.Listening)), GateTwoRules.WordCount);
        Assert.NotNull(heard);
        Assert.Contains("40-65", heard!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same asymmetry in the other direction, on B1: 160 words is inside listening's 110-185 and
    /// under reading's floor of 220. A gate that only shortened the listening <em>ceiling</em> would
    /// accept a 160-word B1 passage that cannot support a B1 reading item (D0.3).
    /// </summary>
    [Fact]
    public void A_B1_script_legal_as_listening_is_too_short_as_reading()
    {
        Assert.Null(Refusal(GateTwo.Review(Item(B1Listening, "B1", GateTwoModality.Listening,
            asr: new GateTwoAsr.Transcribed(B1Listening))), GateTwoRules.WordCount));

        var seen = Refusal(GateTwo.Review(Item(B1Listening, "B1", GateTwoModality.Reading)),
            GateTwoRules.WordCount);
        Assert.NotNull(seen);
        Assert.Contains("220-300", seen!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Flesch-Kincaid boundary on A1's 1.5-3.0. This fixture is 75 monosyllabic words in 5
    /// sentences: <c>0.39 * 15 + 11.8 - 15.59 = 2.06</c>, inside the band. Shorten it and the grade
    /// falls with it, because with monosyllables the grade is a function of words per sentence alone.
    /// </summary>
    [Fact]
    public void The_A1_flesch_kincaid_envelope_is_inclusive_at_both_ends()
    {
        // the legal fixture, at the middle of the band
        Assert.Null(Refusal(GateTwo.Review(Item(A1Reading, "A1")), GateTwoRules.FleschKincaid));

        // 100 words in 2 sentences is 50 words/sentence: FK far above A1's ceiling
        var tooLong = Refusal(GateTwo.Review(Item(Cat(2, 50), "A1")), GateTwoRules.FleschKincaid);
        Assert.NotNull(tooLong);
        Assert.Contains("1.5-3", tooLong!.Message, StringComparison.Ordinal);

        // 20 three-word sentences: 60 words, exactly A1 reading's floor, but 3 words/sentence
        // gives FK -2.62, under A1's 1.5 floor
        var tooShort = Refusal(GateTwo.Review(Item(Cat(20, 3), "A1")), GateTwoRules.FleschKincaid);
        Assert.NotNull(tooShort);
        Assert.Contains("below", tooShort!.Message, StringComparison.OrdinalIgnoreCase);

        // and it is *only* the FK rule that refuses it: the length is exactly the floor
        Assert.Null(Refusal(GateTwo.Review(Item(Cat(20, 3), "A1")), GateTwoRules.WordCount));
    }

    /// <summary>
    /// The coverage denominator is <b>every token</b>, as D0.1 writes it ("≥95% tokens"). This
    /// fixture is exactly 95% over all tokens and 83% over its content words, so a guard that
    /// divided by content words would refuse an item the specification accepts.
    /// </summary>
    [Fact]
    public void Lexical_coverage_divides_by_every_token_not_by_the_content_words()
    {
        // 20 tokens, 19 in the A1 list. Content words: cat, bed, dog, box, bag (in list) and
        // ubiquitous (out) - 5 of 6, i.e. 83%.
        const string text = "The cat is on the bed and the dog is in the box and the cat is a bag ubiquitous.";

        var measured = GateTwo.Measure(text, CefrLevel.A1);
        Assert.Equal(20, measured.Words);
        Assert.Equal(19, measured.InListTokens);
        Assert.Equal(0.95, measured.LexicalCoverage, 3);

        // the all-token denominator is what the rule reads, so the item is accepted
        Assert.Null(Refusal(GateTwo.Review(Item(text, "A1")), GateTwoRules.LexicalCoverage));
    }

    /// <summary>
    /// One token fewer and the same text is under A1's floor: 18 of 19 is 94.7%, which D0.1 refuses.
    /// This is the pair to the test above - the boundary is exactly 95%.
    /// </summary>
    [Fact]
    public void One_off_list_token_too_many_is_refused_at_A1()
    {
        const string text = "The cat is on the bed and the dog is in the box and the cat is in ubiquitous.";

        var measured = GateTwo.Measure(text, CefrLevel.A1);
        Assert.Equal(19, measured.Words);
        Assert.Equal(18, measured.InListTokens);
        Assert.Equal(18.0 / 19.0, measured.LexicalCoverage, 3);

        var refusal = Refusal(GateTwo.Review(Item(text, "A1")), GateTwoRules.LexicalCoverage);
        Assert.NotNull(refusal);
        Assert.Contains("95", refusal!.Message, StringComparison.Ordinal);
        Assert.Contains("ubiquitous", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A word the list holds only as a lemma is covered by its inflected form, or every text with a
    /// past tense is refused. "arrive", "arriving" and "arrives" are one family.
    /// </summary>
    [Fact]
    public void An_inflected_form_is_covered_by_its_headword()
    {
        Assert.True(GateTwo.Covers("arriving", CefrLevel.B2));
        Assert.True(GateTwo.Covers("arrives", CefrLevel.B2));
        Assert.True(GateTwo.Covers("arrive", CefrLevel.B2));

        // and a word that is not in the list at all is not covered by guessing a stem
        Assert.False(GateTwo.Covers("ubiquitous", CefrLevel.B2));
    }

    /// <summary>
    /// "Unrestricted" is the C2 lexical profile, and it means everything is inside it. The other
    /// reading - that a band with no word list covers no words - reports C2 coverage as 0% and would
    /// refuse every C2 text the moment a bound was switched on. This test exists because the first
    /// draft did exactly that.
    /// </summary>
    [Fact]
    public void C2_covers_every_word_because_its_profile_is_unrestricted()
    {
        Assert.True(GateTwo.Covers("cat", CefrLevel.C2));
        Assert.True(GateTwo.Covers("ubiquitous", CefrLevel.C2));

        var measured = GateTwo.Measure("Ubiquitous vestigial vindication of virtuosity.", CefrLevel.C2);
        Assert.Equal(1.0, measured.LexicalCoverage, 3);
    }

    /// <summary>
    /// C2's lexical profile is "unrestricted": a text of rare academic lexis is accepted, and the
    /// gate says nothing about vocabulary. A profile that carried C1's 85% floor into C2 would
    /// refuse the band's own target register.
    /// </summary>
    [Fact]
    public void C2_refuses_nothing_for_its_vocabulary()
    {
        const string text = "Ubiquitous vestigial vindication of virtuosity and voracity.";

        Assert.Null(Refusal(GateTwo.Review(Item(text, "C2")), GateTwoRules.LexicalCoverage));
    }

    /// <summary>
    /// D17's acoustic rule at its exact boundary: 0.0% word error rate. <see cref="B1Listening"/> is a
    /// script that passes every other rule, so only the transcription can refuse it - and one word
    /// swapped is 1 error over 160 reference words, which a rule rounding the percentage to one
    /// decimal place would call 0.6% and a rule comparing "errors &gt; 0" on a rounded <em>share</em>
    /// of 100 would call zero.
    /// </summary>
    [Fact]
    public void One_wrong_word_in_the_transcription_is_refused()
    {
        Assert.True(GateTwo.Review(Item(B1Listening, "B1", GateTwoModality.Listening,
            asr: new GateTwoAsr.Transcribed(B1Listening))).Accepted);

        var verdict = GateTwo.Review(Item(B1Listening, "B1", GateTwoModality.Listening,
            asr: new GateTwoAsr.Transcribed(B1Listening.Replace("city library", "city bakeries"))));

        var refusal = Refusal(verdict, GateTwoRules.WordErrorRate);
        Assert.NotNull(refusal);
        Assert.Contains("0%", refusal!.Message, StringComparison.Ordinal);
        Assert.Contains("bakeries", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The boundary that rounding hides. D17's threshold is 0.0%, and one error in an 800-word script
    /// is 0.125% - a share that rounds to 0.0% at one decimal place, and to 0 at zero places. A gate
    /// that compared a <em>rounded</em> percentage to the threshold would pass a recording with a
    /// clipped word in it, which is the exact defect D17 exists to refuse. The comparison is on the
    /// edit count, so this is refused.
    /// </summary>
    [Fact]
    public void One_error_in_a_long_script_is_still_refused_though_it_rounds_to_zero_percent()
    {
        // 218 sentences of 11 words plus one of 8: 2,406 words with exactly one "meeting" to mishear.
        // The script has to be this long for the trap to exist: 1 error in 2,406 words is 0.0416%,
        // which rounds to 0.0% at one decimal place - the value D17 accepts.
        var script = string.Join(' ',
            Enumerable.Repeat("The staff arrived early and opened the office before the bell.", 218)
                .Append("The manager called the morning meeting to order."));
        var heard = script.Replace("meeting", "metering");

        var error = GateTwo.WordErrorRate(heard, script);

        Assert.Equal(2406, error.ReferenceWords);
        Assert.Equal(1, error.Edits);
        Assert.Equal(1.0 / 2406.0, error.Rate, 6);
        Assert.Equal(0.0, Math.Round(error.Rate * 100, 1));   // rounds to 0.0% - the trap

        Assert.NotNull(Refusal(
            GateTwo.Review(Item(script, "B1", GateTwoModality.Listening,
                asr: new GateTwoAsr.Transcribed(heard))),
            GateTwoRules.WordErrorRate));
    }

    /// <summary>
    /// A clipped word at the end of the recording is a missing token, not a synonym, and a missing
    /// token must be an error. A comparison on "does every heard word appear in the script" accepts
    /// a recording that drops its last half.
    /// </summary>
    [Fact]
    public void A_transcription_missing_words_is_refused()
    {
        var verdict = GateTwo.Review(Item(B1Listening, "B1", GateTwoModality.Listening,
            asr: new GateTwoAsr.Transcribed(B1Listening[..(B1Listening.Length / 2)])));

        var refusal = Refusal(verdict, GateTwoRules.WordErrorRate);
        Assert.NotNull(refusal);
        Assert.Contains("missing", refusal!.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Punctuation and case are not word errors: a transcription that differs only in commas and
    /// capitals heard the same words, and refusing it would refuse every honest transcript. (The
    /// first draft compared raw tokens, which made every capitalised word a substitution - the test
    /// caught it.)
    /// </summary>
    [Fact]
    public void A_transcription_differing_only_in_punctuation_is_accepted()
    {
        var heard = B1Listening
            .Replace(",", string.Empty)
            .Replace(".", string.Empty)
            .ToUpperInvariant();

        Assert.True(GateTwo.Review(Item(B1Listening, "B1", GateTwoModality.Listening,
            asr: new GateTwoAsr.Transcribed(heard))).Accepted,
            "punctuation and case are not words");
    }

    /// <summary>
    /// The rule the ticket warns about: 0.0% WER is either verified or it is not. An unavailable
    /// transcription is a refusal, because a gate that passes what it could not check is a gate that
    /// certifies nothing - and the flaw it misses is heard by every learner who reads the script.
    /// </summary>
    [Fact]
    public void An_unavailable_transcription_is_refused_rather_than_treated_as_a_pass()
    {
        var verdict = GateTwo.Review(Item(B1Listening, "B1", GateTwoModality.Listening,
            asr: new GateTwoAsr.Unavailable("no ASR credential configured")));

        var refusal = Refusal(verdict, GateTwoRules.TranscriptUnavailable);
        Assert.NotNull(refusal);
        Assert.Contains("no ASR credential configured", refusal!.Message, StringComparison.Ordinal);
        Assert.False(verdict.Accepted);

        // ... and an audio item with no verifier attempted at all is the same case, not an omission
        var silent = GateTwo.Review(Item(B1Listening, "B1", GateTwoModality.Listening));
        Assert.NotNull(Refusal(silent, GateTwoRules.TranscriptUnavailable));
    }

    /// <summary>
    /// Listening text with no script cannot be checked against the audio at all, and the transcript
    /// is the artefact a learner reads in review: its absence is a defect of its own.
    /// </summary>
    [Fact]
    public void A_listening_item_with_no_script_is_refused()
    {
        var verdict = GateTwo.Review(Item(null, "B1", GateTwoModality.Listening,
            asr: new GateTwoAsr.Transcribed("anything")));
        Assert.NotNull(Refusal(verdict, GateTwoRules.MissingScript));
    }

    /// <summary>
    /// D0.2's speaking and writing columns are <em>learner output</em> demands, not properties of the
    /// authored prompt. Profiling a 40-word writing prompt against the reading envelope would refuse
    /// every productive task in the pool, so gate two says so and measures nothing.
    /// </summary>
    [Fact]
    public void A_productive_prompt_is_not_profiled_against_the_reading_envelope()
    {
        var verdict = GateTwo.Review(Item(
            "Argue for or against a congestion charge. Give reasons and an example.",
            "B2", GateTwoModality.Productive));

        Assert.True(verdict.Accepted, Rules(verdict));
        Assert.Null(GateTwo.Profile(CefrLevel.B2, GateTwoModality.Productive));

        var notice = Assert.Single(verdict.Notices, n => n.Rule == GateTwoRules.NotProfiled);
        Assert.Contains("rubric", notice.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// D0.4 bounds a standalone <c>sentence_cloze</c> stimulus at 15-45 words, not by D0.2: D0.2's
    /// A1 reading floor is 60 words, so a 20-word cloze is legal by D0.4 and refused by D0.2. The
    /// band's MLTU and FK bounds cannot apply either - a one-sentence cloze's MLTU is its word count
    /// - so only the length and the lexical profile are checked.
    /// </summary>
    [Fact]
    public void A_sentence_slot_uses_the_D04_word_bound_not_the_D02_envelope()
    {
        const string cloze =
            "The cat is on the bed and the dog is in the box and the bird is in the ____ tree.";

        var slot = GateTwo.Review(Item(cloze, "A1", kind: GateTwoTextKind.SentenceSlot));
        Assert.Equal(20, GateTwo.Measure(cloze, CefrLevel.A1).Words);
        Assert.Null(Refusal(slot, GateTwoRules.WordCount));
        Assert.Null(Refusal(slot, GateTwoRules.MeanTUnitLength));
        Assert.Null(Refusal(slot, GateTwoRules.FleschKincaid));

        // ... and the same text as a stimulus is below D0.2's 60-word A1 reading floor
        Assert.NotNull(Refusal(GateTwo.Review(Item(cloze, "A1")), GateTwoRules.WordCount));

        var tooLong = Refusal(
            GateTwo.Review(Item(Cat(1, 46), "B1", kind: GateTwoTextKind.SentenceSlot)), GateTwoRules.WordCount);
        Assert.NotNull(tooLong);
        Assert.Contains("15-45", tooLong!.Message, StringComparison.Ordinal);

        var tooShort = Refusal(
            GateTwo.Review(Item(Cat(1, 14), "B1", kind: GateTwoTextKind.SentenceSlot)), GateTwoRules.WordCount);
        Assert.NotNull(tooShort);
        Assert.Contains("15-45", tooShort!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The acceptance side of the whole gate: two hand-built items that satisfy every bound are
    /// accepted with no refusals at all. Without this, a gate that refused everything would pass
    /// every test above.
    /// </summary>
    [Fact]
    public void Items_inside_every_bound_are_accepted()
    {
        Assert.True(GateTwo.Review(Item(A1Reading, "A1")).Accepted,
            Rules(GateTwo.Review(Item(A1Reading, "A1"))));

        Assert.True(GateTwo.Review(Item(B1Listening, "B1", GateTwoModality.Listening,
            asr: new GateTwoAsr.Transcribed(B1Listening))).Accepted,
            Rules(GateTwo.Review(Item(B1Listening, "B1", GateTwoModality.Listening,
                asr: new GateTwoAsr.Transcribed(B1Listening)))));
    }

    /// <summary>
    /// The B1 reading fixture is not merely short enough to dodge the rules - it sits inside every
    /// one of them, and this asserts the numbers rather than trusting them. Each is the midpoint of
    /// a different bound, so a rule edited to a different number leaves the fixture outside it and
    /// this test says which. The passage itself lives in <c>PoolGateTwoTests</c> because the database
    /// test is where it does its work.
    /// </summary>
    [Fact]
    public void The_B1_reading_fixture_sits_inside_every_bound()
    {
        var measured = GateTwo.Measure(PoolGateTwoTests.B1Passage, CefrLevel.B1);

        Assert.Equal(299, measured.Words);                              // D0.2: 220-300
        Assert.Equal(13.0, measured.MeanTUnitLength, 3);                 // D0.1: 10.5-13.5
        Assert.Equal(1.348, measured.ClausesPerTUnit, 3);                // D0.1: 1.3-1.6
        Assert.Equal(6.97, measured.FleschKincaid, 2);                   // D0.2: 6.0-8.0
        Assert.Equal(0.9532, measured.LexicalCoverage, 4);               // D0.1: >= 90%

        Assert.True(GateTwo.Review(Item(PoolGateTwoTests.B1Passage, "B1")).Accepted,
            Rules(GateTwo.Review(Item(PoolGateTwoTests.B1Passage, "B1"))));
    }

    /// <summary>
    /// Every refusal names the number that failed, so the author is told what to change. A gate that
    /// only says "out of band" sends the item back unchanged.
    /// </summary>
    [Fact]
    public void Every_measurement_is_reported_beside_the_verdict()
    {
        var verdict = GateTwo.Review(Item(Cat(1, 40), "A2"));

        Assert.NotEmpty(verdict.Measurements);
        Assert.Contains(verdict.Measurements, m => m.Name == "words" && m.Value == "40");
        Assert.Contains(verdict.Measurements, m => m.Name == "mltu");
        Assert.Contains(verdict.Measurements, m => m.Name == "clauses_per_t_unit");
        Assert.Contains(verdict.Measurements, m => m.Name == "coverage");
    }
}
