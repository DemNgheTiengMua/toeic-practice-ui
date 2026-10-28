using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record AdminMentorSlotDto(
    int SlotId,
    int MentorId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Status);
