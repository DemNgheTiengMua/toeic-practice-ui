using Contracts;

namespace WebMVC.Models;

public sealed record ExamIndexViewModel(MockExamListViewDto Catalog, string? ErrorMessage = null)
{
    public bool HasCredits { get; } = Catalog.CreditBalance > 0;
}
