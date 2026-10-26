using Contracts;

namespace WebMVC.Models;

public sealed record ExamResumeViewModel(
    string? AuthorizationCode = null,
    string? ErrorMessage = null);
