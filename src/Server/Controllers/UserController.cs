using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using Server.Services;

namespace Server.Controllers;

/// <summary>
/// The controller responsible for managing user-related operations.
/// Provides endpoints for creating, retrieving, updating, and deleting users.
/// </summary>
[Authorize]
[ApiController]
[Route("api/users")]
public class UserController : ControllerBase
{
    /// <summary>
    /// The user service instance.
    /// </summary>
    private readonly IUserService _userService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserController"/> class.
    /// </summary>
    /// <param name="userService">The user service instance.</param>
    public UserController(IUserService userService)
    {
        _userService = userService;
    }
}