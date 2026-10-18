using System.Threading.Tasks;
using Microsoft.Playwright;

namespace WebMVC.E2ETests.Pages;

/// <summary>
/// Page Object Model encapsulating authentication flows on /auth/sign-in.
/// </summary>
public class LoginPage : BasePage
{
    public ILocator EmailInput => Page.GetByRole(AriaRole.Textbox, new() { Name = "Email" });
    public ILocator PasswordInput => Page.GetByRole(AriaRole.Textbox, new() { Name = "Password" });
    public ILocator SignInButton => Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" });
    public ILocator ErrorAlert => Page.GetByRole(AriaRole.Alert);
    public ILocator SignOutButton => Page.Locator("form[action='/auth/sign-out'] button[type='submit']").First;
    public ILocator SignInLink => Page.Locator("a[href='/auth/sign-in']").First;

    public LoginPage(IPage page, string baseUrl = "http://localhost:5000") : base(page, baseUrl)
    {
    }

    public async Task GotoAsync()
    {
        await NavigateAsync("/auth/sign-in");
    }

    public async Task LoginAsync(string email, string password)
    {
        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);
        await SignInButton.ClickAsync();
    }

    public async Task SignOutAsync()
    {
        if (await SignOutButton.IsVisibleAsync())
        {
            await SignOutButton.ClickAsync();
        }
    }
}
