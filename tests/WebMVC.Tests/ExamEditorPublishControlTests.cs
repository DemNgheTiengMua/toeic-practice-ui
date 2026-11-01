using System.IO;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// #157: publishing is POST /{id}/publish and nothing else. The exam editor's Status dropdown set
/// Published directly on a 0-question exam in one form post, which is exactly the audit bypass the
/// ticket closes. This test reads the view, so it fails if the control comes back.
/// </summary>
public sealed class ExamEditorPublishControlTests
{
    private static string AdminView(string name) =>
        File.ReadAllText(Path.Combine(TestPathHelper.FindRepoRoot(), "src", "WebMVC", "Views", "Admin", name));

    [Fact]
    public void The_exam_editor_step_1_offers_no_status_control()
    {
        var step1 = AdminView("_ExamEditorStep1.cshtml");

        Assert.DoesNotContain("name=\"Status\"", step1, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<option value=\"Published\"", step1, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExamStatusDto.Published", step1, System.StringComparison.Ordinal);
    }

    [Fact]
    public void The_generate_dialog_offers_no_immediate_publish()
    {
        // AC5: grep -rn "Published (immediately live)" src/ is empty.
        var dialog = AdminView("_ExamGenerateDialog.cshtml");

        Assert.DoesNotContain("Published (immediately live)", dialog, System.StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"status\"", dialog, System.StringComparison.OrdinalIgnoreCase);
    }
}
