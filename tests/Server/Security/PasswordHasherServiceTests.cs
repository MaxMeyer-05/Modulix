using Server.Models.Dtos;
using Server.Security.Password;

using Server.TestData;

namespace Server.Security;

/// <summary>
/// Unit tests for <see cref="PasswordHasherService"/>.
/// </summary>
[Trait("Category", "Security")]
[Trait("SubCategory", "PasswordHashing")]
public class PasswordHasherServiceTests
{
    private readonly PasswordHasherService _sut = new();
    private readonly UserDto _defaultUser = UserDtoTestFixture.CreateTestUser();

    #region Password Hashing Tests

    [Theory]
    [ClassData(typeof(ValidPasswordTestData))]
    [Trait("Feature", "HashGeneration")]
    public void HashPassword_GivenValidPassword_ProducesNonEmptySecureHash(string plainPassword)
    {
        // Arrange & Act
        var hash = _sut.HashPassword(_defaultUser, plainPassword);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.NotEqual(plainPassword, hash);
    }

    [Fact]
    [Trait("Feature", "Salting")]
    public void HashPassword_SamePasswordHashedTwice_ProducesDifferentHashesDueToSalting()
    {
        // Arrange
        const string password = "IdenticalPasswordForSaltTest123!";

        // Act
        var firstHash = _sut.HashPassword(_defaultUser, password);
        var secondHash = _sut.HashPassword(_defaultUser, password);

        // Assert
        Assert.NotEqual(firstHash, secondHash);
    }

    #endregion

    #region Password Verification Success Tests

    [Theory]
    [ClassData(typeof(ValidPasswordTestData))]
    [Trait("Feature", "Verification")]
    public void VerifyPassword_GivenMatchingPasswordAndHash_ReturnsTrue(string plainPassword)
    {
        // Arrange
        var hash = _sut.HashPassword(_defaultUser, plainPassword);

        // Act
        var isVerified = _sut.VerifyPassword(_defaultUser, plainPassword, hash);

        // Assert
        Assert.True(isVerified);
    }

    [Fact]
    [Trait("Feature", "Verification")]
    public void VerifyPassword_DifferentUserInstancesWithSameData_VerifiesSuccessfully()
    {
        // Arrange
        const string password = "MultiUserVerificationPassword123!";
        var registrationUser = UserDtoTestFixture.CreateTestUser();
        var loginUser = UserDtoTestFixture.CreateTestUser();
        var hash = _sut.HashPassword(registrationUser, password);

        // Act
        var isVerified = _sut.VerifyPassword(loginUser, password, hash);

        // Assert
        Assert.True(isVerified);
    }

    #endregion

    #region Password Verification Failure Tests

    [Theory]
    [ClassData(typeof(MismatchedPasswordTestData))]
    [Trait("Feature", "Verification")]
    public void VerifyPassword_GivenWrongPassword_ReturnsFalse(string actualPassword, string wrongAttempt)
    {
        // Arrange
        var hash = _sut.HashPassword(_defaultUser, actualPassword);

        // Act
        var isVerified = _sut.VerifyPassword(_defaultUser, wrongAttempt, hash);

        // Assert
        Assert.False(isVerified);
    }

    [Theory]
    [ClassData(typeof(CorruptedHashTestData))]
    [Trait("Feature", "Verification")]
    public void VerifyPassword_GivenCorruptedHash_ReturnsFalseOrThrowsGracefully(string corruptedHash)
    {
        // Arrange
        const string password = "AnyPassword123!";

        // Act
        var isVerified = false;
        var threwFormatException = false;

        try
        {
            isVerified = _sut.VerifyPassword(_defaultUser, password, corruptedHash);
        }
        catch (FormatException)
        {
            threwFormatException = true;
        }

        // Assert
        Assert.True(!isVerified || threwFormatException);
    }

    #endregion
}