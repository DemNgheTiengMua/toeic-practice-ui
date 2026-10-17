using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WebMVC.E2ETests.Journeys;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class AuthenticationTests : PlaywrightTestBase
{
    [Test]
    public async Task Should_SignIn_Successfully_With_Valid_Learner_Credentials()
    {
        await SignInAsync("learner@demo.local", "Demo1234!");

        // Expect redirect to dashboard or certificates
        await Expect(Page).ToHaveURLAsync(new Regex(@"(Dashboard|certificates)"));

        // Expect authenticated header to be visible with demo learner name or sign out button
        await Expect(Page.Locator("form[action='/auth/sign-out'] button[type='submit']").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_Display_Error_On_Invalid_Credentials()
    {
        await Page.GotoAsync($"{BaseUrl}/auth/sign-in");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Email" }).FillAsync("learner@demo.local");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Password" }).FillAsync("WrongPassword!123");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        // Must remain on sign-in page
        await Expect(Page).ToHaveURLAsync(new Regex(@"/auth/sign-in"));

        // Must display failure alert
        await Expect(Page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
    }

    [Test]
    public async Task Should_SignOut_Cleanly()
    {
        await SignInAsync("learner@demo.local", "Demo1234!");
        await Expect(Page.Locator("form[action='/auth/sign-out'] button[type='submit']").First).ToBeVisibleAsync();

        await SignOutAsync();

        // After sign-out, verify Sign in link is visible
        await Expect(Page.Locator("a[href='/auth/sign-in']").First).ToBeVisibleAsync();
    }
}
