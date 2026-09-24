using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Server.Services;

namespace Server.Controllers;

/// <summary>
/// Controller for managing modular Docker containers, artifacts, and routing configurations.
/// </summary>
[Authorize]
[ApiController]
[Route("api/modules-management/modules")]
public class ModulesManagementController : ControllerBase
{
    /// <summary>
    /// The module service used by the controller.
    /// </summary>
    private readonly IModuleService _moduleService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModulesManagementController"/> class.
    /// </summary>
    /// <param name="moduleService">The module service.</param>
    public ModulesManagementController(IModuleService moduleService)
    {
        _moduleService = moduleService;
    }
}