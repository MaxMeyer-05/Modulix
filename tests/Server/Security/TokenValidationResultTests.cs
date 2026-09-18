using Server.Models.Dtos;
using Server.Models.Enums;

using Server.Security.Tokens;

using Server.TestData;

namespace Server.Security;

/// <summary>
/// Unit tests for <see cref="TokenValidationResult"/>.
/// </summary>
[Trait("Category", "Security")]
[Trait("SubCategory", "TokenValidationResult")]
public class TokenValidationResultTests
{
    #region Factory Method Success Tests

    [Theory]
    [ClassData(typeof(TokenValidationSuccessTestData))]
    [Trait("Feature", "SuccessFactory")]
    public void Success_GivenSessionData_ReturnsValidResultWithSessionAndNullError(UserSessionDataDto sessionData)
    {
        // Arrange & Act
        var result = TokenValidationResult.Success(sessionData);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.Same(sessionData, result.UserSessionData);
    }

    [Theory]
    [ClassData(typeof(TokenValidationSuccessTestData))]
    [Trait("Feature", "SuccessFactory")]
    public void Success_GivenSessionData_PreservesSessionPropertiesCorrectly(UserSessionDataDto sessionData)
    {
        // Arrange & Act
        var result = TokenValidationResult.Success(sessionData);

        // Assert
        Assert.NotNull(result.UserSessionData);
        Assert.Equal(sessionData.UserId, result.UserSessionData.UserId);
        Assert.Equal(sessionData.Role, result.UserSessionData.Role);
        Assert.Equal(sessionData.Scopes, result.UserSessionData.Scopes);
        Assert.Equal(sessionData.Claims, result.UserSessionData.Claims);
    }

    #endregion

    #region Factory Method Failure Tests

    [Theory]
    [ClassData(typeof(TokenValidationFailureTestData))]
    [Trait("Feature", "FailureFactory")]
    public void Failure_GivenErrorMessage_ReturnsInvalidResultWithErrorMessageAndNullSession(string? errorMessage)
    {
        // Arrange & Act
        var result = TokenValidationResult.Failure(errorMessage!);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Equal(errorMessage, result.ErrorMessage);
        Assert.Null(result.UserSessionData);
    }

    #endregion

    #region Object Initializer & Direct Assignment Tests

    [Fact]
    [Trait("Feature", "DirectInitialization")]
    public void DirectInitialization_SettingAllProperties_HoldsProvidedValues()
    {
        // Arrange
        var expectedSession = new UserSessionDataDto
        {
            UserId = Guid.NewGuid(),
            Role = Roles.Admin
        };
        const string expectedError = "Custom error message";

        // Act
        var result = new TokenValidationResult
        {
            IsValid = true,
            ErrorMessage = expectedError,
            UserSessionData = expectedSession
        };

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(expectedError, result.ErrorMessage);
        Assert.Same(expectedSession, result.UserSessionData);
    }

    [Fact]
    [Trait("Feature", "DirectInitialization")]
    public void DirectInitialization_DefaultConstructor_InitializesWithDefaultValues()
    {
        // Arrange & Act
        var result = new TokenValidationResult();

        // Assert
        Assert.False(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.UserSessionData);
    }

    #endregion
}