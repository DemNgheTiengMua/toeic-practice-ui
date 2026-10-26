using Contracts;

namespace WebMVC.Models;

public sealed record SaveAnswerFormModel(
    int? SelectedOptionId,
    bool IsFlagged,
    string? ResponseText = null);
