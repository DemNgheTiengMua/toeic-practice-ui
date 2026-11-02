using System;
using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>#158: one question's answer key, exposed only on the admin preview path.</summary>
public sealed record ExamPreviewAnswer(
    int QuestionId,
    IReadOnlyList<int> CorrectOptionIds,
    string? Explanation);

/// <summary>#158: a jump target for the preview's section selector.</summary>
public sealed record ExamPreviewSection(
    int SectionId,
    string SectionCode,
    string SectionName,
    int FirstDisplayOrder,
    int LastDisplayOrder,
    int QuestionCount);

/// <summary>
/// #158: the real sitting projection plus the two things only an admin preview may see — the answer
/// key and the exam's status. No <c>ExamAttempt</c> is involved, so nothing here can be written.
/// </summary>
public sealed record ExamPreviewTakeView(
    AttemptTakeView Take,
    IReadOnlyList<ExamPreviewAnswer> Answers,
    IReadOnlyList<ExamPreviewSection> Sections,
    int DurationMinutes,
    string ExamStatus);
