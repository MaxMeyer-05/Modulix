using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Modulix.Web.Services;

namespace Modulix.Web.Pages;

public class LogoutModel : PageModel
{
    private readonly IUserActivityService _userActivityService;

    public LogoutModel(IUserActivityService userActivityService)
    {
        _userActivityService = userActivityService;
    }

    public IActionResult OnGet() => RedirectToPage("/Index");

    public IActionResult OnPost()
    {
        _userActivityService.Clear(User);

        return SignOut(
            new AuthenticationProperties { RedirectUri = Url.Content("~/SignedOut") },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}