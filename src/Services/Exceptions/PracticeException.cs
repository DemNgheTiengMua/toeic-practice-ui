using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public class PracticeException(string code, string message) : Exception(message)
{
    public const string SectionUnavailable = "section_unavailable";
    public const string ModalityNotAllowed = "modality_not_allowed";
    public const string NoQuestionsAvailable = "no_questions_available";
    public const string SessionNotFound = "session_not_found";
    public const string QuestionNotFound = "question_not_found";
    public const string InvalidOption = "invalid_option";
    public const string SessionAlreadyFinished = "session_already_finished";
    public const string AlreadyAnswered = "already_answered";

    /// <summary>#235: a free drill refused by the plan, the cell's day, or the daily ceiling. The
    /// message names what is missing and what to do instead — never a bare "locked".</summary>
    public const string FreeDrillLocked = "free_drill_locked";

    public string Code { get; } = code;
}
