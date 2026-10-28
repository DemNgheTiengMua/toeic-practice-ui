using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Http;

namespace WebMVC.Models;

public sealed record AdminExamListViewModel(
    AdminExamListSummaryDto Summary,
    string? Query,
    string? Status,
    int? CertId,
    int Page,
    string? Purpose = null,
    string? ErrorMessage = null,
    string? SuccessMessage = null)
{
    public IReadOnlyList<AdminExamRowViewModel> Rows { get; } = Summary.Exams.Select(exam => new AdminExamRowViewModel(exam)).ToArray();
    public int TotalPages { get; } = Math.Max(1, (int)Math.Ceiling((double)Summary.TotalCount / Summary.PageSize));
    public int FirstRow { get; } = ((Summary.Page - 1) * Summary.PageSize) + 1;
    public int LastRow { get; } = Math.Min(Summary.Page * Summary.PageSize, Summary.TotalCount);
    public string FilterQuery { get; } = $"&q={Query}&status={Status}" +
        (CertId.HasValue ? $"&certId={CertId.Value}" : "") +
        (!string.IsNullOrEmpty(Purpose) ? $"&purpose={Purpose}" : "");
}
