using Microsoft.EntityFrameworkCore;

using Server.Security.Tokens;
using Server.Security.Password;

using Server.Mappers;
using Server.Models.Dtos;
using Server.Database.DbContexts;

namespace Server.Services;

/// <inheritdoc cref="IAuthService"/>
public class AuthService : IAuthService
{
    /// <summary>
    /// The database context.
    /// </summary>
    private readonly ServerContext _context;

    /// <summary>
    /// The password hasher service.
    /// </summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>
    /// The JWT token provider service.
    /// </summary>
    private readonly IJwtTokenProvider _tokenService;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<AuthService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthService"/> class.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="passwordHasher">The password hasher service.</param>
    /// <param name="tokenService">The JWT token provider service.</param>
    /// <param name="logger">The logger.</param>
    public AuthService(
        ServerContext context, 
        IPasswordHasher passwordHasher, 
        IJwtTokenProvider tokenService,
        ILogger<AuthService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<(UserDto, TokenResultDto)> LoginAsync(LoginDto loginDto, CancellationToken ct = default)
    {
        var user = await _context.Users.SingleOrDefaultAsync(user => user.UserEmail == loginDto.UserEmail, ct);
        if (user is null)
            throw new UnauthorizedAccessException("Provided login credentials are invalid.");
        
        if (!_passwordHasher.VerifyPassword(user, loginDto.UserPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Provided login credentials are invalid.");

        var tokenResult = _tokenService.CreateTokenPair(user.Id, user.Role, user.AllowedScopes);
        var userDto = user.ToUserDto();

        _context.RefreshTokens.Add(new()
        {
            Token = tokenResult.RefreshToken,
            UserId = user.Id,
            ExpiresAtUtc = tokenResult.RefreshTokenExpiresAtUtc
        });
        await _context.SaveChangesAsync(ct);

        _logger.LogDebug("Login completed for user {UserId}.", user.Id);
        return (userDto, tokenResult);
    }

    /// <inheritdoc/>
    public async Task LogoutAsync(Guid userId, string refreshToken, CancellationToken ct = default)
    {
        var refreshTokenEntity = await _context.RefreshTokens.SingleOrDefaultAsync(
            token => token.UserId == userId && token.Token == refreshToken && !token.IsRevoked,
            ct);
        if (refreshTokenEntity is null)
            throw new UnauthorizedAccessException("Provided refresh token is invalid.");

        refreshTokenEntity.IsRevoked = true;
        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task RegisterAsync(RegisterDto registerDto, CancellationToken ct = default)
    {
        if (registerDto.UserPassword != registerDto.Confirm_UserPassword)
            throw new ArgumentException("Passwords do not match.");

        var existingUser = await _context.Users
            .SingleOrDefaultAsync(user => user.UserEmail == registerDto.UserEmail, ct);
        if (existingUser is not null)
            throw new InvalidOperationException("Email is already registered.");

        var user = registerDto.ToUserEntity();
        user.PasswordHash = _passwordHasher.HashPassword(user, registerDto.UserPassword);

        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);
    }
}