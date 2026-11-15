using System.ComponentModel.DataAnnotations;

namespace WebAPI.Endpoints;

public sealed record PaginationQuery(
    [property: Range(1, int.MaxValue, ErrorMessage = "Page must be at least 1.")]
    int? Page = null,
    [property: Range(1, 100, ErrorMessage = "Page size must be between 1 and 100.")]
    int? PageSize = null)
{
    public int PageOrDefault => Page ?? 1;
    public int PageSizeOrDefault => PageSize ?? 20;
}
