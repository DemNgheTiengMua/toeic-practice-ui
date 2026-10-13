using System.Security.Claims;
using Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
    public IActionResult SignIn(string? returnUrl = null, bool locked = false) => View(new SignInViewModel
    {
        ReturnUrl = returnUrl,
        FormError = locked ? "This account is locked. Contact support." : null,
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
        return result.User.Role == "admin"
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

    private async Task SignInCookieAsync(AuthTokenResponse result)
    {
        var claims = new List<Claim>
        {
            new(IAccessTokenFactory.ClaimUserId, result.User.UserId.ToString()),
            new(IAccessTokenFactory.ClaimEmail, result.User.Email),
            new(IAccessTokenFactory.ClaimRole, result.User.Role),
        };
        if (!string.IsNullOrEmpty(result.User.FullName))
        {
            claims.Add(new Claim(IAccessTokenFactory.ClaimName, result.User.FullName));
        }

        // The identity must declare the compact claim types as its name/role claim types, or
        // RequireRole and User.Identity.Name fall back to the long XML-namespace defaults.
        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme,
            nameType: IAccessTokenFactory.ClaimEmail,
            roleType: IAccessTokenFactory.ClaimRole);
        var principal = new ClaimsPrincipal(identity);

        var properties = new AuthenticationProperties(
            new Dictionary<string, string?> { ["jwt"] = result.Token },
            new Dictionary<string, object?> { [".expiry"] = result.ExpiresAt.ToString("O") });

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
    }
}
