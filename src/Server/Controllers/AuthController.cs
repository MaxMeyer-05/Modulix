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
    /// The authentication service.
    /// </summary>
    private readonly IAuthService _authService;

    /// <summary>
    /// The authenticated user's session data.
    /// </summary>
    private readonly UserSessionDataDto _userSession;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthController"/> class.
    /// </summary>
    /// <param name="authService">The authentication service.</param>
    /// <param name="userSession">The authenticated user's session data.</param>
    public AuthController(IAuthService authService, UserSessionDataDto userSession)
    {
        _authService = authService;
        _userSession = userSession;
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
        var result = await _authService.LoginAsync(loginDto, HttpContext.RequestAborted);
        return Ok(result);
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
        await _authService.RegisterAsync(registerDto, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>
    /// Handles user logout requests.
    /// </summary>
    /// <param name="logoutDto">The refresh token for the session to end.</param>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] LogoutDto logoutDto)
    {
        await _authService.LogoutAsync(_userSession.UserId, logoutDto.RefreshToken, HttpContext.RequestAborted);
        return NoContent();
    }
}