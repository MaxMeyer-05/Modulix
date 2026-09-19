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
/// Unit tests for <see cref="UserService"/>.
/// </summary>
[Trait("Category", "Services")]
[Trait("SubCategory", "UserService")]
public class UserServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServerContext _context;
    private readonly PasswordHasherService _passwordHasher;
    private readonly FakeJwtTokenProvider _tokenProvider;
    private readonly UserService _sut;

    #region Setup & Teardown

    public UserServiceTests()
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

        _sut = new UserService(
            _context,
            _tokenProvider,
            _passwordHasher,
            NullLogger<UserService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region DeleteUser Tests

    [Fact]
    [Trait("Feature", "DeleteUser")]
    public async Task DeleteUserAsync_UserDoesNotExist_ThrowsKeyNotFoundException()
    {
        // Arrange
        var missingUserId = Guid.NewGuid();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.DeleteUserAsync(missingUserId, "any-password"));

        Assert.Equal($"User with ID '{missingUserId}' not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "DeleteUser")]
    public async Task DeleteUserAsync_PasswordProvidedAndInvalid_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("deleteuser");
        user.PasswordHash = _passwordHasher.HashPassword(user, "CorrectPassword123!");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.DeleteUserAsync(user.Id, "WrongPassword123!"));

        Assert.Equal("Provided password is invalid.", exception.Message);
        Assert.NotNull(await _context.Users.FindAsync(user.Id));
    }

    [Fact]
    [Trait("Feature", "DeleteUser")]
    public async Task DeleteUserAsync_WithoutPassword_DeletesUserSuccessfully()
    {
        // Arrange (e.g. Admin deletion where password confirmation is omitted)
        var user = UserTestFixture.CreateTestUser("admin_delete_user");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act
        await _sut.DeleteUserAsync(user.Id, password: null);

        // Assert
        var deletedUser = await _context.Users.FindAsync(user.Id);
        Assert.Null(deletedUser);
    }

    [Fact]
    [Trait("Feature", "DeleteUser")]
    public async Task DeleteUserAsync_ValidPasswordProvided_DeletesUserSuccessfully()
    {
        // Arrange
        const string password = "ValidUserPassword123!";
        var user = UserTestFixture.CreateTestUser("self_delete_user");
        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act
        await _sut.DeleteUserAsync(user.Id, password);

        // Assert
        var deletedUser = await _context.Users.FindAsync(user.Id);
        Assert.Null(deletedUser);
    }

    #endregion

    #region GetAllUsers Tests

    [Fact]
    [Trait("Feature", "GetAllUsers")]
    public async Task GetAllUsersAsync_NoUsersInDatabase_ReturnsEmptyCollection()
    {
        // Act
        var result = await _sut.GetAllUsersAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    [Trait("Feature", "GetAllUsers")]
    public async Task GetAllUsersAsync_MultipleUsers_ReturnsUsersOrderedByEmail()
    {
        // Arrange
        var userB = new User
        {
            Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            UserEmail = "b_user@example.com",
            PasswordHash = "hash-b",
            Role = Roles.User
        };
        var userA = new User
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            UserEmail = "a_second_user@example.com",
            PasswordHash = "hash-a",
            Role = Roles.User
        };
        var userC = new User
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            UserEmail = "c_user@example.com",
            PasswordHash = "hash-c",
            Role = Roles.Admin
        };

        _context.Users.AddRange(userB, userA, userC);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _sut.GetAllUsersAsync()).ToList();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(userA.Id, result[0].Id);
        Assert.Equal(userB.Id, result[1].Id);
        Assert.Equal(userC.Id, result[2].Id);
    }

    #endregion

    #region GetUserById Tests

    [Fact]
    [Trait("Feature", "GetUserById")]
    public async Task GetUserByIdAsync_UserExists_ReturnsMappedUserDto()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("founduser", Roles.Admin);
        user.AllowedScopes = ["reports.read", "profile.edit"];
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetUserByIdAsync(user.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.UserEmail, result.UserEmail);
        Assert.Equal(user.Role, result.Role);
        Assert.Equal(user.AllowedScopes, result.AllowedScopes);
    }

    [Fact]
    [Trait("Feature", "GetUserById")]
    public async Task GetUserByIdAsync_UserNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var missingId = Guid.NewGuid();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.GetUserByIdAsync(missingId));

        Assert.Equal($"User with ID '{missingId}' not found.", exception.Message);
    }

    #endregion

    #region RefreshToken Tests

    [Fact]
    [Trait("Feature", "RefreshToken")]
    public async Task RefreshTokenAsync_ValidActiveToken_RotatesTokenAndReturnsNewPair()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("refreshuser");
        var otherUser = UserTestFixture.CreateTestUser("otherrefreshuser");
        otherUser.Id = Guid.NewGuid();
        _context.Users.Add(user);
        _context.Users.Add(otherUser);

        const string activeToken = "valid-active-refresh-token";
        var originalRefreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = activeToken,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            IsRevoked = false
        };
        _context.RefreshTokens.Add(originalRefreshToken);
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = otherUser.Id,
            Token = "other-user-refresh-token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            IsRevoked = false
        });
        await _context.SaveChangesAsync();

        // Act
        var tokenResult = await _sut.RefreshTokenAsync(activeToken);

        // Assert - Return DTO
        Assert.NotNull(tokenResult);
        Assert.Equal(_tokenProvider.ExpectedAccessToken, tokenResult.AccessToken);
        Assert.Equal(_tokenProvider.ExpectedRefreshToken, tokenResult.RefreshToken);

        // Assert - Old token is revoked
        var oldTokenEntity = await _context.RefreshTokens.SingleAsync(rt => rt.Token == activeToken);
        Assert.True(oldTokenEntity.IsRevoked);

        // Assert - New token is persisted
        var newTokenEntity = await _context.RefreshTokens
            .SingleOrDefaultAsync(rt => rt.Token == _tokenProvider.ExpectedRefreshToken);
        Assert.NotNull(newTokenEntity);
        Assert.False(newTokenEntity.IsRevoked);
        Assert.Equal(user.Id, newTokenEntity.UserId);
        Assert.Equal(_tokenProvider.ExpectedRefreshExpiry, newTokenEntity.ExpiresAtUtc);

        var otherUserToken = await _context.RefreshTokens
            .SingleAsync(rt => rt.Token == "other-user-refresh-token");
        Assert.False(otherUserToken.IsRevoked);
    }

    [Fact]
    [Trait("Feature", "RefreshToken")]
    public async Task RefreshTokenAsync_TokenRevokedOrExpired_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("expiredtokenuser");
        _context.Users.Add(user);
        const string expiredToken = "expired-token";
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = expiredToken,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5),
            IsRevoked = false
        });
        await _context.SaveChangesAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.RefreshTokenAsync(expiredToken));

        Assert.Equal("Invalid refresh token.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "RefreshToken")]
    public async Task RefreshTokenAsync_TokenAlreadyRevoked_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("revokedtokenuser");
        _context.Users.Add(user);
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = "revoked-token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            IsRevoked = true
        });
        await _context.SaveChangesAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.RefreshTokenAsync("revoked-token"));

        Assert.Equal("Invalid refresh token.", exception.Message);
    }

    #endregion

    #region UpdateUser Tests

    [Fact]
    [Trait("Feature", "UpdateUser")]
    public async Task UpdateUserAsync_UserNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var missingUserId = Guid.NewGuid();
        var updateDto = new UpdateUserDto { UserEmail = "new@example.com" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateUserAsync(missingUserId, updateDto));

        Assert.Equal($"User with ID '{missingUserId}' not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "UpdateUser")]
    public async Task UpdateUserAsync_NoStateChanges_ReturnsNull()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("unmodified");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // DTO with identical email
        var updateDto = new UpdateUserDto { UserEmail = user.UserEmail };

        // Act
        var result = await _sut.UpdateUserAsync(user.Id, updateDto);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    [Trait("Feature", "UpdateUser")]
    public async Task UpdateUserAsync_EmailNotSpecified_ReturnsNullAndLeavesRefreshTokensUntouched()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("unspecifiedemail");
        _context.Users.Add(user);
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = "existing-token",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.UpdateUserAsync(user.Id, new UpdateUserDto());

        // Assert
        Assert.Null(result);
        var tokens = await _context.RefreshTokens.Where(rt => rt.UserId == user.Id).ToListAsync();
        Assert.Single(tokens);
        Assert.Equal("existing-token", tokens[0].Token);
    }

    [Fact]
    [Trait("Feature", "UpdateUser")]
    public async Task UpdateUserAsync_DataChanged_RemovesOldTokensAndIssuesNewTokenResult()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("changeuser");
        _context.Users.Add(user);

        // Pre-existing tokens
        _context.RefreshTokens.AddRange(
            new RefreshToken { UserId = user.Id, Token = "old-token-1", ExpiresAtUtc = DateTime.UtcNow.AddDays(1) },
            new RefreshToken { UserId = user.Id, Token = "old-token-2", ExpiresAtUtc = DateTime.UtcNow.AddDays(1) }
        );
        await _context.SaveChangesAsync();

        var updateDto = new UpdateUserDto { UserEmail = "newemail@example.com" };

        // Act
        var result = await _sut.UpdateUserAsync(user.Id, updateDto);

        // Assert - New TokenResult returned
        Assert.NotNull(result);
        Assert.Equal(_tokenProvider.ExpectedRefreshToken, result.RefreshToken);

        // Assert - Old tokens removed and new token persisted
        var userTokens = await _context.RefreshTokens.Where(rt => rt.UserId == user.Id).ToListAsync();
        Assert.Single(userTokens);
        Assert.Equal(_tokenProvider.ExpectedRefreshToken, userTokens[0].Token);

        // Assert - Email updated
        var updatedUser = await _context.Users.FindAsync(user.Id);
        Assert.Equal("newemail@example.com", updatedUser!.UserEmail);
    }

    #endregion

    #region UpdatePassword Tests

    [Theory]
    [ClassData(typeof(MismatchedUpdatePasswordTestData))]
    [Trait("Feature", "UpdatePassword")]
    public async Task UpdatePasswordAsync_PasswordsDoNotMatch_ThrowsArgumentException(UpdatePasswordDto dto)
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdatePasswordAsync(Guid.NewGuid(), dto));

        Assert.Equal("New password and password confirmation do not match.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "UpdatePassword")]
    public async Task UpdatePasswordAsync_UserNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var missingUserId = Guid.NewGuid();
        var dto = new UpdatePasswordDto
        {
            CurrentPassword = "CurrentPassword123!",
            NewPassword = "NewPassword123!",
            ConfirmNewPassword = "NewPassword123!"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdatePasswordAsync(missingUserId, dto));

        Assert.Equal($"User with ID '{missingUserId}' not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "UpdatePassword")]
    public async Task UpdatePasswordAsync_CurrentPasswordInvalid_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("wrongpwduser");
        user.PasswordHash = _passwordHasher.HashPassword(user, "ActualPassword123!");
        _context.Users.Add(user);
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = "existing-token",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await _context.SaveChangesAsync();

        var dto = new UpdatePasswordDto
        {
            CurrentPassword = "IncorrectPassword!",
            NewPassword = "NewSecretPassword123!",
            ConfirmNewPassword = "NewSecretPassword123!"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.UpdatePasswordAsync(user.Id, dto));

        Assert.Equal("Current password is invalid.", exception.Message);
        Assert.True(_passwordHasher.VerifyPassword(user, "ActualPassword123!", user.PasswordHash));
        var tokens = await _context.RefreshTokens.Where(rt => rt.UserId == user.Id).ToListAsync();
        Assert.Single(tokens);
        Assert.Equal("existing-token", tokens[0].Token);
    }

    [Fact]
    [Trait("Feature", "UpdatePassword")]
    public async Task UpdatePasswordAsync_ValidRequest_UpdatesPasswordHashAndReplacesRefreshTokens()
    {
        // Arrange
        const string oldPassword = "OldPassword123!";
        const string newPassword = "NewPassword123!";
        var user = UserTestFixture.CreateTestUser("successpwduser");
        user.PasswordHash = _passwordHasher.HashPassword(user, oldPassword);
        _context.Users.Add(user);

        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = "existing-token-before-password-change",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await _context.SaveChangesAsync();

        var dto = new UpdatePasswordDto
        {
            CurrentPassword = oldPassword,
            NewPassword = newPassword,
            ConfirmNewPassword = newPassword
        };

        // Act
        var result = await _sut.UpdatePasswordAsync(user.Id, dto);

        // Assert - Return token pair
        Assert.NotNull(result);
        Assert.Equal(_tokenProvider.ExpectedRefreshToken, result.RefreshToken);

        // Assert - Password hash updated
        var updatedUser = await _context.Users.FindAsync(user.Id);
        Assert.True(_passwordHasher.VerifyPassword(updatedUser!, newPassword, updatedUser!.PasswordHash));

        // Assert - Previous refresh tokens cleared and replaced
        var userTokens = await _context.RefreshTokens.Where(rt => rt.UserId == user.Id).ToListAsync();
        Assert.Single(userTokens);
        Assert.Equal(_tokenProvider.ExpectedRefreshToken, userTokens[0].Token);
    }

    #endregion

    #region UpdateUserScopes Tests

    [Fact]
    [Trait("Feature", "UpdateUserScopes")]
    public async Task UpdateUserScopesAsync_UserNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var missingUserId = Guid.NewGuid();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateUserScopesAsync(missingUserId, new UpdateUserScopesDto { AllowedScopes = ["profile.read"] }));

        Assert.Equal($"User with ID '{missingUserId}' not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "UpdateUserScopes")]
    public async Task UpdateUserScopesAsync_ScopesUnchanged_LeavesDatabaseUntouched()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("samescopes");
        user.AllowedScopes = ["profile.read"];
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var dto = new UpdateUserScopesDto { AllowedScopes = ["profile.read"] };

        // Act
        await _sut.UpdateUserScopesAsync(user.Id, dto);

        // Assert
        Assert.Equal(EntityState.Unchanged, _context.Entry(user).State);
    }

    [Theory]
    [ClassData(typeof(UserScopesUpdateTestData))]
    [Trait("Feature", "UpdateUserScopes")]
    public async Task UpdateUserScopesAsync_ScopesChanged_PersistsNewScopes(List<string> newScopes)
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("changescopes");
        user.AllowedScopes = ["initial.scope"];
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var dto = new UpdateUserScopesDto { AllowedScopes = newScopes };

        // Act
        await _sut.UpdateUserScopesAsync(user.Id, dto);

        // Assert
        var updatedUser = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.NotNull(updatedUser.AllowedScopes);
        Assert.Equal(newScopes.OrderBy(s => s), updatedUser.AllowedScopes!.OrderBy(s => s));
    }

    [Fact]
    [Trait("Feature", "UpdateUserScopes")]
    public async Task UpdateUserScopesAsync_ScopesAreNormalizedBeforePersistence()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("normalizescopes");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var dto = new UpdateUserScopesDto
        {
            AllowedScopes = [" reports.read ", "", "reports.read", "  ", "profile.write "]
        };

        // Act
        await _sut.UpdateUserScopesAsync(user.Id, dto);

        // Assert
        var updatedUser = await _context.Users.FindAsync(user.Id);
        Assert.Equal(["reports.read", "profile.write"], updatedUser!.AllowedScopes);
    }

    #endregion

    #region UpdateUserRole Tests

    [Fact]
    [Trait("Feature", "UpdateUserRole")]
    public async Task UpdateUserRoleAsync_UserNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var missingUserId = Guid.NewGuid();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateUserRoleAsync(missingUserId, new UpdateUserRoleDto { Role = Roles.Admin }));

        Assert.Equal($"User with ID '{missingUserId}' not found.", exception.Message);
    }

    [Fact]
    [Trait("Feature", "UpdateUserRole")]
    public async Task UpdateUserRoleAsync_RoleUnchanged_LeavesDatabaseUntouched()
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("samerole", Roles.User);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var dto = new UpdateUserRoleDto { Role = Roles.User };

        // Act
        await _sut.UpdateUserRoleAsync(user.Id, dto);

        // Assert
        Assert.Equal(EntityState.Unchanged, _context.Entry(user).State);
    }

    [Theory]
    [ClassData(typeof(UserRoleUpdateTestData))]
    [Trait("Feature", "UpdateUserRole")]
    public async Task UpdateUserRoleAsync_RoleChanged_PersistsNewRole(Roles initialRole, Roles targetRole)
    {
        // Arrange
        var user = UserTestFixture.CreateTestUser("changerole", initialRole);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var dto = new UpdateUserRoleDto { Role = targetRole };

        // Act
        await _sut.UpdateUserRoleAsync(user.Id, dto);

        // Assert
        var updatedUser = await _context.Users.FindAsync(user.Id);
        Assert.Equal(targetRole, updatedUser!.Role);
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