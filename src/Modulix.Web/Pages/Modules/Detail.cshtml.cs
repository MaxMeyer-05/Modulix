using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulix.Client.Models.Dtos;

namespace Modulix.Web.Pages.Modules;

public class DetailModel : PageModel
{
    public ModuleDetailDto? Module { get; private set; }

    public void OnGet(Guid id)
    {
        Module = ModulePreview.Modules.FirstOrDefault(module => module.Id == id);
        if (Module is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}