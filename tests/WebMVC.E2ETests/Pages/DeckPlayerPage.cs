using System.Threading.Tasks;
using Microsoft.Playwright;

namespace WebMVC.E2ETests.Pages;

/// <summary>
/// Page Object Model encapsulating the interactive guided learning deck player.
/// </summary>
public class DeckPlayerPage : BasePage
{
    public ILocator DeckContainer => Page.Locator(".lesson-deck");
    public ILocator NextButton => Page.Locator("#btnNext");
    public ILocator FinishButton => Page.Locator("#btnFinish");
    public ILocator MicroCheckCard => Page.Locator("#card-4");
    public ILocator CheckAnswerButton => Page.Locator("#btnCheckAnswer");
    public ILocator ExplanationContainer => Page.Locator("#microCheckExplanation");

    public DeckPlayerPage(IPage page, string baseUrl = "http://localhost:5000") : base(page, baseUrl)
    {
    }

    public async Task GotoAsync(int lessonId)
    {
        await NavigateAsync($"/lessons/{lessonId}");
    }

    public async Task NavigateToMicroCheckCardAsync()
    {
        for (int i = 0; i < 4; i++)
        {
            if (await MicroCheckCard.IsVisibleAsync()) break;
            if (await NextButton.IsVisibleAsync() && !await NextButton.IsDisabledAsync())
            {
                await NextButton.ClickAsync();
            }
        }
    }

    public async Task AnswerMicroCheckAsync(string optionKey)
    {
        var option = Page.Locator($"label[data-opt='{optionKey}']");
        await option.ClickAsync();
        await CheckAnswerButton.ClickAsync();
    }
}
