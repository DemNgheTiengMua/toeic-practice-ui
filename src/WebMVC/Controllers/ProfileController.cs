using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>The learner's profile: name, phone, time zone (UI-52) and password change. Both POSTs
/// are forwarding endpoints and carry an anti-forgery token (ADR-0002).
/// <para>
/// #242 / LRN-09: this screen is also the only place a certificate changes. Switching changes the
/// path, the placement and the practice pool at once, so it lives behind a deliberate visit here and
/// not behind a one-click control in the shell.
/// </para></summary>
[Authorize]
public sealed class ProfileController(
    IProfileClient api,
    ILogger<ProfileController> logger,
    ILearnerContextClient? learnerContext = null) : Controller
{
    private bool IsStaff => new CurrentUser(new HttpContextAccessor { HttpContext = HttpContext }).IsStaff;

    [HttpGet("/profile")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var profile = await api.GetProfileAsync(cancellationToken);
            var shell = await ShellAsync(cancellationToken);
            return View(new ProfilePageViewModel(new ProfileViewModel
            {
                FullName = profile.FullName,
                Phone = profile.Phone,
                TimeZoneId = profile.TimeZoneId,
                TimeZoneOptions = OptionsIncluding(profile.TimeZoneId),
            }, IsStaff)
            {
                Certificates = shell?.Certificates,
                ActiveCertCode = shell?.Active.Code,
            });
        }
        // HttpRequestException, not ApiException alone: with the API down HttpClient throws a plain
        // transport fault. A dead session (AUTH_LOCKED / AUTH_REQUIRED) still reaches the sign-out
        // middleware in Program.cs, as in LearnerControllerBase.
        catch (HttpRequestException ex) when (ex is not ApiException { Code: ApiError.Codes.Locked or ApiError.Codes.Unauthenticated })
        {
            // A failed load must not render an empty form: saving it would overwrite the learner's
            // real name and zone with blanks. Show the failure and a retry instead.
            logger.LogError(ex, "Could not load the profile for the signed-in learner");
            return View(new ProfilePageViewModel(new ProfileViewModel(), IsStaff)
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

        // #242: a re-render keeps the certificate card, because a learner who mistyped a phone number
        // should not also lose the one control that changes their certificate.
        var shell = await ShellAsync(cancellationToken);

        ProfilePageViewModel Page(string? error = null, string? success = null)
        {
            model.FormError = error;
            model.SuccessMessage = success;
            return new ProfilePageViewModel(model, IsStaff)
            {
                Certificates = shell?.Certificates,
                ActiveCertCode = shell?.Active.Code,
            };
        }

        if (!ModelState.IsValid)
        {
            return View(Page());
        }

        try
        {
            await api.UpdateProfileAsync(
                new UpdateProfileRequest(model.FullName, model.Phone, model.TimeZoneId),
                cancellationToken);
            return View(Page(success: "Profile saved."));
        }
        catch (ApiException ex) when (ex.Code is ApiError.Codes.Validation)
        {
            return View(Page(error: ex.Title));
        }
        catch (HttpRequestException ex) when (ex is not ApiException { Code: ApiError.Codes.Locked or ApiError.Codes.Unauthenticated })
        {
            // API down or another refusal: keep what the learner typed rather than a raw 500.
            logger.LogError(ex, "Could not save the profile for the signed-in learner");
            return View(Page(error: "We couldn't save your profile just now. Try again in a moment."));
        }
    }

    [HttpGet("/profile/password")]
    public IActionResult Password() => View(new PasswordPageViewModel(new ChangePasswordViewModel(), IsStaff));

    [HttpPost("/profile/password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Password(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(new PasswordPageViewModel(model, IsStaff));
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
            return View(new PasswordPageViewModel(model, IsStaff));
        }
        catch (ApiException ex) when (ex.Code is ApiError.Codes.TooManyRequests)
        {
            model.FormError = "Too many attempts. Please wait a moment before trying again.";
            return View(new PasswordPageViewModel(model, IsStaff));
        }

        // A password change ends every session issued under the old password, this one included
        // (the API's session stamp), so sign out cleanly instead of failing on the next request.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/auth/sign-in?reset=1");
    }

    /// <summary>#242: the shell's certificate list, or null when it cannot be read. A profile page
    /// that cannot see the catalogue still saves the learner's name and zone; it simply omits the
    /// switch card rather than showing an empty one. No certificate is switched here.</summary>
    private async Task<LearnerContextDto?> ShellAsync(CancellationToken cancellationToken)
    {
        // A staff account has no active certificate, and the context endpoint is learner-only, so
        // asking for one there is a guaranteed refusal rather than a tick per page load.
        if (IsStaff)
        {
            return null;
        }

        if (learnerContext is null)
        {
            return LearnerShell.Get(HttpContext);
        }

        try
        {
            var shell = await learnerContext.GetLearnerContextAsync(cancellationToken);
            // A projection that answered without a certificate is not a shell. Rendering it would
            // dereference a null in the layout that wraps every page, so the card is simply omitted.
            if (shell.Active is null)
            {
                logger.LogWarning("The learner context carried no active certificate; omitting the switch card");
                return null;
            }

            LearnerShell.Set(HttpContext, shell);
            return shell;
        }
        catch (HttpRequestException ex) when (ex is not ApiException { Code: ApiError.Codes.Locked or ApiError.Codes.Unauthenticated })
        {
            logger.LogWarning(ex, "Could not load the certificate list for the profile screen");
            return null;
        }
        // A payload that cannot be read is the same class of failure as one that never arrived: the
        // card is omitted and the learner still edits their name and zone. Left uncaught it would turn
        // a malformed context into a 500 on a page that worked before this card existed.
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogWarning(ex, "The learner context could not be read for the profile screen");
            return null;
        }
    }

    /// <summary>The shortlist, plus the current zone if it isn't listed (e.g. a zone removed from
    /// the shortlist later) so the picker never silently discards it.</summary>
    private static IReadOnlyList<string> OptionsIncluding(string? current) =>
        Contracts.TimeZones.Contains(current)
            ? Contracts.TimeZones.Ids
            : [current!, .. Contracts.TimeZones.Ids];
}
