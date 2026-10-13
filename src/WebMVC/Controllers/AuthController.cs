using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

/// <summary>Register, sign in, sign out. The API mints the JWT; this controller stores it in an
/// HttpOnly cookie as an authentication token and mirrors the identity into cookie claims.
/// Every POST here is a forwarding endpoint and carries an anti-forgery token (ADR-0002).</summary>
public sealed class AuthController(ICompanionApiClient api) : Controller
{
    [HttpGet("/auth/register")]
    public IActionResult Register(string? returnUrl = null) => View(new RegisterViewModel { ReturnUrl = returnUrl });

    [HttpPost("/auth/register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        AuthTokenResponse result;
        try
        {
            result = await api.RegisterAsync(
                new RegisterRequest(model.FullName, model.Email, model.Phone, model.Password, model.TermsAccepted),
                cancellationToken);
        }
        catch (ApiException ex) when (ex.Code is ApiError.Codes.EmailTaken or ApiError.Codes.Validation)
        {
            model.FormError = ex.Title;
            return View(model);
        }

        await SignInCookieAsync(result);
        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet("/auth/sign-in")]
    public IActionResult SignIn(string? returnUrl = null, bool locked = false, bool reset = false) => View(new SignInViewModel
    {
        ReturnUrl = returnUrl,
        FormError = locked ? "This account is locked. Contact support." : null,
        Notice = reset ? "Password changed. Sign in with your new password." : null,
    });

    [HttpPost("/auth/sign-in")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignIn(SignInViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        AuthTokenResponse result;
        try
        {
            result = await api.SignInAsync(new SignInRequest(model.Email, model.Password), cancellationToken);
        }
        catch (ApiException ex) when (ex.Code is ApiError.Codes.InvalidCredentials or ApiError.Codes.Locked)
        {
            model.FormError = ex.Title;
            return View(model);
        }

        await SignInCookieAsync(result);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        // Learners and admins land on their own dashboards.
        return result.User.Role == Roles.Admin
            ? RedirectToAction("Index", "Admin")
            : RedirectToAction("Index", "Dashboard");
    }

    [HttpPost("/auth/sign-out")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignOut()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("/auth/denied")]
    public IActionResult Denied() => View();

    // ---- Email verification (issue #5) ----
    // The emailed link lands on a confirm page and the token is consumed by a POST, not the GET:
    // mail scanners prefetch links, and a GET that burned a single-use token would leave the
    // learner holding a dead link.

    [HttpGet("/auth/verify-email")]
    public IActionResult VerifyEmail(string? token = null) => View(new VerifyEmailViewModel
    {
        Token = token ?? "",
        Verified = string.IsNullOrEmpty(token) ? false : null,
    });

    [HttpPost("/auth/verify-email")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyEmail(VerifyEmailViewModel model, CancellationToken cancellationToken)
    {
        try
        {
            await api.VerifyEmailAsync(new VerifyEmailRequest(model.Token), cancellationToken);
            model.Verified = true;
        }
        catch (ApiException ex) when (ex.Code is ApiError.Codes.TokenInvalid or ApiError.Codes.Validation)
        {
            model.Verified = false;
        }

        return View(model);
    }

    [Authorize]
    [HttpPost("/auth/resend-activation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendActivation([FromServices] CurrentUser currentUser, CancellationToken cancellationToken)
    {
        var token = await currentUser.GetBearerTokenAsync();
        try
        {
            await api.ResendActivationAsync(token!, cancellationToken);
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.MailUnavailable)
        {
            return RedirectToAction("Index", "Dashboard", new { activation = "failed" });
        }

        return RedirectToAction("Index", "Dashboard", new { activation = "sent" });
    }

    // ---- Password reset (issue #5) ----

    [HttpGet("/auth/forgot-password")]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost("/auth/forgot-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await api.ForgotPasswordAsync(new ForgotPasswordRequest(model.Email), cancellationToken);
            model.Sent = true;
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.Validation)
        {
            model.FormError = ex.Detail ?? ex.Title;
        }
        catch (HttpRequestException)
        {
            // API down (any ApiException not caught above is a 5xx). The copy never says "no such
            // account": it must not reveal whether the address is registered.
            model.FormError = "Could not send the password reset link. Try again in a few minutes.";
        }

        return View(model);
    }

    [HttpGet("/auth/reset-password")]
    public IActionResult ResetPassword(string? token = null) => View(new ResetPasswordViewModel
    {
        Token = token ?? "",
        LinkInvalid = string.IsNullOrEmpty(token),
    });

    [HttpPost("/auth/reset-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await api.ResetPasswordAsync(new ResetPasswordRequest(model.Token, model.NewPassword), cancellationToken);
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.TokenInvalid)
        {
            model.LinkInvalid = true;
            return View(model);
        }
        catch (ApiException ex) when (ex.Code == ApiError.Codes.Validation)
        {
            model.FormError = ex.Detail ?? ex.Title;
            return View(model);
        }

        // Any session still open in this browser belongs to the old password; start clean.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/auth/sign-in?reset=1");
    }

    private async Task SignInCookieAsync(AuthTokenResponse result)
    {
        var claims = AuthClaims.For(result.User.UserId, result.User.Email, result.User.Role, result.User.FullName);

        // The identity must declare the compact claim types as its name/role claim types, or
        // RequireRole and User.Identity.Name fall back to the long XML-namespace defaults.
        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme,
            nameType: IAccessTokenFactory.ClaimEmail,
            roleType: IAccessTokenFactory.ClaimRole);
        var principal = new ClaimsPrincipal(identity);

        var properties = new AuthenticationProperties(
            new Dictionary<string, string?> { ["jwt"] = result.Token })
        {
            // Cookie lifetime is pinned to the token's expiry, not a second hardcoded constant.
            ExpiresUtc = result.ExpiresAt,
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
    }
}
