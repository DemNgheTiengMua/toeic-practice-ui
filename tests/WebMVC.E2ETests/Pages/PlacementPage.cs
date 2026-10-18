using System.Threading.Tasks;
using Microsoft.Playwright;

namespace WebMVC.E2ETests.Pages;

/// <summary>
/// Page Object Model encapsulating the free diagnostic placement test and target band selection.
/// </summary>
public class PlacementPage : BasePage
{
    public ILocator StartPlacementButton => Page.Locator("button#btn-start-placement, form[action*='/placement'] button[type='submit']");
    public ILocator SubmitPlacementButton => Page.Locator("button#btn-submit-placement, button[type='submit']:has-text('Submit')");
    public ILocator TargetBandDropdown => Page.Locator("select#target-band-select, select[name='targetBand']");
    public ILocator SaveTargetBandButton => Page.Locator("button#btn-save-target-band, button[type='submit']:has-text('Save')");

    public PlacementPage(IPage page, string baseUrl = "http://localhost:5000") : base(page, baseUrl)
    {
    }

    public async Task GotoPlacementAsync()
    {
        await NavigateAsync("/placement");
    }

    public async Task GotoTargetBandAsync()
    {
        await NavigateAsync("/placement/target-band");
    }
}
