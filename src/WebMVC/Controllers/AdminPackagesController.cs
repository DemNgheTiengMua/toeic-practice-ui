using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

[Authorize(Policy = Roles.Admin)]
[Route("admin/packages")]
public sealed class AdminPackagesController(
    IAdminPackagesClient api,
    CurrentUser currentUser,
    ILogger<AdminPackagesController> logger) : AdminControllerBase(currentUser, logger)
{

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var summary = await api.GetAdminPackagesAsync(cancellationToken);
            return View("~/Views/Admin/Packages.cshtml", summary);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not load admin packages");
            TempData["Error"] = "Could not load the package list. Please try again.";
            return View("~/Views/Admin/Packages.cshtml", new AdminPackagesSummaryDto([], 0, 0, 0, 0));
        }
    }

    [HttpGet("create")]
    public IActionResult Create()
    {
        return View("~/Views/Admin/PackageForm.cshtml", new PackageFormViewModel
        {
            IsActive = true,
            Credits = 1,
            Price = 49000,
            DisplayOrder = 1,
        });
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] PackageFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("~/Views/Admin/PackageForm.cshtml", model);
        }

        try
        {
            var request = new CreatePackageRequest(
                model.Name,
                model.Credits,
                model.Price,
                model.IsActive,
                model.IsFeatured,
                model.Features,
                model.DisplayOrder);

            await api.CreatePackageAsync(request, cancellationToken);
            TempData["Success"] = $"Package '{model.Name}' created successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("~/Views/Admin/PackageForm.cshtml", model);
        }
    }

    [HttpGet("edit/{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        try
        {
            var pkg = await api.GetAdminPackageAsync(id, cancellationToken);
            var model = new PackageFormViewModel
            {
                PackageId = pkg.PackageId,
                Name = pkg.Name,
                Credits = pkg.Credits,
                Price = pkg.Price,
                IsActive = pkg.IsActive,
                IsFeatured = pkg.IsFeatured,
                Features = pkg.Features,
                DisplayOrder = pkg.DisplayOrder,
            };
            return View("~/Views/Admin/PackageForm.cshtml", model);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not load package {PackageId}", id);
            TempData["Error"] = "Package not found.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost("edit/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [FromForm] PackageFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.PackageId = id;
            return View("~/Views/Admin/PackageForm.cshtml", model);
        }

        try
        {
            var request = new UpdatePackageRequest(
                model.Name,
                model.Credits,
                model.Price,
                model.IsActive,
                model.IsFeatured,
                model.Features,
                model.DisplayOrder);

            await api.UpdatePackageAsync(id, request, cancellationToken);
            TempData["Success"] = $"Package '{model.Name}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiException ex)
        {
            model.PackageId = id;
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("~/Views/Admin/PackageForm.cshtml", model);
        }
    }

    [HttpPost("deactivate/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        try
        {
            await api.DeactivatePackageAsync(id, cancellationToken);
            TempData["Success"] = "Package deactivated.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not deactivate package {PackageId}", id);
            TempData["Error"] = "Could not deactivate package.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("toggle-featured/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleFeatured(int id, CancellationToken cancellationToken)
    {
        try
        {
            var pkg = await api.GetAdminPackageAsync(id, cancellationToken);
            if (!pkg.IsActive && !pkg.IsFeatured)
            {
                TempData["Error"] = "Cannot feature an inactive package. Activate it first.";
                return RedirectToAction(nameof(Index));
            }

            var updated = new UpdatePackageRequest(
                pkg.Name,
                pkg.Credits,
                pkg.Price,
                pkg.IsActive,
                !pkg.IsFeatured,
                pkg.Features,
                pkg.DisplayOrder);

            await api.UpdatePackageAsync(id, updated, cancellationToken);
            TempData["Success"] = !pkg.IsFeatured
                ? $"Package '{pkg.Name}' marked as featured."
                : $"Package '{pkg.Name}' is no longer featured.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not toggle featured status for package {PackageId}", id);
            TempData["Error"] = "Could not update featured status.";
        }

        return RedirectToAction(nameof(Index));
    }
}
