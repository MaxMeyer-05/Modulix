using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using Server.Services;
using Server.Models.Dtos;

namespace Server.Controllers;

/// <summary>
/// Controller responsible for handling authentication-related endpoints,
/// including login, registration, and logout.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// The authentication service used by the controller.
    /// </summary>
    private readonly IAuthService _authService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthController"/> class.
    /// </summary>
    /// <param name="authService">The authentication service.</param>
    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Handles user login requests.
    /// </summary>
    /// <param name="loginDto">The login details of the user.</param>
    /// <returns>A tuple containing the user information and the token result.</returns>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<(UserDto, TokenResultDto)>> Login([FromBody] LoginDto loginDto)
    {
        var result = await _authService.LoginAsync(loginDto);
        return Ok(result);
    }

    /// <summary>
    /// Handles user registration requests.
    /// </summary>
    /// <param name="registerDto">The registration details of the new user.</param>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
    {
        await _authService.RegisterAsync(registerDto);
        return NoContent();
    }

    /// <summary>
    /// Handles user logout requests.
    /// </summary>
    /// <param name="userId">The ID of the user to log out.</param>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] Guid userId)
    {
        await _authService.LogoutAsync(userId);
        return NoContent();
    }
}