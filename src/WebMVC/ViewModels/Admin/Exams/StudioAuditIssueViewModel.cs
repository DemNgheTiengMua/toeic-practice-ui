using Contracts;
namespace WebMVC.Models;

public sealed class StudioAuditIssueViewModel(ExamAuditIssueDto issue)
{
    public ExamAuditIssueDto Issue { get; } = issue;
    public string ItemClass { get; } = issue.Severity == "error" ? "audit-item--error" : "audit-item--warning";
    public string Icon { get; } = issue.Severity == "error" ? "&#10008;" : "&#9888;";
}
