using System.IO;
using Xunit;

namespace WebMVC.Tests;

public sealed class IdentityFinancePresentationTests
{
    [Theory]
    [InlineData("Home/Index.cshtml")]
    [InlineData("Certificates/Index.cshtml")]
    [InlineData("Auth")]
    [InlineData("Profile")]
    [InlineData("Pricing")]
    [InlineData("Wallet")]
    [InlineData("Checkout")]
    [InlineData("Support")]
    [InlineData("Admin/Orders.cshtml")]
    [InlineData("Admin/_OrderDetail.cshtml")]
    [InlineData("Admin/Index.cshtml")]
    [InlineData("Admin/PackageForm.cshtml")]
    [InlineData("Admin/Users.cshtml")]
    [InlineData("Admin/Packages.cshtml")]
    [InlineData("Admin/Complaints.cshtml")]
    [InlineData("Admin/ComplaintDetail.cshtml")]
    public void Views_UsePreparedPresentationAndExternalAssets(string relative)
    {
        var target = Path.Combine(TestPathHelper.FindRepoRoot(), "src/WebMVC/Views", relative);
        foreach (var path in File.Exists(target) ? new[] { target } : Directory.GetFiles(target, "*.cshtml"))
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("@inject", text);
            Assert.DoesNotContain("<style", text);
            Assert.DoesNotMatch(@"<script(?:\s[^>]*)?>\s*[^<\s]", text);
            Assert.DoesNotContain(".GroupBy(", text);
            Assert.DoesNotContain(".Select(", text);
            Assert.DoesNotContain(".Sum(", text);
            Assert.DoesNotContain("Math.", text);
            Assert.DoesNotContain("CurrencyFormatter.", text);
            Assert.DoesNotContain("LearnerShell.Get(", text);
        }
    }
}
