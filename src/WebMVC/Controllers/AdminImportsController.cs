using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebMVC.Controllers;

/// <summary>
/// Question batch import (ADR-0007).
/// Legacy /admin/imports route smoothly redirects to integrated exam package import on /admin/exams.
/// </summary>
[Authorize(Policy = Roles.Admin)]
[Route("admin/imports")]
public sealed class AdminImportsController(
    CurrentUser currentUser,
    ILogger<AdminImportsController> logger) : AdminControllerBase(currentUser, logger)
{
    [HttpGet("")]
    [HttpPost("")]
    public IActionResult Index([FromQuery] int? certId = null)
    {
        return RedirectToAction("Index", "AdminExams", new { certId, import = true });
    }
}
