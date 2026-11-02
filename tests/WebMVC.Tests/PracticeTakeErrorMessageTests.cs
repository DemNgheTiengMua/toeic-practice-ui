using System.IO;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #166: the practice take view is the one take view that never rendered its error message, so a
/// learner whose browser did not run the AJAX handler was redirected with no explanation. The
/// view model already carried <c>ErrorMessage</c>; nothing printed it.
/// </summary>
public sealed class PracticeTakeErrorMessageTests
{
    [Fact]
    public void The_practice_take_view_renders_the_error_message_it_is_given()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Practice", "Take.cshtml");
        Assert.True(File.Exists(viewPath), "Practice/Take.cshtml must exist");

        var view = File.ReadAllText(viewPath);

        // Both halves matter: the guard property and the markup that prints it. A view that reads
        // Model.ErrorMessage without the guard would render an empty alert on every page.
        Assert.Contains("Model.HasErrorMessage", view, System.StringComparison.Ordinal);
        Assert.Contains("Model.ErrorMessage", view, System.StringComparison.Ordinal);
    }

    [Fact]
    public void The_view_model_reports_whether_it_has_an_error_at_all()
    {
        // The view model builds a presentation object in its constructor, so the two required
        // arguments have to be real. Only the ErrorMessage is under test here.
        var session = new Contracts.PracticeSessionDto(
            SessionId: 1, SectionId: 1, SectionCode: "P5", SectionName: "Part 5",
            Modality: Contracts.ModalityDto.Reading, QuestionCount: 10,
            Status: Contracts.PracticeStatusDto.InProgress, CorrectCount: 0,
            QuestionSource: Contracts.QuestionSourceDto.All, StartedAt: System.DateTimeOffset.UtcNow,
            FinishedAt: null);
        var question = new Contracts.PracticeQuestionDto(
            SessionId: 1, SectionName: "Part 5", QuestionId: 1, DisplayOrder: 1, TotalQuestions: 10,
            Stem: "Stem", CategoryName: null, Hint: null, HintUsed: false,
            Options: [], SelectedOptionId: null, IsCorrect: null, CorrectOptionId: null,
            Explanation: null, Transcript: null, Passage: null, AudioPath: null, ImagePath: null,
            ImageAlt: null);

        Assert.True(new WebMVC.Models.PracticeTakeViewModel(session, question, "Already answered.").HasErrorMessage);
        Assert.False(new WebMVC.Models.PracticeTakeViewModel(session, question, null).HasErrorMessage);
        Assert.False(new WebMVC.Models.PracticeTakeViewModel(session, question, "   ").HasErrorMessage);
    }
}
