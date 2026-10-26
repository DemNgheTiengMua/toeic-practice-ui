using System;
using System.Linq;
using Contracts;
using DataAccess.Scaffold;
using Services;
using Xunit;

namespace Services.Tests;

public sealed class ExamQuickIngestServiceTests
{
    private readonly ExamQuickIngestService _parser = new();

    [Fact]
    public void EmptyOrWhitespaceInput_ReturnsErrorWithLineNumber()
    {
        var result = _parser.Parse(new QuickIngestParseRequest("   "));
        Assert.False(result.Success);
        Assert.Equal(0, result.TotalQuestions);
        Assert.True(result.ErrorCount > 0);
        Assert.Contains(result.GlobalIssues, i => i.Severity == "error");
    }

    [Fact]
    public void StandardAsteriskFormat_ParsesQuestionsAndOptionsCorrectly()
    {
        var raw = """
            1. Ms. Tanaka requested that all department managers submit their quarterly budgets by _____ Friday.
            *A. next
            B. nearly
            C. soon
            D. highly
            EXPLANATION: The adjective 'next' correctly precedes a day of the week to indicate future time.

            Question 2: Visitors to the research facility must check in at the front desk.
            A) Always
            *B) Promptly
            C) Slowly
            D) Loudly
            GIẢI THÍCH: Trạng từ 'promptly' bổ nghĩa cho động từ check in.
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.True(result.Success, string.Join("; ", result.GlobalIssues.Concat(result.Questions.SelectMany(q => q.Issues)).Select(i => $"L{i.LineNumber}: {i.Message}")));
        Assert.Equal(2, result.TotalQuestions);
        Assert.Equal(2, result.ValidQuestions);
        Assert.Equal(0, result.ErrorCount);

        var q1 = result.Questions[0];
        Assert.Contains("Tanaka", q1.Stem);
        Assert.Equal(4, q1.Options.Count);
        Assert.True(q1.Options[0].IsCorrect);
        Assert.False(q1.Options[1].IsCorrect);
        Assert.Equal("A", q1.Options[0].Label);
        Assert.Equal("next", q1.Options[0].Text);
        Assert.NotNull(q1.Explanation);
        Assert.Contains("future time", q1.Explanation);

        var q2 = result.Questions[1];
        Assert.Contains("Visitors", q2.Stem);
        Assert.Equal(4, q2.Options.Count);
        Assert.False(q2.Options[0].IsCorrect);
        Assert.True(q2.Options[1].IsCorrect);
        Assert.Equal("B", q2.Options[1].Label);
        Assert.NotNull(q2.Explanation);
        Assert.Contains("Trạng từ", q2.Explanation);
    }

    [Fact]
    public void TrailingAnswerKey_SetsCorrectOption()
    {
        var raw = """
            Câu 1: The proposal was accepted _____ the committee had reviewed all feedback.
            (A) although
            (B) because
            (C) after
            (D) while
            ANSWER: C
            CATEGORY: conjunctions
            EXPLANATION: 'After' shows sequence of events.
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.True(result.Success);
        Assert.Single(result.Questions);

        var q = result.Questions[0];
        Assert.True(q.IsValid);
        Assert.Equal("conjunctions", q.CategoryCode);
        Assert.Equal("C", q.Options.Single(o => o.IsCorrect).Label);
        Assert.Equal("after", q.Options.Single(o => o.IsCorrect).Text);
        Assert.Equal("'After' shows sequence of events.", q.Explanation);
    }

    [Fact]
    public void TrailingVietnameseKey_SetsCorrectOption()
    {
        var raw = """
            1) All employees are invited to the annual gala.
            A. True
            B. False
            ĐÁP ÁN: A
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.True(result.Success);
        Assert.Single(result.Questions);
        var q = result.Questions[0];
        Assert.True(q.IsValid);
        Assert.Equal("A", q.Options.Single(o => o.IsCorrect).Label);
    }

    [Fact]
    public void MissingCorrectAnswer_FlagsErrorWithLineNumber()
    {
        var raw = """
            1. Choose the correct answer.
            A. Option 1
            B. Option 2
            C. Option 3
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.False(result.Success);
        Assert.Equal(1, result.TotalQuestions);
        Assert.Equal(0, result.ValidQuestions);
        Assert.True(result.ErrorCount > 0);

        var q = result.Questions[0];
        Assert.False(q.IsValid);
        Assert.Contains(q.Issues, i => i.Severity == "error" && i.Message.Contains("no correct answer"));
    }

