using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace WebMVC.Controllers;

/// <summary>Shared plumbing for staff controllers: the signed-in account and the forwarded JWT.
/// <para>
/// D16: this base declares no policy. Each staff controller names the capability it needs, because
/// there is no single "admin" namespace — a support triager and a blueprint designer are different
/// accounts holding different namespaces, and a base gate on one of them would either refuse the
/// other or be meaningless.
/// </para></summary>
public abstract class AdminControllerBase(CurrentUser currentUser, ILogger? logger = null) : Controller
{
    protected CurrentUser CurrentUser { get; } = currentUser;
    protected ILogger? Logger { get; } = logger;

    protected async Task<string> BearerAsync() => (await CurrentUser.GetBearerTokenAsync())!;
}
