using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services.Credits;

namespace Services;

public class LearningPathException(string code, string message) : Exception(message)
{
    public const string PathNotFound = "path_not_found";
    public const string PathAlreadyActive = "path_already_active";
    public const string TemplateNotFound = "template_not_found";
    public const string ModuleNotFound = "module_not_found";
    public const string StepNotFound = "step_not_found";
    public const string StepLocked = "step_locked";
    public const string NoQuestionsAvailable = "no_questions_available";
    public const string InsufficientCredits = "insufficient_credits";

    public string Code { get; } = code;
}
