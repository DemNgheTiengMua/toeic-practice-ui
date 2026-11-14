namespace Contracts;

public sealed record BookMentorRequest(
    int SlotId,
    string? FocusArea = null);
