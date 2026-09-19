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
    [ProducesResponseType(typeof((UserDto, TokenResultDto)), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<(UserDto, TokenResultDto)>> Login([FromBody] LoginDto loginDto)
    {
        try
        {
            var result = await _authService.LoginAsync(loginDto);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Handles user registration requests.
    /// </summary>
    /// <param name="registerDto">The registration details of the new user.</param>
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
    {
        try
        {
            await _authService.RegisterAsync(registerDto);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Handles user logout requests.
    /// </summary>
    /// <param name="userId">The ID of the user to log out.</param>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout([FromBody] Guid userId)
    {
        try
        {
            await _authService.LogoutAsync(userId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}