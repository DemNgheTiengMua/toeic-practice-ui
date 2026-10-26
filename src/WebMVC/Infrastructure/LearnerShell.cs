using Contracts;
using Microsoft.AspNetCore.Http;

namespace WebMVC.Infrastructure;

/// <summary>Where the learner layout finds the shell context the base controller loaded.</summary>
public static class LearnerShell
{
    private const string Key = "learner-shell";

    public static void Set(HttpContext http, LearnerContextDto context) => http.Items[Key] = context;

    public static LearnerContextDto? Get(HttpContext http) => http.Items[Key] as LearnerContextDto;
}
