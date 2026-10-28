using Xunit;

namespace WebMVC.Tests;

public sealed class DiagnosticPresentationArchitectureTests
{
    [Fact]
    public void Diagnostic_views_use_display_models_and_external_assets()
    {
        var root = TestPathHelper.FindRepoRoot();
        foreach (var area in new[] { "Practice", "SavedQuestions", "WeaknessAnalysis" })
        foreach (var path in Directory.GetFiles(Path.Combine(root, "src/WebMVC/Views", area), "*.cshtml"))
        {
            var view = File.ReadAllText(path);
            Assert.DoesNotContain("@inject", view);
            Assert.DoesNotContain("<style>", view);
            Assert.DoesNotContain("<script>", view);
            Assert.DoesNotContain(".GroupBy(", view);
            Assert.DoesNotMatch(@"new\s+[\w.]+(?:Dto|Model)\s*\(", view);
            Assert.DoesNotContain("ViewBag.Insight", view);
            Assert.True(File.ReadLines(path).Count() <= 450, path);
        }
    }
    [Theory]
    [InlineData("Practice", "PracticeDtos.cs", "PracticeSectionDto")]
    [InlineData("SavedQuestions", "SavedQuestionDtos.cs", "SavedQuestionItemDto")]
    [InlineData("WeaknessAnalysis", "WeaknessDtos.cs", "WeaknessAnalysisDto")]
    [InlineData("Ai", "AiInsightDtos.cs", "AiInsightDto")]
    public void Diagnostic_contracts_have_focused_domain_units(string domain, string oldBundle, string primaryType)
    {
        var contracts = Path.Combine(TestPathHelper.FindRepoRoot(), "src/Contracts");
        Assert.True(File.Exists(Path.Combine(contracts, domain, primaryType + ".cs")));
        if (File.Exists(Path.Combine(contracts, oldBundle)))
            Assert.DoesNotContain("public sealed record", File.ReadAllText(Path.Combine(contracts, oldBundle)));
    }

    [Theory]
    [InlineData("Practice", "GetPracticeCatalogAsync")]
    [InlineData("SavedQuestions", "GetSavedQuestionsAsync")]
    [InlineData("WeaknessAnalysis", "GetWeaknessAnalysisAsync")]
    [InlineData("AiInsights", "GetDashboardInsightAsync")]
    public void Diagnostic_client_methods_live_in_domain_partials(string domain, string method)
    {
        var contracts = Path.Combine(TestPathHelper.FindRepoRoot(), "src/Contracts");
        Assert.DoesNotContain(method, File.ReadAllText(Path.Combine(contracts, "CompanionApiClient.cs")));
        var partial = Path.Combine(contracts, "Clients", $"CompanionApiClient.{domain}.cs");
        Assert.True(File.Exists(partial));
        Assert.Contains(method, File.ReadAllText(partial));
    }

    [Fact]
    public void Practice_question_composes_explanation_and_transcript_partials()
    {
        var folder = Path.Combine(TestPathHelper.FindRepoRoot(), "src/WebMVC/Views/Practice");
        var question = File.ReadAllText(Path.Combine(folder, "_TakeQuestion.cshtml"));
        Assert.Contains("_TakeExplanation", question);
        Assert.Contains("_TakeTranscript", question);
    }
    [Fact]
    public void Insight_panel_renders_prepared_display_state()
    {
        var path = Path.Combine(TestPathHelper.FindRepoRoot(), "src/WebMVC/Views/Shared/_AiInsightPanel.cshtml");
        var view = File.ReadAllText(path);
        Assert.DoesNotContain(" switch", view);
        Assert.DoesNotContain(".ToWireCode()", view);
        Assert.DoesNotContain(".ToString(", view);
    }
}
