using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WebMVC.Controllers;

/// <summary>Every learner page renders inside the learner shell (credit balance, certificate
/// switcher), so the shell's context is loaded once here before any action runs and handed to
/// the layout through <see cref="LearnerShell"/>. Learner controllers inherit this.</summary>
[Authorize(Policy = Roles.Learner)]
public abstract class LearnerControllerBase(ICompanionApiClient api, CurrentUser currentUser, ILogger logger) : Controller
{
    protected ICompanionApiClient Api { get; } = api;

    protected CurrentUser CurrentUser { get; } = currentUser;

    /// <summary>The policy guarantees a signed-in user, so the cookie always carries a token.</summary>
    protected async Task<string> BearerAsync() => (await CurrentUser.GetBearerTokenAsync())!;

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            var shell = await Api.GetLearnerContextAsync(await BearerAsync(), HttpContext.RequestAborted);
            LearnerShell.Set(HttpContext, shell);
        }
        // AUTH_LOCKED / AUTH_REQUIRED propagate to the sign-out middleware in Program.cs.
        catch (HttpRequestException ex) when (ex is not ApiException { Code: ApiError.Codes.Locked or ApiError.Codes.Unauthenticated })
        {
            logger.LogError(ex, "Learner context could not be loaded");
            context.Result = View("~/Views/Shared/LearnerUnavailable.cshtml");
            return;
        }

        await next();
    }
}

/// <summary>Where the learner layout finds the shell context the base controller loaded.</summary>
public static class LearnerShell
{
    private const string Key = "learner-shell";

    public static void Set(HttpContext http, LearnerContextDto context) => http.Items[Key] = context;

    public static LearnerContextDto? Get(HttpContext http) => http.Items[Key] as LearnerContextDto;
}
