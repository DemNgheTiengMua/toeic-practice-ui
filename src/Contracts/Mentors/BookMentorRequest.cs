using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record BookMentorRequest(
    int? SlotId = null,
    string? FocusArea = null,
    int? ExamId = null);
