using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebMVC.Controllers;

/// <summary>The learner's landing page after sign-in.</summary>
[Authorize]
public sealed class DashboardController(CurrentUser currentUser) : Controller
{
    public IActionResult Index() => View(currentUser);
}
