using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WebMVC.E2ETests.Support;

/// <summary>
/// Writes screenshots under <c>docs/audit/screenshots/&lt;campaign&gt;/&lt;role&gt;/&lt;flow&gt;/</c> and keeps them
/// comparable between runs.
/// <para>
/// <b>Why a shared helper.</b> A screenshot is only evidence if two runs of it are the same picture. Four
/// things made earlier passes differ for no reason: a reveal transition caught mid-flight, a web font that
/// landed after the capture, the live-metrics region repainting between load and shutter, and scroll
/// position left over from the previous screen. Each is pinned once here rather than at every call site.
/// </para>
/// <para>
/// <b>The movement guard.</b> These are captures, not assertions — the assertions live in the journeys
/// beside them. The one exception is money: after the money-accuracy fix no screen may render an amount
/// in a shortened form, because such a figure cannot equal the amount in the database. A capture that
/// finds an abbreviation fails rather than quietly photographing a wrong number.
/// </para>
/// </summary>
public sealed class ScreenshotCapture
{
    /// <summary>Amounts are never abbreviated; "20.2M ₫" is not a number this product can hold.</summary>
    private static readonly Regex AbbreviatedMoney =
        new(@"\d[\d.,]*\s?M\s?₫", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IPage _page;
    private readonly List<string> _consoleErrors = [];

    public ScreenshotCapture(IPage page, string campaign, string role)
    {
        _page = page;
        Campaign = campaign;
        Role = role;
        Directory.CreateDirectory(CampaignRoot);

        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                _consoleErrors.Add(message.Text);
            }
        };
    }

    public string Campaign { get; }

    public string Role { get; }

    public string CampaignRoot =>
        Path.Combine(RepoRoot(), "docs", "audit", "screenshots", Campaign, Role);

    public IReadOnlyList<string> ConsoleErrors => _consoleErrors;

    /// <summary>
    /// Freezes motion, then shoots. Full-page, so a screen taller than the viewport is one image rather
    /// than an arbitrary first fold.
    /// </summary>
    public async Task CaptureAsync(string flow, string name)
    {
        await WaitForSettledAsync();
        await FreezeAsync();

        var dir = Path.Combine(CampaignRoot, flow);
        Directory.CreateDirectory(dir);

        await _page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, $"{name}.png"),
            FullPage = true,
            Animations = ScreenshotAnimations.Disabled,
        });
    }

    /// <summary>
    /// Waits for the document and its layout to settle, but never for <c>networkidle</c>.
    /// <para>
    /// The admin dashboard holds a server-sent event stream open for the life of the page (#286), so the
    /// network is <i>never</i> idle there by design. Waiting for it hangs for the full timeout and then
    /// fails, which is how this was found: the campaign timed out on /admin and nowhere else. A settled
    /// document plus two animation frames is the honest signal — the stream is meant to stay open.
    /// </para>
    /// </summary>
    public async Task WaitForSettledAsync()
    {
        // Not LoadState.Load either: an open EventSource keeps the document from reaching `load` in
        // Chromium, so waiting for it times out on exactly the pages that have a live stream. The
        // document is parsed at DOMContentLoaded, and two frames let layout and fonts settle.
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await _page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");

        // A page with a live stream says "Connecting…" for a moment and only then says "Live". Photographing
        // the first state would record a transient as if it were the screen, and the audit would show a
        // dashboard that looks broken when it is merely early.
        var indicator = _page.Locator("[data-dashboard-live-state]");
        if (await indicator.CountAsync() > 0)
        {
            try
            {
                await indicator.First.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 5000,
                });

                // Wait for the badge to stop saying "Connecting".
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline)
                {
                    var text = await indicator.First.InnerTextAsync();
                    if (!text.StartsWith("Connecting", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    await Task.Delay(100);
                }
            }
            catch (TimeoutException)
            {
                // Still connecting after five seconds is itself worth recording: the image will show it.
            }
        }
    }

    /// <summary>
    /// Navigates and captures in one step, so a broken route is recorded as a broken route rather than
    /// aborting the rest of the pass.
    /// </summary>
    /// <returns>True when the page answered 2xx.</returns>
    public async Task<bool> VisitAsync(string url, string flow, string name, bool capture = true)
    {
        // Deliberately not WaitUntilState.NetworkIdle: see WaitForSettledAsync.
        var response = await _page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var ok = response is not null && response.Ok;

        if (capture)
        {
            // A non-2xx still gets shot: the audit's job is to show what the reviewer would have seen.
            await CaptureAsync(flow, ok ? name : $"{name}--HTTP-{response?.Status ?? 0}");
        }

        return ok;
    }

    /// <summary>
    /// Fails when the current screen states an amount in a shortened form.
    /// </summary>
    public async Task AssertNoAbbreviatedMoneyAsync(string where)
    {
        var text = await _page.Locator("body").InnerTextAsync();
        var match = AbbreviatedMoney.Match(text);

        Assert.That(match.Success, Is.False,
            $"{where} renders money as '{match.Value}'. A shortened amount cannot equal the amount in the "
            + "database, so this screen cannot be audited as financially correct. Money goes through "
            + "CurrencyFormatter, which states the exact figure.");
    }

    /// <summary>
    /// Stops every animation and transition, then lets the layout settle. A reveal transition caught
    /// half-finished is the difference between two runs of the same screen.
    /// </summary>
    private async Task FreezeAsync()
    {
        await _page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = """
                *, *::before, *::after {
                  animation-duration: 0s !important;
                  animation-delay: 0s !important;
                  animation-iteration-count: 1 !important;
                  transition-duration: 0s !important;
                  transition-delay: 0s !important;
                  scroll-behavior: auto !important;
                  caret-color: transparent !important;
                }
                """,
        });

        await _page.EvaluateAsync("() => document.fonts.ready");
        await _page.EvaluateAsync("() => window.scrollTo(0, 0)");

        // One frame for the style tag to take effect before the shutter.
        await _page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExtraEfficiency.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root above " + AppContext.BaseDirectory);
    }
}
