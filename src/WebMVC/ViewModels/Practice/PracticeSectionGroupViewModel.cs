using Contracts;
namespace WebMVC.Models;
public sealed record PracticeSectionGroupViewModel(string Name, IReadOnlyList<PracticeSectionDto> Sections);
