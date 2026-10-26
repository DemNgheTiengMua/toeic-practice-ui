using Contracts;

namespace WebMVC.Models;

public sealed class ExamSubmitFormModel
{
    public List<SubmitAttemptDraftRequest> Drafts { get; set; } = [];
}
