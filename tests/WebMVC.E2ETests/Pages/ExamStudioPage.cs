using System.Threading.Tasks;
using Microsoft.Playwright;

namespace WebMVC.E2ETests.Pages;

/// <summary>
/// Page Object Model encapsulating the 3-panel Admin Exam Studio shell.
/// </summary>
public class ExamStudioPage : BasePage
{
    public ILocator LeftRail => Page.Locator("#studio-left-rail");
    public ILocator CenterWorkstation => Page.Locator("#studio-workstation");
    public ILocator RightAudit => Page.Locator("#studio-right-audit");
    public ILocator RailTotalCount => Page.Locator("#rail-total-count");
    public ILocator RailSections => Page.Locator(".rail-section");

    // Quick Ingest Modal
    public ILocator QuickIngestButton => Page.Locator("#btn-studio-quick-ingest");
    public ILocator QuickIngestModal => Page.Locator("#quick-ingest-modal");
    public ILocator QuickIngestSampleButton => Page.Locator("#qi-sample-btn");
    public ILocator QuickIngestRawTextArea => Page.Locator("#qi-raw-text");
    public ILocator QuickIngestParseButton => Page.Locator("#qi-parse-btn");
    public ILocator QuickIngestPreviewContainer => Page.Locator("#qi-preview-container");
    public ILocator QuickIngestPreviewRows => Page.Locator("#qi-preview-body tr");
    public ILocator QuickIngestCloseButton => Page.Locator("#quick-ingest-close");

    // Audit Panel & Exports
    public ILocator AuditStatusBadge => Page.Locator("#audit-status-badge");
    public ILocator AuditQuestionCount => Page.Locator("#audit-q-count");
    public ILocator ExportZipButton => Page.Locator("#btn-export-exam");
    public ILocator ExportJsonButton => Page.Locator("#btn-export-json");

    public ExamStudioPage(IPage page, string baseUrl = "http://localhost:5000") : base(page, baseUrl)
    {
    }

    public async Task GotoAsync(int examId)
    {
        await NavigateAsync($"/admin/exams/{examId}/studio");
    }
}
