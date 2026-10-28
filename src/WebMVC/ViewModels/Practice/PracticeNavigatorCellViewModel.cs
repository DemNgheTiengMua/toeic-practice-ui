using Contracts;

namespace WebMVC.Models;

public sealed record PracticeNavigatorCellViewModel(PracticeNavigatorItemDto Item, string CssClass, string Title);
