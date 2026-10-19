using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Modulix.Controllers;

/// <summary>
/// Controller for managing module versions.
/// </summary>
[Authorize]
[ApiController]
[Route("api/version-management/module-versions")]
public class ModuleVersionsManagementController : ControllerBase
{
    
}