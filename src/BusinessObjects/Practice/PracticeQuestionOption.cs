using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

public sealed record PracticeQuestionOption(
    int OptionId,
    string Label,
    string Text);
