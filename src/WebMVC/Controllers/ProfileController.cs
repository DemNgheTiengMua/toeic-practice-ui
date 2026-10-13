using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>The learner's profile: name, phone, time zone (UI-52) and password change. Both POSTs
/// are forwarding endpoints and carry an anti-forgery token (ADR-0002).</summary>
[Authorize]
public sealed class ProfileController(ICompanionApiClient api, CurrentUser currentUser) : Controller
{
    [HttpGet("/profile")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var profile = await api.GetProfileAsync(await TokenAsync(), cancellationToken);
        return View(new ProfileViewModel
        {
            FullName = profile.FullName,
            Phone = profile.Phone,
            TimeZoneId = profile.TimeZoneId,
            TimeZoneOptions = OptionsIncluding(profile.TimeZoneId),
        });
    }

    [HttpPost("/profile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ProfileViewModel model, CancellationToken cancellationToken)
    {
        model.TimeZoneOptions = OptionsIncluding(model.TimeZoneId);
        if (!Contracts.TimeZones.Contains(model.TimeZoneId))
        {
            ModelState.AddModelError(nameof(model.TimeZoneId), "Pick a time zone from the list.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await api.UpdateProfileAsync(
                new UpdateProfileRequest(model.FullName, model.Phone, model.TimeZoneId),
                await TokenAsync(), cancellationToken);
            model.SuccessMessage = "Profile saved.";
            return View(model);
        }
        catch (ApiException ex) when (ex.Code is ApiError.Codes.Validation)
        {
            model.FormError = ex.Title;
            return View(model);
        }
    }

    [HttpGet("/profile/password")]
    public IActionResult Password() => View(new ChangePasswordViewModel());

    [HttpPost("/profile/password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Password(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await api.ChangePasswordAsync(
                new ChangePasswordRequest(model.CurrentPassword, model.NewPassword),
                await TokenAsync(), cancellationToken);
        }
        catch (ApiException ex) when (ex.Code is ApiError.Codes.InvalidCredentials or ApiError.Codes.Validation)
        {
            model.FormError = ex.Title;
            return View(model);
        }

        // A password change ends every session issued under the old password, this one included
        // (the API's session stamp), so sign out cleanly instead of failing on the next request.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/auth/sign-in?reset=1");
    }

    private async Task<string> TokenAsync() => (await currentUser.GetBearerTokenAsync())!;

    /// <summary>The shortlist, plus the current zone if it isn't listed (e.g. a zone removed from
    /// the shortlist later) so the picker never silently discards it.</summary>
    private static IReadOnlyList<string> OptionsIncluding(string? current) =>
        Contracts.TimeZones.Contains(current)
            ? Contracts.TimeZones.Ids
            : [current!, .. Contracts.TimeZones.Ids];
}
