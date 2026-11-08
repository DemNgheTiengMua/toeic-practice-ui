using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;
using WebMVC.E2ETests.Pages;

namespace WebMVC.E2ETests.Journeys;

/// <summary>
/// #221 / D18: the audio equipment gate, driven through a real browser.
/// <para>
/// A permission gate is the kind of thing that is only true in a browser: the unit tests assert the
/// markup and the script text, but only Chromium can say whether the three states actually render
/// from a real <c>navigator.permissions</c> answer.
/// </para>
/// <para>
/// This fixture launches its own Chromium because it needs fake media devices
/// (<c>--use-fake-device-for-media-stream</c>) and a controllable permission decision. The Playwright
/// NUnit <c>PageTest</c> base in 1.50 does not expose launch options, so the browser is created here.
/// </para>
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
public sealed class AudioGateBrowserTests
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private string _baseUrl = "http://localhost:5000";

    [OneTimeSetUp]
    public async Task LaunchAsync()
    {
        var configured = Environment.GetEnvironmentVariable("E2E_BASE_URL");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("E2E_BASE_URL is unset, so there is no running app to drive.");
        }

        _baseUrl = configured!.TrimEnd('/');
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try { await probe.GetAsync(_baseUrl); }
        catch (Exception ex) { Assert.Ignore($"No app answered at {_baseUrl} ({ex.Message})."); }

        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            // A synthetic microphone that emits a tone, so "signal detected" is reachable without a
            // real device. The per-test contexts below decide which permission state the page sees.
            Args =
            [
                "--use-fake-device-for-media-stream",
                "--use-fake-ui-for-media-stream",
                "--no-sandbox",
            ],
        });
    }

    [OneTimeTearDown]
    public async Task CloseAsync()
    {
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }

    /// <summary>A context with the microphone already granted - the state a set-up learner is in.</summary>
    private Task<IBrowserContext> GrantedContextAsync() =>
        _browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _baseUrl })
            .ContinueWith(t =>
            {
                var context = t.Result;
                return GrantThenReturn(context);
            }).Unwrap();

    private async Task<IBrowserContext> GrantThenReturn(IBrowserContext context)
    {
        await context.GrantPermissionsAsync(["microphone"], new() { Origin = _baseUrl });
        return context;
    }

    /// <summary>A context in which the microphone was never asked for.</summary>
    private Task<IBrowserContext> UnaskedContextAsync() =>
        _browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _baseUrl });

    private async Task<IPage> SignInAsync(IBrowserContext context, string email = "learner@demo.local")
    {
        var page = await context.NewPageAsync();
        var login = new LoginPage(page, _baseUrl);
        await login.GotoAsync();
        await login.LoginAsync(email, "Demo1234!");
        await WaitForUrlAsync(page, @"(dashboard|admin|certificates)");
        return page;
    }

    // The Playwright assertion API is not reachable from this fixture's compilation surface (the
    // base class that supplies `Expect` is not in use here), so these helpers poll the same
    // conditions the assertions would, with a deadline. They are deliberately small: a test that
    // needs a framework detail to compile proves nothing about the product.
    private static async Task WaitForUrlAsync(IPage page, string pattern)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (Regex.IsMatch(page.Url, pattern, RegexOptions.IgnoreCase)) return;
            await Task.Delay(100);
        }

        Assert.Fail($"URL never matched /{pattern}/; it was {page.Url}");
    }

    private static async Task WaitForVisibleAsync(ILocator locator, bool visible = true)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await locator.CountAsync() > 0 && await locator.First.IsVisibleAsync() == visible) return;
            await Task.Delay(100);
        }

        Assert.Fail($"Element was never {(visible ? "visible" : "hidden")}: {locator}");
    }

    private static async Task WaitForTextAsync(ILocator locator, string expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        string seen = "";
        while (DateTime.UtcNow < deadline)
        {
            seen = await locator.InnerTextAsync();
            if (seen.Contains(expected, StringComparison.OrdinalIgnoreCase)) return;
            await Task.Delay(100);
        }

        Assert.Fail($"Text never contained '{expected}'. It was: {seen}");
    }

    private static async Task WaitForCountAsync(ILocator locator, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        int seen = -1;
        while (DateTime.UtcNow < deadline)
        {
            seen = await locator.CountAsync();
            if (seen == expected) return;
            await Task.Delay(100);
        }

        Assert.Fail($"Count was {seen}, expected {expected}: {locator}");
    }

    /// <summary>
    /// The centrepiece. With the permission granted, the settings surface renders the granted block
    /// and hides the other two. This is the assertion a "gate exists" test cannot make.
    /// </summary>
    [Test]
    public async Task Granted_microphone_renders_the_granted_state_and_no_denied_remedy()
    {
        await using var context = await GrantedContextAsync();
        var page = await SignInAsync(context);
        await page.GotoAsync($"{_baseUrl}/profile");

        var gate = page.Locator("main [data-audio-equipment] [data-permission-gate]").First;
        await WaitForVisibleAsync(gate);

        await WaitForVisibleAsync(gate.Locator("[data-permission-state='granted']"));
        await WaitForVisibleAsync(gate.Locator("[data-permission-state='denied']"), visible: false);
        await WaitForVisibleAsync(gate.Locator("[data-permission-state='never-asked']"), visible: false);

        // The copy, not the class, is what the learner reads.
        await WaitForTextAsync(
            gate.Locator("[data-permission-state='granted']"), "Microphone permission: granted.");

        // The state is also announced, not only coloured.
        await WaitForCountAsync(gate.Locator("[data-permission-state='granted'] [role='status']"), 1);
    }

    /// <summary>
    /// With no grant the surface must NOT claim denied: a fresh browser has never been asked, and that
    /// is a different state with a different remedy. This is the "three states collapse into two"
    /// trap, asserted against the browser rather than against our own cache.
    /// </summary>
    [Test]
    public async Task An_unasked_microphone_renders_the_never_asked_state_not_denied()
    {
        await using var context = await UnaskedContextAsync();
        var page = await SignInAsync(context);
        await page.GotoAsync($"{_baseUrl}/profile");

        var gate = page.Locator("main [data-audio-equipment] [data-permission-gate]").First;
        await WaitForVisibleAsync(gate.Locator("[data-permission-state='never-asked']"));
        await WaitForVisibleAsync(gate.Locator("[data-permission-state='denied']"), visible: false);
        await WaitForTextAsync(
            gate.Locator("[data-permission-state='never-asked']"), "Microphone permission: not asked yet.");

        // The explicit control D18 requires: it says what it does, and only this state offers it.
        var request = gate.Locator("[data-audio-action='request-permission']");
        await WaitForVisibleAsync(request);
        await WaitForTextAsync(request, "Enable the microphone to see device names");
    }

    /// <summary>
    /// The gate's own three-state record survives a reload. A state held only in memory would render
    /// "never asked" after every navigation, and the learner would meet the prompt on every screen.
    /// </summary>
    [Test]
    public async Task The_permission_record_survives_a_reload()
    {
        await using var context = await GrantedContextAsync();
        var page = await SignInAsync(context);
        await page.GotoAsync($"{_baseUrl}/profile");

        // Give the gate a moment to write its record.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        string stored = "";
        while (DateTime.UtcNow < deadline)
        {
            stored = await page.EvaluateAsync<string>(
                "() => (JSON.parse(localStorage.getItem('ee_microphone_preference_permission') || '{}').state) || ''");
            if (stored == "granted") break;
            await Task.Delay(100);
        }
        Assert.That(stored, Is.EqualTo("granted"),
            "The gate must record the browser's answer locally, so a reload does not re-ask.");

        await page.ReloadAsync();
        var gate = page.Locator("main [data-audio-equipment] [data-permission-gate]").First;
        await WaitForVisibleAsync(gate.Locator("[data-permission-state='granted']"));
    }

    /// <summary>
    /// The reading-only surface. The criterion is that a page which examines no speaking skill never
    /// offers a microphone control; this asserts the rendered attribute the panel is built from.
    /// </summary>
    [Test]
    public async Task A_surface_that_examines_no_speaking_skill_offers_no_microphone_panel()
    {
        await using var context = await GrantedContextAsync();
        var page = await SignInAsync(context);
        await page.GotoAsync($"{_baseUrl}/practice");

        var checks = page.Locator("[data-audio-equipment]");
        var count = await checks.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var check = checks.Nth(i);
            var showsInput = await check.GetAttributeAsync("data-show-input");
            Assert.That(showsInput, Is.EqualTo("false"),
                $"Practice catalogue item {i} must not offer a microphone: no speaking skill is drilled there.");
        }
    }

    /// <summary>
    /// The output test plays a real sample and the badge only reaches CHECK COMPLETE after playback.
    /// A "test passed" badge with no sound is the defect D18 names.
    /// </summary>
    [Test]
    public async Task The_output_test_plays_a_real_sample_and_then_confirms()
    {
        await using var context = await GrantedContextAsync();
        var page = await SignInAsync(context);
        await page.GotoAsync($"{_baseUrl}/profile");

        var check = page.Locator("main [data-audio-equipment]").First;
        await check.Locator("[data-audio-action='speaker-test']").ClickAsync();
        await WaitForTextAsync(check.Locator("[data-audio-role='speaker-status']"), "CHECK COMPLETE");
    }
}
