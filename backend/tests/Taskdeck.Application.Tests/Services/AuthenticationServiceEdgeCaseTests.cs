using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Edge-case tests for AuthenticationService: login, registration, password change,
/// token validation, and external login scenarios not covered by the happy-path suite.
/// Linked to #707 (TST-40).
/// </summary>
public class AuthenticationServiceEdgeCaseTests
{
    private static readonly JwtSettings DefaultJwtSettings = new()
    {
        SecretKey = "TaskdeckTestsOnlySecretKeyMustBeLongEnough123!",
        Issuer = "TaskdeckTests",
        Audience = "TaskdeckUsers",
        ExpirationMinutes = 60
    };

    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IExternalLoginRepository> _externalLoginRepoMock;

    public AuthenticationServiceEdgeCaseTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _userRepoMock = new Mock<IUserRepository>();
        _externalLoginRepoMock = new Mock<IExternalLoginRepository>();

        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ExternalLogins).Returns(_externalLoginRepoMock.Object);
    }

    // ─────────────────────────────────────────────────────────
    // Login edge cases
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "password")]
    [InlineData("", "password")]
    [InlineData("   ", "password")]
    public async Task LoginAsync_ShouldReturnValidationError_WhenUsernameOrEmailIsBlank(
        string? usernameOrEmail, string password)
    {
        var service = CreateService();
        var result = await service.LoginAsync(new LoginDto(usernameOrEmail!, password));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnForbidden_WhenOnlyMatchIsInactiveUser()
    {
        var password = "correctPassword1";
        var user = new User("inactiveuser", "inactive@test.com", BCrypt.Net.BCrypt.HashPassword(password));
        user.Deactivate();
        var service = CreateService();

        _userRepoMock.Setup(r => r.GetByUsernameAsync("inactiveuser", default)).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.GetByEmailAsync("inactiveuser", default)).ReturnsAsync((User?)null);

        var result = await service.LoginAsync(new LoginDto("inactiveuser", password));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("inactive");
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnAuthFailed_WhenNoUserExists()
    {
        var service = CreateService();
        _userRepoMock.Setup(r => r.GetByUsernameAsync("nonexistent", default)).ReturnsAsync((User?)null);
        _userRepoMock.Setup(r => r.GetByEmailAsync("nonexistent", default)).ReturnsAsync((User?)null);

        var result = await service.LoginAsync(new LoginDto("nonexistent", "password"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.AuthenticationFailed);
    }

    [Fact]
    public async Task LoginAsync_ShouldSucceed_AfterPreviousFailedAttempt()
    {
        var password = "correctPassword1";
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword(password));
        var service = CreateService();

        _userRepoMock.Setup(r => r.GetByUsernameAsync("testuser", default)).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.GetByEmailAsync("testuser", default)).ReturnsAsync((User?)null);

        // First: wrong password
        var failResult = await service.LoginAsync(new LoginDto("testuser", "wrongPassword"));
        failResult.IsSuccess.Should().BeFalse();

        // Then: correct password still works (no lockout)
        var successResult = await service.LoginAsync(new LoginDto("testuser", password));
        successResult.IsSuccess.Should().BeTrue();
        successResult.Value.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LoginAsync_ConcurrentLogins_ShouldReturnDifferentJtis()
    {
        var password = "password123";
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword(password));
        var service = CreateService();

        _userRepoMock.Setup(r => r.GetByUsernameAsync("testuser", default)).ReturnsAsync(user);

        var result1 = await service.LoginAsync(new LoginDto("testuser", password));
        var result2 = await service.LoginAsync(new LoginDto("testuser", password));

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();

        // Both tokens valid but have different JTI (unique token IDs)
        var handler = new JwtSecurityTokenHandler();
        var jwt1 = handler.ReadJwtToken(result1.Value.Token);
        var jwt2 = handler.ReadJwtToken(result2.Value.Token);
        jwt1.Id.Should().NotBe(jwt2.Id);
    }

    // ─────────────────────────────────────────────────────────
    // Registration edge cases
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_ShouldReturnConflict_WhenDuplicateEmail()
    {
        var service = CreateService();
        _userRepoMock.Setup(r => r.ExistsAsync("newuser", "existing@test.com", default)).ReturnsAsync(true);

        var result = await service.RegisterAsync(new CreateUserDto("newuser", "existing@test.com", "password123"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Theory]
    [InlineData("", "email@test.com", "password123")]
    [InlineData("   ", "email@test.com", "password123")]
    [InlineData("user", "", "password123")]
    [InlineData("user", "   ", "password123")]
    public async Task RegisterAsync_ShouldReturnValidationError_WhenRequiredFieldsBlank(
        string username, string email, string password)
    {
        var service = CreateService();

        var result = await service.RegisterAsync(new CreateUserDto(username, email, password));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task RegisterAsync_ShouldReturnError_WhenUsernameTooShort()
    {
        var service = CreateService();
        _userRepoMock.Setup(r => r.ExistsAsync("ab", "test@test.com", default)).ReturnsAsync(false);
        _userRepoMock.Setup(r => r.AddAsync(It.IsAny<User>(), default))
            .ReturnsAsync((User u, CancellationToken _) => u);

        // Username < 3 chars should be rejected by the User entity constructor
        var result = await service.RegisterAsync(new CreateUserDto("ab", "test@test.com", "password123"));

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_ShouldReturnError_WhenUsernameTooLong()
    {
        var service = CreateService();
        var longUsername = new string('a', 51);
        _userRepoMock.Setup(r => r.ExistsAsync(longUsername, "test@test.com", default)).ReturnsAsync(false);
        _userRepoMock.Setup(r => r.AddAsync(It.IsAny<User>(), default))
            .ReturnsAsync((User u, CancellationToken _) => u);

        var result = await service.RegisterAsync(new CreateUserDto(longUsername, "test@test.com", "password123"));

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_ShouldReturnError_WhenEmailInvalid()
    {
        var service = CreateService();
        _userRepoMock.Setup(r => r.ExistsAsync("validuser", "not-an-email", default)).ReturnsAsync(false);
        _userRepoMock.Setup(r => r.AddAsync(It.IsAny<User>(), default))
            .ReturnsAsync((User u, CancellationToken _) => u);

        var result = await service.RegisterAsync(new CreateUserDto("validuser", "not-an-email", "password123"));

        result.IsSuccess.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────
    // Token validation edge cases
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateTokenAsync_ShouldRejectMalformedToken()
    {
        var service = CreateService();

        var result = await service.ValidateTokenAsync("not.a.jwt.token");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task ValidateTokenAsync_ShouldRejectTokenSignedWithWrongKey()
    {
        // Create a valid-looking token but signed with a different key
        var wrongKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("DifferentSecretKeyThatIsLongEnoughForHmac256!"));
        var credentials = new SigningCredentials(wrongKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: DefaultJwtSettings.Issuer,
            audience: DefaultJwtSettings.Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        var service = CreateService();

        var result = await service.ValidateTokenAsync(tokenString);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task ValidateTokenAsync_ShouldRejectExpiredToken()
    {
        // Create a token that expired in the past
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DefaultJwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: DefaultJwtSettings.Issuer,
            audience: DefaultJwtSettings.Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        var service = CreateService();

        var result = await service.ValidateTokenAsync(tokenString);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task ValidateTokenAsync_ShouldRejectTokenWithFutureNbf()
    {
        // Token not valid yet (notBefore in the future)
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DefaultJwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: DefaultJwtSettings.Issuer,
            audience: DefaultJwtSettings.Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            notBefore: DateTime.UtcNow.AddHours(1),
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        var service = CreateService();

        var result = await service.ValidateTokenAsync(tokenString);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task ValidateTokenAsync_ShouldRejectTokenWithMissingSubClaim()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DefaultJwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // No sub/NameIdentifier claim at all
        var token = new JwtSecurityToken(
            issuer: DefaultJwtSettings.Issuer,
            audience: DefaultJwtSettings.Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("username", "testuser"),
            },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        var service = CreateService();

        var result = await service.ValidateTokenAsync(tokenString);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task ValidateTokenAsync_ShouldRejectTokenWithNonGuidSubClaim()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DefaultJwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: DefaultJwtSettings.Issuer,
            audience: DefaultJwtSettings.Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "not-a-guid"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        var service = CreateService();

        var result = await service.ValidateTokenAsync(tokenString);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task ValidateTokenAsync_ShouldRejectTokenForDeletedUser()
    {
        var password = "password123";
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword(password));
        var service = CreateService();

        _userRepoMock.Setup(r => r.GetByUsernameAsync("testuser", default)).ReturnsAsync(user);

        // Login to get a valid token
        var loginResult = await service.LoginAsync(new LoginDto("testuser", password));
        loginResult.IsSuccess.Should().BeTrue();

        // Simulate user deletion: GetByIdAsync returns null
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync((User?)null);

        var validateResult = await service.ValidateTokenAsync(loginResult.Value.Token);

        validateResult.IsSuccess.Should().BeFalse();
        validateResult.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ValidateTokenAsync_ShouldRejectTokenForInactiveUser()
    {
        var password = "password123";
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword(password));
        var service = CreateService();

        _userRepoMock.Setup(r => r.GetByUsernameAsync("testuser", default)).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var loginResult = await service.LoginAsync(new LoginDto("testuser", password));
        loginResult.IsSuccess.Should().BeTrue();

        // Deactivate user after token was issued
        user.Deactivate();

        var validateResult = await service.ValidateTokenAsync(loginResult.Value.Token);

        validateResult.IsSuccess.Should().BeFalse();
        validateResult.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ValidateTokenAsync_ShouldRejectTokenWithWrongIssuer()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DefaultJwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "WrongIssuer",
            audience: DefaultJwtSettings.Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        var service = CreateService();

        var result = await service.ValidateTokenAsync(tokenString);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task ValidateTokenAsync_ShouldRejectTokenWithWrongAudience()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DefaultJwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: DefaultJwtSettings.Issuer,
            audience: "WrongAudience",
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        var service = CreateService();

        var result = await service.ValidateTokenAsync(tokenString);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────
    // Password change edge cases
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ChangePasswordAsync_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        var service = CreateService();
        _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((User?)null);

        var result = await service.ChangePasswordAsync(Guid.NewGuid(), "old", "new");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldReturnAuthFailed_WhenCurrentPasswordIsWrong()
    {
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("correct"));
        var service = CreateService();
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ChangePasswordAsync(user.Id, "wrong", "newpassword");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.AuthenticationFailed);
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldSucceed_AndUpdateHash()
    {
        var oldPassword = "oldPassword1";
        var newPassword = "newPassword1";
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword(oldPassword));
        var service = CreateService();
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ChangePasswordAsync(user.Id, oldPassword, newPassword);

        result.IsSuccess.Should().BeTrue();
        // The user's password hash should now verify against the new password
        BCrypt.Net.BCrypt.Verify(newPassword, user.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash).Should().BeFalse();
    }

    [Fact]
    public async Task PasswordChange_DoesNotInvalidateExistingTokens()
    {
        // Current behavior: password change does NOT set TokenInvalidatedAt.
        // Existing JWTs remain valid until natural expiry. Document this behavior.
        var oldPassword = "oldPassword1";
        var newPassword = "newPassword1";
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword(oldPassword));
        var service = CreateService();

        _userRepoMock.Setup(r => r.GetByUsernameAsync("testuser", default)).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        // Get a token before password change
        var loginResult = await service.LoginAsync(new LoginDto("testuser", oldPassword));
        loginResult.IsSuccess.Should().BeTrue();

        // Change password
        var changeResult = await service.ChangePasswordAsync(user.Id, oldPassword, newPassword);
        changeResult.IsSuccess.Should().BeTrue();

        // Validate old token — it should still work (TokenInvalidatedAt not set)
        var validateResult = await service.ValidateTokenAsync(loginResult.Value.Token);
        validateResult.IsSuccess.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────
    // External login edge cases
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExternalLoginAsync_ShouldReturnNotFound_WhenLinkedUserAccountDeleted()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        var existingLogin = new ExternalLogin(userId, "GitHub", "12345");

        _externalLoginRepoMock
            .Setup(r => r.GetByProviderAsync("GitHub", "12345", default))
            .ReturnsAsync(existingLogin);

        // User no longer exists in DB
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync((User?)null);

        var dto = new ExternalLoginDto("GitHub", "12345", "octocat", "octocat@github.com");
        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ExternalLoginAsync_ShouldReturnError_WhenAllUsernameVariantsTaken_ShortUsername()
    {
        // Known defect: when > 100 username variants are taken and the original
        // username is short (< 18 chars), the GUID fallback generates a string
        // shorter than 50 characters, causing Substring(0, 50) to throw
        // ArgumentOutOfRangeException. The generic catch returns UnexpectedError.
        // This documents the current behavior for future fix consideration.
        var service = CreateService();

        _externalLoginRepoMock
            .Setup(r => r.GetByProviderAsync("GitHub", "99999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalLogin?)null);

        _userRepoMock.Setup(r => r.GetByEmailAsync("unique@example.com", It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        // Every username variant returns a user (simulating all taken)
        var takenUser = new User("taken", "taken@test.com", BCrypt.Net.BCrypt.HashPassword("p"));
        _userRepoMock.Setup(r => r.GetByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(takenUser);

        _userRepoMock.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) => u);

        _externalLoginRepoMock.Setup(r => r.AddAsync(It.IsAny<ExternalLogin>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalLogin l, CancellationToken _) => l);

        var dto = new ExternalLoginDto("GitHub", "99999", "popular", "unique@example.com");
        var result = await service.ExternalLoginAsync(dto);

        // Current behavior: returns UnexpectedError due to Substring overflow
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
    }

    private AuthenticationService CreateService(JwtSettings? jwtSettings = null)
    {
        return new AuthenticationService(_unitOfWorkMock.Object, jwtSettings ?? DefaultJwtSettings);
    }
}
