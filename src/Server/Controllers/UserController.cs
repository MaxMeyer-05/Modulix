using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using Server.Services;

using Server.Models.Dtos;
using Server.Models.Enums;

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
    /// The authenticated user's session data.
    /// </summary>
    private readonly UserSessionDataDto _userSession;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserController"/> class.
    /// </summary>
    /// <param name="userService">The user service instance.</param>
    /// <param name="userSession">The authenticated user's session data.</param>
    public UserController(
        IUserService userService, 
        UserSessionDataDto userSession)
    {
        _userService = userService;
        _userSession = userSession;
    }

    /// <summary>
    /// Retrieves all users.
    /// </summary>
    /// <returns>A collection of user information.</returns>
    [HttpGet]
    [Authorize(Roles = nameof(Roles.Admin))]
    [ProducesResponseType(typeof(IEnumerable<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAllUsers()
    {
        var users = await _userService.GetAllUsersAsync(HttpContext.RequestAborted);
        return Ok(users);
    }

    /// <summary>
    /// Retrieves the authenticated user's information.
    /// </summary>
    /// <returns>The authenticated user's information.</returns>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetCurrentUser()
    {
        var user = await _userService.GetUserByIdAsync(_userSession.UserId, HttpContext.RequestAborted);
        return Ok(user);
    }

    /// <summary>
    /// Updates the authenticated user's information.
    /// </summary>
    /// <param name="updateUserDto">The updated user information.</param>
    /// <returns>The new token result if the update was successful; otherwise, no content.</returns>
    [HttpPatch("me")]
    [ProducesResponseType(typeof(TokenResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TokenResultDto?>> UpdateCurrentUser([FromBody] UpdateUserDto updateUserDto)
    {
        var tokenResult = await _userService.UpdateUserAsync(_userSession.UserId, updateUserDto, HttpContext.RequestAborted);
        if (tokenResult is null)
            return NoContent();
        return Ok(tokenResult);
    }

    /// <summary>
    /// Updates the authenticated user's password.
    /// </summary>
    /// <param name="updatePasswordDto">The current password and new password details.</param>
    /// <returns>A new token result for the authenticated user.</returns>
    [HttpPatch("me/password")]
    [ProducesResponseType(typeof(TokenResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TokenResultDto>> UpdateCurrentUserPassword([FromBody] UpdatePasswordDto updatePasswordDto)
    {
        var tokenResult = await _userService.UpdatePasswordAsync(_userSession.UserId, updatePasswordDto, HttpContext.RequestAborted);
        return Ok(tokenResult);
    }

    /// <summary>
    /// Updates a user's role.
    /// </summary>
    /// <param name="userId">The ID of the user whose role is to be updated.</param>
    /// <param name="updateUserRoleDto">The updated user role information.</param>
    [HttpPatch("{userId}/role")]
    [Authorize(Roles = nameof(Roles.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserRole([FromRoute] Guid userId, [FromBody] UpdateUserRoleDto updateUserRoleDto)
    {
        await _userService.UpdateUserRoleAsync(userId, updateUserRoleDto, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>
    /// Updates a user's allowed scopes.
    /// </summary>
    /// <param name="userId">The ID of the user whose scopes are to be updated.</param>
    /// <param name="updateUserScopesDto">The updated user scopes.</param>
    [HttpPatch("{userId}/scopes")]
    [Authorize(Roles = nameof(Roles.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserScopes([FromRoute] Guid userId, [FromBody] UpdateUserScopesDto updateUserScopesDto)
    {
        await _userService.UpdateUserScopesAsync(userId, updateUserScopesDto, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>
    /// Deletes the authenticated user's account.
    /// </summary>
    /// <param name="deleteUserDto">The current password of the user.</param>
    [HttpDelete("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCurrentUser([FromBody] DeleteCurrentUserDto deleteUserDto)
    {
        await _userService.DeleteUserAsync(_userSession.UserId, deleteUserDto.CurrentPassword, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>
    /// Deletes a user's account by their ID. Only accessible by admins.
    /// </summary>
    /// <param name="userId">The ID of the user to delete.</param>
    [HttpDelete("{userId}")]
    [Authorize(Roles = nameof(Roles.Admin))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteUserById([FromRoute] Guid userId)
    {
        await _userService.DeleteUserAsync(userId, null, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>
    /// Refreshes the JWT token using a valid refresh token.
    /// </summary>
    /// <param name="refreshTokenRequest">The refresh token request.</param>
    /// <returns>The new JWT token pair if the refresh token is valid.</returns>
    [AllowAnonymous]
    [HttpPost("token/refresh")]
    [ProducesResponseType(typeof(TokenResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TokenResultDto>> RefreshToken([FromBody] RefreshTokenRequestDto refreshTokenRequest)
    {
        var tokenResult = await _userService.RefreshTokenAsync(refreshTokenRequest.RefreshToken, HttpContext.RequestAborted);
        return Ok(tokenResult);
    }
}