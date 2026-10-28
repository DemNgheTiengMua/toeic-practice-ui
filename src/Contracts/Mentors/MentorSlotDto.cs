using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record MentorSlotDto(
    int SlotId,
    int MentorId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Status);
