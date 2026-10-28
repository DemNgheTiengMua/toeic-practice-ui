using Contracts;

namespace WebMVC.Models;

public sealed record SaveWritingDraftAjaxModel(
    int AttemptId,
    int QuestionId,
    string? ResponseText);
