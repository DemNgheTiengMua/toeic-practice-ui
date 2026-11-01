namespace Contracts;

public sealed record MicroCheckRequest(string SelectedOption);

/// <summary>#155: the graded micro-check. <paramref name="CorrectOption"/> is revealed only here - the
/// lesson page no longer carries it for the browser to grade against.</summary>
public sealed record MicroCheckResultDto(
    bool IsCorrect,
    string CorrectOption,
    string? Explanation,
    int? CategoryId);
