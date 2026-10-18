using System.Threading.Tasks;
using Microsoft.Playwright;

namespace WebMVC.E2ETests.Pages;

/// <summary>
/// Base class for all Page Object Models wrapping Playwright IPage.
/// Enforces semantic accessibility locators and standardized navigation.
/// </summary>
public abstract class BasePage
{
    public IPage Page { get; }
    public string BaseUrl { get; }

    protected BasePage(IPage page, string baseUrl = "http://localhost:5000")
    {
        Page = page;
        BaseUrl = baseUrl.TrimEnd('/');
    }

    public virtual async Task NavigateAsync(string relativePath)
    {
        var url = relativePath.StartsWith("http", System.StringComparison.OrdinalIgnoreCase)
            ? relativePath
            : $"{BaseUrl}/{relativePath.TrimStart('/')}";
        await Page.GotoAsync(url);
    }

    public async Task WaitForPageReadyAsync()
    {
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    public async Task<string> GetTitleAsync()
    {
        return await Page.TitleAsync();
    }

    public ILocator GetPlacard(string text)
    {
        return Page.GetByText(text);
    }

    public ILocator GetAlert()
    {
        return Page.GetByRole(AriaRole.Alert);
    }
}
