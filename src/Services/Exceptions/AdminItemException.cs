using System;
using Contracts;

namespace Services;

/// <summary>
/// #188: a refusal from pool authoring. Its own type rather than <see cref="AdminExamException"/>,
/// for the reason <see cref="AdminLessonException"/> has one: an item write names no exam, and an
/// error type that says "exam" invites a caller to think it does.
/// </summary>
public class AdminItemException(string code, string message, int status = 400) : Exception(message)
{
    public string Code => code;
    public int Status => status;
}

public sealed class ItemNotFoundException(int itemId)
    : AdminItemException(ApiError.Codes.NotFound, $"Item with id {itemId} not found.", 404);

public sealed class PoolTopicNotFoundException(int topicId)
    : AdminItemException(ApiError.Codes.Validation, $"Topic with id {topicId} not found in the twenty-topic vocabulary.", 400);
