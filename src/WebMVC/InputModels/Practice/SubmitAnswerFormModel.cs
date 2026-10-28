using Contracts;

namespace WebMVC.Models;

public sealed class SubmitAnswerFormModel
{
    public int? SelectedOptionId { get; set; }

    public string? ResponseText { get; set; }

    public string? SelectedOptionIdsJson { get; set; }

    public List<int>? SelectedOptionIds { get; set; }

    public string? Action { get; set; }
}
