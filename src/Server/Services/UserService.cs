using Microsoft.EntityFrameworkCore;

using Server.Mappers;
using Server.Models.Dtos;
using Server.Database.DbContexts;

using Server.Security.Tokens;
using Server.Security.Password;

namespace Server.Services;

/// <inheritdoc cref="IUserService"/>
public class UserService : IUserService
{
    /// <summary>
    /// The database context.
    /// </summary>
    private readonly ServerContext _context;

    /// <summary>
    /// The JWT token provider service.
    /// </summary>
    private readonly IJwtTokenProvider _tokenService;

    /// <summary>
    /// The password hasher service.
    /// </summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<UserService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserService"/> class.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="tokenService">The JWT token provider service.</param>
    /// <param name="passwordHasher">The password hasher service.</param>
    /// <param name="logger">The logger instance.</param>
    public UserService(
        ServerContext context, 
        IJwtTokenProvider tokenService,
        IPasswordHasher passwordHasher,
        ILogger<UserService> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task DeleteUserAsync(Guid userId, string password, CancellationToken ct)
    {
        var user = await _context.Users.FindAsync(userId, ct);
        if (user is null)
            throw new KeyNotFoundException($"User with ID '{userId}' not found.");

        if (!_passwordHasher.VerifyPassword(user, password, user.PasswordHash))
            throw new UnauthorizedAccessException("Provided password is invalid.");

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(ct);
        _logger.LogDebug("Deleted user with ID '{UserId}' from the database.", userId);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<UserDto>> GetAllUsersAsync(CancellationToken ct)
    {
        var users = await _context.Users.ToListAsync(ct);
        var userDtos = users.Select(user => user.ToUserDto()).ToList();

        _logger.LogDebug("Retrieved {Count} users from the database.", userDtos.Count);
        return userDtos;
    }

    /// <inheritdoc/>
    public async Task<UserDto> GetUserByIdAsync(Guid userId, CancellationToken ct)
    {
        var user = await _context.Users.FindAsync(userId, ct);
        if (user is null)
            throw new KeyNotFoundException($"User with ID '{userId}' not found.");

        _logger.LogDebug("Retrieved user with ID '{UserId}' from the database.", userId);
        return user.ToUserDto();
    }

    /// <inheritdoc/>
    public async Task<TokenResultDto> RefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        var refreshTokenEntity = await _context.RefreshTokens
            .SingleOrDefaultAsync(rt => rt.Token == refreshToken, ct);
        if (refreshTokenEntity is null)
            throw new UnauthorizedAccessException("Invalid refresh token.");

        var user = await _context.Users.FindAsync(refreshTokenEntity.UserId, ct);
        if (user is null)
            throw new KeyNotFoundException($"User with ID '{refreshTokenEntity.UserId}' not found.");

        var tokenResult = _tokenService.CreateTokenPair(user.Id, user.Role, user.AllowedScopes);
        return tokenResult;
    }

    /// <inheritdoc/>
    public async Task UpdateUserAsync(Guid userId, UpdateUserDto userDto, CancellationToken ct)
    {
        var user = await _context.Users.FindAsync(userId, ct);
        if (user is null)
            throw new KeyNotFoundException($"User with ID '{userId}' not found.");

        if (!string.IsNullOrEmpty(userDto.New_UserPassword) 
            && !string.IsNullOrEmpty(userDto.Current_UserPassword)
            && userDto.New_UserPassword == userDto.Confirm_New_UserPassword)
        {
            if (!_passwordHasher.VerifyPassword(user, userDto.Current_UserPassword, user.PasswordHash))
                throw new UnauthorizedAccessException("Current password is invalid.");

            user.PasswordHash = _passwordHasher.HashPassword(user, userDto.New_UserPassword);
        }

        user.UpdateUserFromDto(userDto);

        _context.ChangeTracker.DetectChanges();
        if (_context.Entry(user).State == EntityState.Unchanged)
            return;

        await _context.SaveChangesAsync(ct);
        _logger.LogDebug("Updated user with ID '{UserId}' in the database.", userId);
    }
}