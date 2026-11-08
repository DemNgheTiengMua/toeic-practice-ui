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
    public const string StepNotFound = "step_not_found";
    public const string StepLocked = "step_locked";
    public const string NoQuestionsAvailable = "no_questions_available";

    /// <summary>#153: the learner's goal is Unrealistic, so no path is generated for it. The message
    /// names the hours required and the hours available — the two numbers the refusal rests on.</summary>
    public const string GoalUnrealistic = "goal_unrealistic";

    /// <summary>#153: nothing says which band the learner starts from — the old code answered "A1",
    /// which matches no certificate's bands. The learner has to take the placement or set a goal.</summary>
    public const string StartingBandUnknown = "starting_band_unknown";

    /// <summary>#153: no target band anywhere — the old code answered "B2", silently aiming every
    /// learner at a band nobody chose.</summary>
    public const string TargetBandMissing = "target_band_missing";

    public string Code { get; } = code;
}
