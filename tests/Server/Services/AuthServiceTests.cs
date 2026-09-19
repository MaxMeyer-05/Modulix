using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Server.Database.Entities;
using Server.Database.DbContexts;

using Server.Models.Dtos;
using Server.Models.Enums;

using Server.Security.Tokens;
using Server.Security.Password;

using Server.TestData;

namespace Server.Services;

/// <summary>
/// Unit tests for <see cref="AuthService"/>.
/// </summary>
[Trait("Category", "Services")]
[Trait("SubCategory", "AuthService")]
public class AuthServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServerContext _context;
    private readonly PasswordHasherService _passwordHasher;
    private readonly FakeJwtTokenProvider _tokenProvider;
    private readonly AuthService _sut;

    #region Setup & Teardown

    public AuthServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ServerContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ServerContext(options);
        _context.Database.EnsureCreated();

        _passwordHasher = new PasswordHasherService();
        _tokenProvider = new FakeJwtTokenProvider();

        _sut = new AuthService(
            _context,
            _passwordHasher,
            _tokenProvider,
            NullLogger<AuthService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Login Tests

    [Fact]
    [Trait("Feature", "Login")]
    public async Task LoginAsync_ValidCredentials_ReturnsUserAndTokenResultAndPersistsRefreshToken()
    {
        // Arrange
        const string password = "ValidPassword123!";
        var user = UserTestFixture.CreateTestUser("loginuser", Roles.Admin);
        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        user.AllowedScopes = ["reports.read", "profile.write"];

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var loginDto = new LoginDto
        {
            UserEmail = user.UserEmail,
            UserPassword = password
        };

        // Act
        var (returnedUser, tokenResult) = await _sut.LoginAsync(loginDto);

        // Assert - Return values
        Assert.NotNull(returnedUser);
        Assert.Equal(user.Id, returnedUser.Id);
        Assert.Equal(user.UserEmail, returnedUser.UserEmail);
        Assert.Equal(user.Role, returnedUser.Role);
        Assert.Equal(_tokenProvider.ExpectedAccessToken, tokenResult.AccessToken);
        Assert.Equal(_tokenProvider.ExpectedRefreshToken, tokenResult.RefreshToken);

        // Assert - Database state
        var savedRefreshToken = await _context.RefreshTokens
            .SingleOrDefaultAsync(t => t.UserId == user.Id && t.Token == tokenResult.RefreshToken);

        Assert.NotNull(savedRefreshToken);
        Assert.False(savedRefreshToken.IsRevoked);
        Assert.Equal(tokenResult.RefreshTokenExpiresAtUtc, savedRefreshToken.ExpiresAtUtc);
    }

    [Fact]
    [Trait("Feature", "Login")]
    public async Task LoginAsync_NonExistingEmail_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var loginDto = new LoginDto
        {
            UserEmail = "nonexistent@example.com",
            UserPassword = "AnyPassword123!"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.LoginAsync(loginDto));

        Assert.Equal("Provided login credentials are invalid.", exception.Message);
    }

    [Theory]
    [ClassData(typeof(InvalidLoginCredentialsTestData))]
    [Trait("Feature", "Login")]
    public async Task LoginAsync_InvalidPassword_ThrowsUnauthorizedAccessException(string email, string wrongPassword)
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("existinguser");
        user.UserEmail = email;
        user.PasswordHash = _passwordHasher.HashPassword(user, "CorrectPassword123!");

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var loginDto = new LoginDto
        {
            UserEmail = email,
            UserPassword = wrongPassword
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.LoginAsync(loginDto));

        Assert.Equal("Provided login credentials are invalid.", exception.Message);
    }

    #endregion

    #region Logout Tests

    [Fact]
    [Trait("Feature", "Logout")]
    public async Task LogoutAsync_ValidActiveRefreshToken_RevokesTokenSuccessfully()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("logoutuser");
        _context.Users.Add(user);

        const string refreshTokenValue = "valid-active-refresh-token";
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenValue,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            IsRevoked = false
        };
        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        // Act
        await _sut.LogoutAsync(user.Id, refreshTokenValue);

        // Assert
        var updatedToken = await _context.RefreshTokens
            .SingleAsync(t => t.Token == refreshTokenValue);

        Assert.True(updatedToken.IsRevoked);
    }

    [Fact]
    [Trait("Feature", "Logout")]
    public async Task LogoutAsync_TokenDoesNotExist_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.LogoutAsync(userId, "non-existent-token"));

        Assert.Equal("Provided refresh token is invalid.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "Logout")]
    public async Task LogoutAsync_TokenBelongsToDifferentUser_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var userA = UserTestFixture.CreateTestUser("userA");
        var userBId = Guid.NewGuid();
        _context.Users.Add(userA);

        const string tokenValue = "user-a-refresh-token";
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = userA.Id,
            Token = tokenValue,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
            IsRevoked = false
        });
        await _context.SaveChangesAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.LogoutAsync(userBId, tokenValue));

        Assert.Equal("Provided refresh token is invalid.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "Logout")]
    public async Task LogoutAsync_TokenAlreadyRevoked_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("revokeduser");
        _context.Users.Add(user);

        const string tokenValue = "already-revoked-token";
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = tokenValue,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
            IsRevoked = true
        });
        await _context.SaveChangesAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.LogoutAsync(user.Id, tokenValue));

        Assert.Equal("Provided refresh token is invalid.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "Logout")]
    public async Task LogoutAsync_TokenExpired_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("expiredlogoutuser");
        _context.Users.Add(user);

        const string tokenValue = "expired-refresh-token";
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = tokenValue,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),
            IsRevoked = false
        });
        await _context.SaveChangesAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.LogoutAsync(user.Id, tokenValue));

        Assert.Equal("Provided refresh token is invalid.", exception.Message);
    }

    #endregion

    #region Registration Tests

    [Theory]
    [ClassData(typeof(ValidRegistrationTestData))]
    [Trait("Feature", "Registration")]
    public async Task RegisterAsync_ValidData_PersistsUserWithHashedPassword(RegisterDto registerDto)
    {
        // Arrange & Act
        await _sut.RegisterAsync(registerDto);

        // Assert
        var createdUser = await _context.Users
            .SingleOrDefaultAsync(u => u.UserEmail == registerDto.UserEmail);

        Assert.NotNull(createdUser);
        Assert.Equal(registerDto.UserEmail, createdUser.UserEmail);
        Assert.Equal(Roles.User, createdUser.Role);
        Assert.Null(createdUser.AllowedScopes);
        Assert.NotEqual(registerDto.UserPassword, createdUser.PasswordHash);
        Assert.True(_passwordHasher.VerifyPassword(createdUser, registerDto.UserPassword, createdUser.PasswordHash));
    }

    [Theory]
    [ClassData(typeof(MismatchedRegistrationPasswordsTestData))]
    [Trait("Feature", "Registration")]
    public async Task RegisterAsync_PasswordMismatch_ThrowsArgumentException(string password, string confirmPassword)
    {
        // Arrange
        var registerDto = new RegisterDto
        {
            UserEmail = "test@example.com",
            UserPassword = password,
            Confirm_UserPassword = confirmPassword
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.RegisterAsync(registerDto));

        Assert.Equal("Passwords do not match.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "Registration")]
    public async Task RegisterAsync_EmailAlreadyExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var existingUser = UserTestFixture.CreateTestUser("duplicateuser");
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var registerDto = new RegisterDto
        {
            UserEmail = existingUser.UserEmail,
            UserPassword = "Password123!",
            Confirm_UserPassword = "Password123!"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.RegisterAsync(registerDto));

        Assert.Equal("Email is already registered.", exception.Message);
    }

    #endregion

    #region Test Fakes & Helpers

    private sealed class FakeJwtTokenProvider : IJwtTokenProvider
    {
        public string ExpectedAccessToken { get; set; } = "mock.access.token";
        public string ExpectedRefreshToken { get; set; } = "mock-refresh-token";
        public DateTime ExpectedAccessExpiry { get; set; } = DateTime.UtcNow.AddMinutes(15);
        public DateTime ExpectedRefreshExpiry { get; set; } = DateTime.UtcNow.AddDays(7);

        public TokenResultDto CreateTokenPair(Guid userId, Roles role, IEnumerable<string>? scopes) =>
            new()
            {
                AccessToken = ExpectedAccessToken,
                RefreshToken = ExpectedRefreshToken,
                AccessTokenExpiresAtUtc = ExpectedAccessExpiry,
                RefreshTokenExpiresAtUtc = ExpectedRefreshExpiry
            };

        public string GenerateAccessToken(Guid userId, Roles role, IEnumerable<string>? scopes, DateTime issuedAtUtc) =>
            ExpectedAccessToken;

        public RefreshToken GenerateRefreshToken(Guid userId, int daysLifetime, DateTime issuedAtUtc) =>
            new()
            {
                UserId = userId,
                Token = ExpectedRefreshToken,
                ExpiresAtUtc = issuedAtUtc.AddDays(daysLifetime),
                IsRevoked = false,
                CreatedAtUtc = issuedAtUtc
            };
    }

    #endregion
}