using Server.Models.Dtos;

namespace Server.Models;

/// <summary>
/// Unit tests for <see cref="RefreshTokenDto.IsActive"/>.
/// </summary>
[Trait("Category", "Models")]
[Trait("SubCategory", "RefreshTokenDto")]
public class RefreshTokenDtoTests
{
    [Fact]
    [Trait("Feature", "ActivityStatus")]
    public void IsActive_FutureUnrevokedToken_ReturnsTrue()
    {
        // Arrange
        var token = new RefreshTokenDto
        {
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(1),
            IsRevoked = false
        };

        // Act & Assert
        Assert.True(token.IsActive);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [Trait("Feature", "ActivityStatus")]
    public void IsActive_RevokedOrExpiredToken_ReturnsFalse(bool isRevoked, int expiryOffsetMinutes)
    {
        // Arrange
        var token = new RefreshTokenDto
        {
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(expiryOffsetMinutes),
            IsRevoked = isRevoked
        };

        // Act & Assert
        Assert.False(token.IsActive);
    }
}