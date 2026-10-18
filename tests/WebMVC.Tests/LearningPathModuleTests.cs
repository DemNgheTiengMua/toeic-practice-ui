using System.IO;
using System.Text.RegularExpressions;
using Contracts;
using WebMVC.Models;
using Xunit;

namespace WebMVC.Tests;

public sealed class LearningPathModuleTests
{
    [Theory]
    [InlineData("Module 2: Sentence Structure & AI Speaking Drill", "Module 2: Sentence Structure & AI Speaking Drill")]
    [InlineData("Sentence Structure & AI Speaking Drill", "Module 2: Sentence Structure & AI Speaking Drill")]
    [InlineData("MODULE 2: Sentence Structure & AI Speaking Drill", "Module 2: Sentence Structure & AI Speaking Drill")]
    [InlineData("Module 2:  Sentence Structure & AI Speaking Drill", "Module 2: Sentence Structure & AI Speaking Drill")]
    public void NormalizeHeading_StripsDuplicatedModulePrefix(string inputTitle, string expectedHeading)
    {
        var heading = LearningPathModuleDetailViewModel.NormalizeHeading(2, inputTitle);
        Assert.Equal(expectedHeading, heading);
    }

    [Fact]
    public void FormattedHeading_UsesNormalizedHeadingFromDetail()
    {
        var detail = new LearningPathModuleDetailDto(
            LearnerPathId: 10,
            ModuleId: 2,
            Title: "Module 2: Sentence Structure & AI Speaking Drill",
            GroupName: "Grammar foundations",
            Description: "Module description",
            EstimatedMinutes: 20,
            DisplayOrder: 2,
            SkillCode: "READ",
            SkillName: "Reading",
            Modality: ModalityDto.Reading,
            Status: "in_progress",
            IsLocked: false,
            ModuleIndex: 2,
            TotalModules: 5,
            FromBandCode: "A2",
            ToBandCode: "B2",
            AnsweredQuestions: 15,
            TotalQuestions: 25,
            AccuracyPercentage: 73,
            CorrectCount: 11,
            TotalAnswered: 15,
            TimeSpentMinutes: 12,
            EstimatedMinutesRemaining: 8,
            IsPremium: false,
            Topics: [],
            Steps: [],
            ActiveStepId: 101);

        var vm = new LearningPathModuleDetailViewModel(detail);
        Assert.Equal("Module 2: Sentence Structure & AI Speaking Drill", vm.FormattedHeading);
    }

    [Fact]
    public void ModuleDetailView_UsesNormalizedModuleHeading_InTitleAndH1()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "LearningPath", "ModuleDetail.cshtml");

        Assert.True(File.Exists(viewPath), "ModuleDetail.cshtml must exist");
        var content = File.ReadAllText(viewPath);

        // Ensures normalized heading is obtained via Model.FormattedHeading
        Assert.Contains(@"var moduleHeading = Model.FormattedHeading;", content);
        Assert.Contains(@"ViewData[""Title""] = moduleHeading;", content);
        Assert.Matches(@"<h1[^>]*>\s*@moduleHeading\s*</h1>", content);
    }
}
