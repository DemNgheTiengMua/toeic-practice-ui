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
    public void Imports_View_MustBeRetired()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var importsPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin", "Imports.cshtml");

        Assert.False(File.Exists(importsPath), "Imports.cshtml should be retired in favor of integrated exam import on /admin/exams");
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

    [Fact]
    public void ComplaintDetailView_MustContainValidationErrorPlacardsAndClientValidation()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin", "ComplaintDetail.cshtml");

        Assert.True(File.Exists(viewPath), "ComplaintDetail.cshtml must exist");
        var content = File.ReadAllText(viewPath);

        // Form has error placards
        Assert.Contains("field__error-placard", content);
        Assert.Contains("field--error", content);

        // Script handles form submit and validates resolution + internalNotes
        Assert.Contains("resolution", content);
        Assert.Contains("internalNotes", content);
        Assert.Contains("aria-invalid", content);
    }

    [Fact]
    public void WritingTakeView_SubmitModal_MustHaveAccessibleOverlayAndKeyboardDismiss()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Writing", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Writing/Take.cshtml must exist");
        var content = File.ReadAllText(viewPath);

        // Markup for #submit-modal has modal-overlay with aria-hidden="true", role="dialog", aria-modal="true"
        Assert.Matches(@"<div\s+id=""submit-modal""\s+class=""modal-overlay""\s+aria-hidden=""true""\s+role=""dialog""\s+aria-modal=""true""", content);

        // Script toggles modal-overlay--open and aria-hidden
        Assert.Contains("modal-overlay--open", content);
        Assert.Contains(@"setAttribute('aria-hidden'", content);

        // Script handles Escape key and backdrop click
        Assert.Contains("Escape", content);
        Assert.Contains("submitModal", content);
    }

    [Fact]
    public void SpeakingTakeView_SubmitModal_MustHaveAccessibleOverlayAndKeyboardDismiss()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var viewPath = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Speaking", "Take.cshtml");

        Assert.True(File.Exists(viewPath), "Speaking/Take.cshtml must exist");
        var content = File.ReadAllText(viewPath);

        // Markup for #submit-modal has modal-overlay with aria-hidden="true", role="dialog", aria-modal="true"
        Assert.Matches(@"<div\s+id=""submit-modal""\s+class=""modal-overlay""\s+aria-hidden=""true""\s+role=""dialog""\s+aria-modal=""true""", content);

        // Script toggles modal-overlay--open and aria-hidden
        Assert.Contains("modal-overlay--open", content);
        Assert.Contains(@"setAttribute('aria-hidden'", content);

        // Script handles Escape key and backdrop click
        Assert.Contains("Escape", content);
        Assert.Contains("submitModal", content);
    }

    [Fact]
    public void AdminViews_CertificatesAndWizard_MustDeclareAdminLayout()
    {
        var repoRoot = TestPathHelper.FindRepoRoot();
        var adminViewsDir = Path.Combine(repoRoot, "src", "WebMVC", "Views", "Admin");

        var certsView = Path.Combine(adminViewsDir, "Certificates.cshtml");
        var wizardView = Path.Combine(adminViewsDir, "CertificateWizard.cshtml");

        Assert.True(File.Exists(certsView), "Certificates.cshtml must exist");
        Assert.True(File.Exists(wizardView), "CertificateWizard.cshtml must exist");

        var certsContent = File.ReadAllText(certsView);
        var wizardContent = File.ReadAllText(wizardView);

        Assert.Matches(@"Layout\s*=\s*""_AdminLayout"";?", certsContent);
        Assert.Matches(@"Layout\s*=\s*""_AdminLayout"";?", wizardContent);
    }
}
