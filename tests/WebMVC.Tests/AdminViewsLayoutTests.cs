using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace WebMVC.Tests;

public sealed class AdminViewsLayoutTests
{

    [Fact]
    public void AdminViews_UsersAndOrders_MustDeclareAdminLayout()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var adminViewsDir = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin");

        var usersView = Path.Combine(adminViewsDir, "Users.cshtml");
        var ordersView = Path.Combine(adminViewsDir, "Orders.cshtml");

        Assert.True(File.Exists(usersView), "Users.cshtml must exist");
        Assert.True(File.Exists(ordersView), "Orders.cshtml must exist");

        var usersContent = File.ReadAllText(usersView);
        var ordersContent = File.ReadAllText(ordersView);

        Assert.Matches(@"Layout\s*=\s*""_AdminLayout"";?", usersContent);
        Assert.Matches(@"Layout\s*=\s*""_AdminLayout"";?", ordersContent);
    }

    [Fact]
    public void AdminViews_DirectoryMustContainViewStartWithAdminLayout()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var adminViewStart = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin", "_ViewStart.cshtml");

        Assert.True(File.Exists(adminViewStart), "src/WebMVC/Views/Admin/_ViewStart.cshtml must exist for defense-in-depth");

        var content = File.ReadAllText(adminViewStart);
        Assert.Matches(@"Layout\s*=\s*""_AdminLayout"";?", content);
    }

    [Fact]
    public void QuestionEditor_View_MustBeRetired()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var questionEditorPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin", "QuestionEditor.cshtml");

        Assert.False(File.Exists(questionEditorPath), "QuestionEditor.cshtml should be retired in favor of Exam Studio");
    }

    [Fact]
    public void AdminViews_ExamsAndExamEditor_MustNotLinkToLegacyQuestionEditor()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var adminViewsDir = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin");

        var examsContent = File.ReadAllText(Path.Combine(adminViewsDir, "Exams.cshtml"));
        var editorContent = File.ReadAllText(Path.Combine(adminViewsDir, "ExamEditor.cshtml"));

        // No link to /questions in action buttons or editor steps
        Assert.DoesNotMatch(@"href=""/admin/exams/@exam\.ExamId/questions""", examsContent);
        Assert.DoesNotMatch(@"href=""/admin/exams/@Model\.ExamId/questions""", editorContent);
    }
}
