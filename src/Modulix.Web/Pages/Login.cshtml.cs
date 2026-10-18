using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Modulix.Web.Pages;

[AllowAnonymous]
public class LoginModel : PageModel
{
    public IActionResult OnGet(string? returnUrl)
    {
        var redirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/");

        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(redirectUri);
        }

        return Challenge(
            new AuthenticationProperties { RedirectUri = redirectUri },
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}