    [Fact]
    public void MultipleCorrectAnswers_FlagsError()
    {
        var raw = """
            1. Choose the correct answer.
            *A. Option 1
            *B. Option 2
            C. Option 3
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.False(result.Success);
        var q = result.Questions[0];
        Assert.False(q.IsValid);
        Assert.Contains(q.Issues, i => i.Severity == "error" && i.Message.Contains("multiple correct answers"));
    }

    [Fact]
    public void FewerThanTwoOptions_FlagsError()
    {
        var raw = """
            1. Only one option provided.
            *A. Lone option
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.False(result.Success);
        var q = result.Questions[0];
        Assert.False(q.IsValid);
        Assert.Contains(q.Issues, i => i.Severity == "error" && i.Message.Contains("at least 2 options"));
    }

    [Fact]
    public void EmptyStem_FlagsError()
    {
        var raw = """
            *A. Option 1
            B. Option 2
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.False(result.Success);
        var q = result.Questions[0];
        Assert.False(q.IsValid);
        Assert.Contains(q.Issues, i => i.Severity == "error" && i.Message.Contains("stem is empty"));
    }

    [Fact]
    public void JsonFallback_ParsesValidArray()
    {
        var json = """
            [
              {
                "stem": "The shipment will arrive on time _____ weather conditions worsen.",
                "difficultyLevel": 4,
                "categoryCode": "grammar_cond",
                "explanation": "Unless introduces an exception condition.",
                "options": [
                  { "label": "A", "text": "unless", "isCorrect": true, "rationale": "Correct conditional conjunction." },
                  { "label": "B", "text": "despite", "isCorrect": false },
                  { "label": "C", "text": "whereas", "isCorrect": false },
                  { "label": "D", "text": "provided", "isCorrect": false }
                ]
              }
            ]
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(json));

