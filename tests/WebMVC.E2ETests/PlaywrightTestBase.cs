using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace WebMVC.E2ETests;

/// <summary>
/// Base class for all Playwright E2E browser tests against WebMVC.
/// Uses NUnit lifecycle hooks provided by Microsoft.Playwright.NUnit.PageTest.
/// </summary>
public abstract class PlaywrightTestBase : PageTest
{
    protected string BaseUrl { get; private set; } = "http://localhost:5000";

    [SetUp]
    public async Task BaseSetupAsync()
    {
        var configuredUrl = Environment.GetEnvironmentVariable("E2E_BASE_URL");
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            BaseUrl = configuredUrl.TrimEnd('/');
        }

        await Page.SetViewportSizeAsync(1440, 900);
    }

    /// <summary>
    /// Helper to sign in with demo credentials.
    /// </summary>
    protected async Task SignInAsync(string email = "learner@demo.local", string password = "Demo1234!")
    {
        await Page.GotoAsync($"{BaseUrl}/auth/sign-in");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Email" }).FillAsync(email);
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Password" }).FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }

    /// <summary>
    /// Helper to sign out cleanly.
    /// </summary>
    protected async Task SignOutAsync()
    {
        var signOutBtn = Page.Locator("form[action='/auth/sign-out'] button[type='submit']").First;
        if (await signOutBtn.IsVisibleAsync())
        {
            await signOutBtn.ClickAsync();
        }
    }
}
