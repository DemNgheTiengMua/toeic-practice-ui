using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WebMVC.Models;

namespace WebMVC.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        var user = new CurrentUser(new HttpContextAccessor { HttpContext = HttpContext });
        return View(new HomeViewModel(user.IsSignedIn, user.IsAdmin));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
