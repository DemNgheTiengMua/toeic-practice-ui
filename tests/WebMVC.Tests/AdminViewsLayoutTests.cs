using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace WebMVC.Tests;

public sealed class AdminViewsLayoutTests
{



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
    public void Imports_View_MustBeRetired()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var importsPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin", "Imports.cshtml");

        Assert.False(File.Exists(importsPath), "Imports.cshtml should be retired in favor of integrated exam import on /admin/exams");
    }

    [Fact]
    public void ComponentsCss_MustContainModalOverlayStandardsAndFieldErrorStyles()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var cssPath = Path.Combine(repoRoot, "src", "WebMVC", "wwwroot", "css", "components.css");

        Assert.True(File.Exists(cssPath), "components.css must exist");
        var css = File.ReadAllText(cssPath);

        // Modal overlay standardized styling
        Assert.Matches(@"\.modal-overlay\s*\{[^}]*display\s*:\s*none", css);
        Assert.Matches(@"\.modal-overlay\.modal-overlay--open,\s*\.modal-overlay\[aria-hidden=""false""\]\s*\{[^}]*display\s*:\s*grid\s*!important", css);

        // Field error styling
        Assert.Matches(@"\.field--error\s+\.field__input,\s*\.field__input\.is-invalid,\s*\.field__input:invalid:not\(:placeholder-shown\)\s*\{[^}]*border-color\s*:\s*var\(--sign-red\)\s*!important", css);
        Assert.Matches(@"\.field__error-placard\s*\{[^}]*color\s*:\s*var\(--sign-red\)", css);
        Assert.Matches(@"\.field--error\s+\.field__error-placard[^{]*\{[^}]*display\s*:\s*block", css);
    }
}
