using Contracts;
namespace WebMVC.Models;

/// <summary>
/// #240: the leveled lesson reader. A lesson is read one module at a time - the step type decides
/// which - and it opens at the exact card the learner stopped at in THAT module rather than at card
/// one.
/// </summary>
public sealed record LessonViewModel(LessonDetailDto Lesson, LessonProgressDto Progress)
{
    public IReadOnlyList<LessonCardPresentation> Cards { get; } = Lesson.Cards.OrderBy(c => c.DisplayOrder)
        .Select((c, i) => new LessonCardPresentation(c, i + 1)).ToArray();

    /// <summary>#240: the exact card to open on. A finished module re-opens at the end so the learner
    /// sees what they completed; a partial one opens where it stopped.</summary>
    public int InitialIndex => Cards.Count == 0
        ? 1
        : Math.Clamp(Progress.ResumeCardIndex, 1, Cards.Count);

    /// <summary>#240: which module this visit reads, and how a link switches to the other one.</summary>
    public string Module => Lesson.Module;

    public bool IsRemedial => Lesson.IsRemedial;

    public string ModuleTitle => IsRemedial ? "Remedial module" : "Primary module";

    public string OtherModule => IsRemedial ? LessonModuleCodes.Primary : LessonModuleCodes.Remedial;

    public string OtherModuleTitle => IsRemedial ? "Primary module" : "Remedial module";

    public string ModuleUrl(string module) => $"/lessons/{Lesson.LessonId}?module={module}";

    public int TotalCards => Cards.Count;

    public string FinishUrl => "/learning-path";
    public string ReturnUrl => $"/admin/lessons/{Lesson.LessonId}/edit";
}
