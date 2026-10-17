using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace WebMVC.Controllers;

/// <summary>Base controller for admin portal endpoints enforcing Roles.Admin authorization policy.</summary>
[Authorize(Policy = Roles.Admin)]
public abstract class AdminControllerBase(CurrentUser currentUser, ILogger? logger = null) : Controller
{
    protected CurrentUser CurrentUser { get; } = currentUser;
    protected ILogger? Logger { get; } = logger;

    protected async Task<string> BearerAsync() => (await CurrentUser.GetBearerTokenAsync())!;
}
