using Contracts;

namespace WebMVC.Models;

public sealed record LessonViewModel(
    LessonDetailDto Lesson,
    LessonProgressDto Progress,
    int? ModuleId = null
);
