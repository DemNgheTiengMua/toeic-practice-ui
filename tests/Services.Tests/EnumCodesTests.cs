using System.Text.Json;
using System.Text.RegularExpressions;
using BusinessObjects;
using Contracts;
using Xunit;

namespace Services.Tests;

/// <summary>The status enums stand in for the schema's CHECK-constrained codes (issue #2). These
/// tests catch drift three ways: an enum vs its CHECK constraint, a wire mirror vs its domain
/// enum, and the JSON a wire enum serialises as.</summary>
public sealed class EnumCodesTests
{
    public static TheoryData<Type, Type, string> Enums => new()
    {
        { typeof(Modality), typeof(ModalityDto), "CK_Skills_Modality" },
        { typeof(QuestionType), typeof(QuestionTypeDto), "CK_Questions_Type" },
        { typeof(ExamStatus), typeof(ExamStatusDto), "CK_Exams_Status" },
        { typeof(ExamPurpose), typeof(ExamPurposeDto), "CK_Exams_Purpose" },
        { typeof(AttemptStatus), typeof(AttemptStatusDto), "CK_Attempts_Status" },
        { typeof(PlacementStatus), typeof(PlacementStatusDto), "CK_Placement_Status" },
        { typeof(FreeResponseStatus), typeof(FreeResponseStatusDto), "CK_FreeResp_Status" },
        { typeof(AiGradingStatus), typeof(AiGradingStatusDto), "CK_AiGradings_Status" },
        { typeof(AiInsightScope), typeof(AiInsightScopeDto), "CK_AiInsights_Scope" },
        { typeof(AiInsightStatus), typeof(AiInsightStatusDto), "CK_AiInsights_Status" },
        { typeof(AiInsightTrend), typeof(AiInsightTrendDto), "CK_AiInsights_Trend" },
        { typeof(PracticeStatus), typeof(PracticeStatusDto), "CK_Practice_Status" },
        { typeof(QuestionSource), typeof(QuestionSourceDto), "CK_Practice_Source" },
        { typeof(FeedbackMode), typeof(FeedbackModeDto), "CK_Practice_Feedback" },
        { typeof(SavedQuestionSource), typeof(SavedQuestionSourceDto), "CK_SavedQ_Source" },
        { typeof(RecommendationSeverity), typeof(RecommendationSeverityDto), "CK_Reco_Severity" },
        { typeof(OrderStatus), typeof(OrderStatusDto), "CK_Orders_Status" },
        { typeof(PaymentGateway), typeof(PaymentGatewayDto), "CK_Orders_Gateway" },
        { typeof(PaymentEventSource), typeof(PaymentEventSourceDto), "CK_PaymentEvents_Source" },
        { typeof(GatewayStatus), typeof(GatewayStatusDto), "CK_PaymentEvents_ReportedStatus" },
        { typeof(CreditReason), typeof(CreditReasonDto), "CK_CreditTxn_Reason" },
        { typeof(LearnerPathStatus), typeof(LearnerPathStatusDto), "CK_LearnerPaths_Status" },
        { typeof(LearnerPathStepStatus), typeof(LearnerPathStepStatusDto), "CK_Steps_Status" },
        { typeof(PathModuleItemType), typeof(PathModuleItemTypeDto), "CK_PMI_Type" },
    };


    [Theory]
    [MemberData(nameof(Enums))]
    public void Domain_enum_codes_match_the_schema_check_constraint(Type domain, Type wire, string constraint)
    {
        _ = wire;
        var schema = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "database-schema.sql"));
        var match = Regex.Match(schema, $@"CONSTRAINT {constraint} CHECK \(\w+ IN\s*\((?<codes>[^)]*)\)");
        Assert.True(match.Success, $"{constraint} not found in docs/database-schema.sql");
        var schemaCodes = Regex.Matches(match.Groups["codes"].Value, "'([a-z_]+)'").Select(m => m.Groups[1].Value).Order();

        var enumCodes = Enum.GetValues(domain).Cast<Enum>().Select(Code).Order();

        Assert.Equal(schemaCodes, enumCodes);
    }

    [Theory]
    [MemberData(nameof(Enums))]
    public void Wire_mirror_has_the_same_members_and_serialises_as_the_database_code(Type domain, Type wire, string constraint)
    {
        _ = constraint;
        Assert.Equal(Enum.GetNames(domain), Enum.GetNames(wire));

        foreach (Enum value in Enum.GetValues(domain))
        {
            var wireValue = (Enum)Enum.Parse(wire, value.ToString());
            Assert.Equal($"\"{Code(value)}\"", JsonSerializer.Serialize(wireValue, wire));
            Assert.Equal(wireValue, JsonSerializer.Deserialize($"\"{Code(value)}\"", wire));
        }
    }

    [Fact]
    public void Parse_rejects_an_unknown_code_and_TryParse_ignores_case()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EnumCodes.Parse<OrderStatus>("Completed"));
        Assert.True(EnumCodes.TryParse<QuestionSource>("WRONG_BEFORE", out var source));
        Assert.Equal(QuestionSource.WrongBefore, source);
        Assert.False(EnumCodes.TryParse<QuestionSource>("", out _));
    }

    // EnumCodes.ToCode is generic; the theories hold the enum as a boxed Enum.
    private static string Code(Enum value) =>
        (string)typeof(EnumCodes).GetMethod(nameof(EnumCodes.ToCode))!
            .MakeGenericMethod(value.GetType())
            .Invoke(null, [value])!;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExtraEfficiency.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
