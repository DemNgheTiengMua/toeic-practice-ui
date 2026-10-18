using System.Threading.Tasks;
using Microsoft.Playwright;

namespace WebMVC.E2ETests.Pages;

/// <summary>
/// Page Object Model encapsulating the pricing, checkout, and mock gateway flows.
/// </summary>
public class CheckoutPage : BasePage
{
    public ILocator PackageRadio(int packageId) => Page.Locator($"input[name='packageId'][value='{packageId}']");
    public ILocator ChoosePackageButton => Page.Locator("button[type='submit']:has-text('Choose')");
    public ILocator MockGatewaySuccessButton => Page.Locator("button[name='status'][value='PAID']");
    public ILocator PaymentResultTitle => Page.Locator("h1:has-text('Payment successful')");
    public ILocator WalletBalanceBadge => Page.Locator(".wallet-balance, .badge--balance, header strong:has-text('credits')");

    public CheckoutPage(IPage page, string baseUrl = "http://localhost:5000") : base(page, baseUrl)
    {
    }

    public async Task GotoPricingAsync()
    {
        await NavigateAsync("/pricing");
    }

    public async Task GotoCheckoutAsync(int packageId)
    {
        await NavigateAsync($"/checkout?packageId={packageId}");
    }
}
