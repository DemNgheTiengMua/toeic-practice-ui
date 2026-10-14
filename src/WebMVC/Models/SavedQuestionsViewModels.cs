using Contracts;

namespace WebMVC.Models;

public sealed record SavedQuestionsIndexViewModel(
    SavedQuestionsViewDto Data,
    string? CurrentSource = null,
    int? CurrentSectionId = null,
    int? CurrentCategoryId = null,
    string? ErrorMessage = null);

public sealed record SavedQuestionReviewViewModel(
    SavedQuestionItemDto Question,
    string? ErrorMessage = null);