        Assert.True(result.Success);
        Assert.Single(result.Questions);
        var q = result.Questions[0];
        Assert.True(q.IsValid);
        Assert.Equal(4, q.DifficultyLevel);
        Assert.Equal("grammar_cond", q.CategoryCode);
        Assert.Equal("unless", q.Options.Single(o => o.IsCorrect).Text);
        Assert.Equal("Correct conditional conjunction.", q.Options.Single(o => o.IsCorrect).Rationale);
    }

    [Fact]
    public void JsonFallback_MalformedJsonReportsLineError()
    {
        var badJson = """
            [
              {
                "stem": "Broken json missing closing brace"
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(badJson));

        Assert.False(result.Success);
        Assert.Contains(result.GlobalIssues, i => i.Severity == "error" && i.Message.Contains("JSON"));
    }

    // ================= STEP 06 NEW MULTI-FORMAT & BLUEPRINT TESTS =================

    [Fact]
    public void RapidAnswerKey_ParsesCleanlyAndGeneratesOptions()
    {
        var raw = """
            1. A
            TRANSCRIPT: Where is the nearest bus stop?
            2. C
            TRANSCRIPT: When does the flight to Tokyo depart?
            3. B
            EXPLANATION: Choice B directly answers the question.
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.True(result.Success);
        Assert.Equal(3, result.TotalQuestions);
        Assert.Equal(3, result.ValidQuestions);

        var q1 = result.Questions[0];
        Assert.Equal(1, q1.Index);
        Assert.Equal("A", q1.Options.Single(o => o.IsCorrect).Label);
        Assert.Equal("Where is the nearest bus stop?", q1.Transcript);

        var q2 = result.Questions[1];
        Assert.Equal(2, q2.Index);
        Assert.Equal("C", q2.Options.Single(o => o.IsCorrect).Label);

        var q3 = result.Questions[2];
        Assert.Equal(3, q3.Index);
        Assert.Equal("B", q3.Options.Single(o => o.IsCorrect).Label);
        Assert.Equal("Choice B directly answers the question.", q3.Explanation);
    }

    [Fact]
    public void ClusteredPassageMode_GroupsQuestionsAndAttachesPassage()
    {
        var raw = """
            PASSAGE: GreenLeaf Logistics has announced an expansion of its refrigerated courier services to meet rising demand from organic food producers. The fleet will add 25 electric delivery vans by the end of Q3.

            1. What is the main purpose of the announcement?
            *A. To announce service expansion
            B. To advertise job openings
            C. To explain rate increases
            D. To report financial earnings

            2. How many new vehicles will be added?
            A. 10
            B. 15
            *C. 25
            D. 50
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalQuestions);

        Assert.All(result.Questions, q =>
        {
            Assert.NotNull(q.Passage);
            Assert.Contains("GreenLeaf Logistics", q.Passage);
        });

        Assert.Equal("A", result.Questions[0].Options.Single(o => o.IsCorrect).Label);
        Assert.Equal("C", result.Questions[1].Options.Single(o => o.IsCorrect).Label);
    }

    [Fact]
    public void InlineCompletionMode_ParsesAcceptedVariantsAndMaxWords()
    {
        var raw = """
            1. What time does the botanical garden open each day?
            ANSWER: 9:00 AM | 9 AM | 9am
            MAX_WORDS: 2

            2. What document is required for free admission?
            ANSWER: student ID | university identification | valid ID
            MAX_WORDS: 2
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalQuestions);

        var q1 = result.Questions[0];
        Assert.Equal("inline_completion", q1.QuestionType);
        Assert.Equal(2, q1.MaxWords);
        Assert.NotNull(q1.AcceptedAnswersJson);
        Assert.Contains("9:00 AM", q1.AcceptedAnswersJson);

        var q2 = result.Questions[1];
        Assert.Equal("inline_completion", q2.QuestionType);
        Assert.Contains("student ID", q2.AcceptedAnswersJson);
    }

    [Fact]
    public void MultiSelectMode_AllowsMultipleCorrectOptions()
    {
        var raw = """
            1. Which documents must researchers submit before October 15?
            *A. Safety audit
            B. Financial budget
            *C. Equipment inventory
            D. Travel reimbursement
            ANSWER: A, C
            MAX_SELECTIONS: 2
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.True(result.Success);
        Assert.Single(result.Questions);

        var q = result.Questions[0];
        Assert.Equal("multi_select", q.QuestionType);
        Assert.Equal(2, q.MaxSelections);
        Assert.Equal(2, q.Options.Count(o => o.IsCorrect));
        Assert.True(q.Options.Single(o => o.Label == "A").IsCorrect);
        Assert.True(q.Options.Single(o => o.Label == "C").IsCorrect);
        Assert.False(q.Options.Single(o => o.Label == "B").IsCorrect);
    }

    [Fact]
    public void KeywordFreeResponseMode_ParsesMandatoryKeywords()
    {
        var raw = """
            PROMPT: Write a sentence based on the photograph using the two keywords provided.
            KEYWORDS: photograph, yesterday
            MIN_WORDS: 5
            MAX_WORDS: 30

            PROMPT: Write a sentence based on the photograph using the two keywords provided.
            KEYWORDS: airport, luggage
            MIN_WORDS: 5
            MAX_WORDS: 30
            """;

        var result = _parser.Parse(new QuickIngestParseRequest(raw));

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalQuestions);

        var q1 = result.Questions[0];
        Assert.Equal("free_response", q1.QuestionType);
        Assert.Equal("photograph, yesterday", q1.RequiredKeywords);
        Assert.Equal(5, q1.MinWords);
        Assert.Equal(30, q1.MaxWords);

        var q2 = result.Questions[1];
        Assert.Equal("free_response", q2.QuestionType);
        Assert.Equal("airport, luggage", q2.RequiredKeywords);
    }

    [Fact]
    public void BlueprintValidation_RejectsOptionBeyondOptionCount()
    {
        // TOEIC Part 2 Section has OptionCount = 3 (A, B, C)
        var section = new Section
        {
            SectionId = 10,
            Name = "Question-Response",
            Code = "P2",
            Modality = "listening",
            OptionCount = 3,
            AllowedComponentTypesJson = "[\"mcq_3\",\"mcq\"]"
        };

        var raw = """
            1. Where is the meeting?
            A. In the lobby
            B. At 3 PM
            C. Yes, I did
            D. Next Friday
            ANSWER: D
            """;

        var parsed = _parser.Parse(new QuickIngestParseRequest(raw));
        var validated = ExamQuickIngestService.ValidateAgainstSectionBlueprint(parsed, section);

        Assert.False(validated.Success);
        Assert.Single(validated.Questions);
        var q = validated.Questions[0];
        Assert.False(q.IsValid);
        Assert.Contains(q.Issues, i => i.Message.Contains("Option 'D' exceeds section allowed OptionCount"));
    }

    [Fact]
    public void BlueprintValidation_RejectsUnallowedComponentType()
    {
        // Section allows only MCQ
        var section = new Section
        {
            SectionId = 20,
            Name = "Incomplete Sentences",
            Code = "P5",
            Modality = "reading",
            OptionCount = 4,
            AllowedComponentTypesJson = "[\"mcq\"]"
        };

        var raw = """
            PROMPT: Write an essay about global warming.
            KEYWORDS: climate, emissions
            """;

        var parsed = _parser.Parse(new QuickIngestParseRequest(raw));
        var validated = ExamQuickIngestService.ValidateAgainstSectionBlueprint(parsed, section);

        Assert.False(validated.Success);
        Assert.Single(validated.Questions);
        var q = validated.Questions[0];
        Assert.False(q.IsValid);
        Assert.Contains(q.Issues, i => i.Message.Contains("is not allowed in section"));
    }

    [Fact]
    public void BlueprintValidation_RejectsKeywordCountMismatch()
    {
        // Section requires exactly 2 keywords (e.g. TOEIC Writing Q1-5)
        var section = new Section
        {
            SectionId = 30,
            Name = "Write a Sentence",
            Code = "W1",
            Modality = "writing",
            RequiredKeywordCount = 2,
            AllowedComponentTypesJson = "[\"free_response\"]"
        };

        var raw = """
            PROMPT: Write a sentence based on the picture.
            KEYWORDS: only_one_keyword
            """;

        var parsed = _parser.Parse(new QuickIngestParseRequest(raw));
        var validated = ExamQuickIngestService.ValidateAgainstSectionBlueprint(parsed, section);

        Assert.False(validated.Success);
        Assert.Single(validated.Questions);
        var q = validated.Questions[0];
        Assert.False(q.IsValid);
        Assert.Contains(q.Issues, i => i.Message.Contains("requires 2 keyword(s), but found 1"));
    }

    [Fact]
    public void BlueprintValidation_RejectsMissingPassageWhenRequired()
    {
        var section = new Section
        {
            SectionId = 40,
            Name = "Reading Passage",
            Code = "P7",
            Modality = "reading",
            RequirePassage = true,
            AllowedComponentTypesJson = "[\"mcq\"]"
        };

        var raw = """
            1. What is the topic?
            *A. First option
            B. Second option
            """;

        var parsed = _parser.Parse(new QuickIngestParseRequest(raw));
        var validated = ExamQuickIngestService.ValidateAgainstSectionBlueprint(parsed, section);

        Assert.False(validated.Success);
        var q = validated.Questions[0];
        Assert.False(q.IsValid);
        Assert.Contains(q.Issues, i => i.Message.Contains("requires a reading passage"));
    }

    [Fact]
    public void SampleTemplateGenerator_ReturnsAppropriateBlueprintSyntax()
    {
        // 1. Keyword mode
        var secKeywords = new Section { RequiredKeywordCount = 2, Modality = "writing" };
        var t1 = ExamQuickIngestService.GenerateSectionSampleTemplate(secKeywords);
        Assert.Contains("KEYWORDS:", t1);
        Assert.Contains("PROMPT:", t1);

        // 2. Inline completion mode
        var secCompletion = new Section { AllowedComponentTypesJson = "[\"inline_completion\"]", Modality = "reading" };
        var t2 = ExamQuickIngestService.GenerateSectionSampleTemplate(secCompletion);
        Assert.Contains("MAX_WORDS:", t2);
        Assert.Contains("ANSWER:", t2);
        Assert.Contains("|", t2);

        // 3. 3-option audio mode
        var secAudio3 = new Section { OptionCount = 3, Modality = "listening" };
        var t3 = ExamQuickIngestService.GenerateSectionSampleTemplate(secAudio3);
        Assert.Contains("1. A", t3);
        Assert.Contains("TRANSCRIPT:", t3);

        // 4. Passage mode
        var secPassage = new Section { RequirePassage = true, OptionCount = 4, Modality = "reading" };
        var t4 = ExamQuickIngestService.GenerateSectionSampleTemplate(secPassage);
        Assert.Contains("PASSAGE:", t4);
    }
}
