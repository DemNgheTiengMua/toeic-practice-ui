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
public sealed class ProfileController(IProfileClient api, ILogger<ProfileController> logger) : Controller
{
    private bool IsAdmin => new CurrentUser(new HttpContextAccessor { HttpContext = HttpContext }).IsAdmin;

    [HttpGet("/profile")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var profile = await api.GetProfileAsync(cancellationToken);
            return View(new ProfilePageViewModel(new ProfileViewModel
            {
                FullName = profile.FullName,
                Phone = profile.Phone,
                TimeZoneId = profile.TimeZoneId,
                TimeZoneOptions = OptionsIncluding(profile.TimeZoneId),
            }, IsAdmin));
        }
        // HttpRequestException, not ApiException alone: with the API down HttpClient throws a plain
        // transport fault. A dead session (AUTH_LOCKED / AUTH_REQUIRED) still reaches the sign-out
        // middleware in Program.cs, as in LearnerControllerBase.
        catch (HttpRequestException ex) when (ex is not ApiException { Code: ApiError.Codes.Locked or ApiError.Codes.Unauthenticated })
        {
            // A failed load must not render an empty form: saving it would overwrite the learner's
            // real name and zone with blanks. Show the failure and a retry instead.
            logger.LogError(ex, "Could not load the profile for the signed-in learner");
            return View(new ProfilePageViewModel(new ProfileViewModel(), IsAdmin)
            {
                LoadError = "We couldn't load your profile just now. Nothing was changed — try again in a moment.",
            });
        }
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
            return View(new ProfilePageViewModel(model, IsAdmin));
        }

        try
        {
            await api.UpdateProfileAsync(
                new UpdateProfileRequest(model.FullName, model.Phone, model.TimeZoneId),
                cancellationToken);
            model.SuccessMessage = "Profile saved.";
            return View(new ProfilePageViewModel(model, IsAdmin));
        }
        catch (ApiException ex) when (ex.Code is ApiError.Codes.Validation)
        {
            model.FormError = ex.Title;
            return View(new ProfilePageViewModel(model, IsAdmin));
        }
        catch (HttpRequestException ex) when (ex is not ApiException { Code: ApiError.Codes.Locked or ApiError.Codes.Unauthenticated })
        {
            // API down or another refusal: keep what the learner typed rather than a raw 500.
            logger.LogError(ex, "Could not save the profile for the signed-in learner");
            model.FormError = "We couldn't save your profile just now. Try again in a moment.";
            return View(new ProfilePageViewModel(model, IsAdmin));
        }
    }

    [HttpGet("/profile/password")]
    public IActionResult Password() => View(new PasswordPageViewModel(new ChangePasswordViewModel(), IsAdmin));

    [HttpPost("/profile/password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Password(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(new PasswordPageViewModel(model, IsAdmin));
        }

        try
        {
            await api.ChangePasswordAsync(
                new ChangePasswordRequest(model.CurrentPassword, model.NewPassword),
                cancellationToken);
        }
        catch (ApiException ex) when (ex.Code is ApiError.Codes.InvalidCredentials or ApiError.Codes.Validation or ApiError.Codes.Locked)
        {
            model.FormError = ex.Title;
            return View(new PasswordPageViewModel(model, IsAdmin));
        }
        catch (ApiException ex) when (ex.Code is ApiError.Codes.TooManyRequests)
        {
            model.FormError = "Too many attempts. Please wait a moment before trying again.";
            return View(new PasswordPageViewModel(model, IsAdmin));
        }

        // A password change ends every session issued under the old password, this one included
        // (the API's session stamp), so sign out cleanly instead of failing on the next request.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/auth/sign-in?reset=1");
    }

    /// <summary>The shortlist, plus the current zone if it isn't listed (e.g. a zone removed from
    /// the shortlist later) so the picker never silently discards it.</summary>
    private static IReadOnlyList<string> OptionsIncluding(string? current) =>
        Contracts.TimeZones.Contains(current)
            ? Contracts.TimeZones.Ids
            : [current!, .. Contracts.TimeZones.Ids];
}
