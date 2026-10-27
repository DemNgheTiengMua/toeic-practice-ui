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

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            await httpClient.GetAsync(BaseUrl);
        }
        catch (Exception ex)
        {
            Assert.Ignore($"E2E server not reachable at {BaseUrl} ({ex.Message}). Start server to run E2E tests.");
        }

        await Page.SetViewportSizeAsync(1440, 900);

        // The sitting room confirms irreversible skill advances with window.confirm. Auto-accept by
        // default so journeys exercise the post-confirmation path; a test that needs to assert the
        // prompt itself can register its own handler, which runs after this one.
        Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
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
        await Expect(Page).ToHaveURLAsync(new Regex(@"(dashboard|admin|certificates)", RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// Opens the database the running app uses (TEST_DB_CONNECTION, catalog defaulting to
    /// ExtraEfficiency), for tests that arrange or check rows directly.
    /// </summary>
    protected static async Task<Microsoft.Data.SqlClient.SqlConnection> OpenAppDatabaseAsync()
    {
        var rawConn = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=ExtraEfficiency;Trusted_Connection=True;TrustServerCertificate=True";
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(rawConn);
        if (string.IsNullOrEmpty(builder.InitialCatalog))
        {
            builder.InitialCatalog = "ExtraEfficiency";
        }

        var conn = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        return conn;
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
