using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AuthenticationService : IAuthenticationService
{
    private const string InvalidCredentialsMessage = "Invalid username/email or password";

    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtSettings _jwtSettings;
    private readonly IRegistrationPolicyService _registrationPolicy;
    private readonly IPasswordHasher _passwordHasher;

    public AuthenticationService(
        IUnitOfWork unitOfWork,
        JwtSettings jwtSettings,
        IRegistrationPolicyService registrationPolicy,
        IPasswordHasher passwordHasher)
    {
        _unitOfWork = unitOfWork;
        _jwtSettings = jwtSettings;
        _registrationPolicy = registrationPolicy;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<AuthResultDto>> LoginAsync(LoginDto dto)
    {
        try
        {
            if (!TryValidateJwtSettings(out var jwtValidationError))
                return Result.Failure<AuthResultDto>(ErrorCodes.UnexpectedError, jwtValidationError);

            var loginIdentifier = dto.UsernameOrEmail?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(loginIdentifier))
                return Result.Failure<AuthResultDto>(ErrorCodes.ValidationError, "Username or email is required");

            var users = await ResolveLoginCandidatesAsync(loginIdentifier);
            if (users.Count == 0)
                return Result.Failure<AuthResultDto>(ErrorCodes.AuthenticationFailed, InvalidCredentialsMessage);

            User? authenticatedUser = null;
            var hasInactivePasswordMatch = false;
            foreach (var candidate in users)
            {
                if (!_passwordHasher.VerifyPassword(dto.Password, candidate.PasswordHash))
                    continue;

                if (!candidate.IsActive)
                {
                    hasInactivePasswordMatch = true;
                    continue;
                }

                authenticatedUser = candidate;
                break;
            }

            if (authenticatedUser == null)
            {
                if (hasInactivePasswordMatch)
                    return Result.Failure<AuthResultDto>(ErrorCodes.Forbidden, "User account is inactive");

                return Result.Failure<AuthResultDto>(ErrorCodes.AuthenticationFailed, InvalidCredentialsMessage);
            }

            var token = GenerateJwtToken(authenticatedUser);
            return Result.Success(new AuthResultDto(token, MapToDto(authenticatedUser)));
        }
        catch (DomainException ex)
        {
            return Result.Failure<AuthResultDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<AuthResultDto>(ErrorCodes.UnexpectedError, $"Login failed: {ex.Message}");
        }
    }

    public async Task<Result<AuthResultDto>> RegisterAsync(CreateUserDto dto)
    {
        var transactionStarted = false;
        try
        {
            if (!TryValidateJwtSettings(out var jwtValidationError))
                return Result.Failure<AuthResultDto>(ErrorCodes.UnexpectedError, jwtValidationError);

            var normalizedUsername = dto.Username?.Trim() ?? string.Empty;
            var normalizedEmail = dto.Email?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedUsername))
                return Result.Failure<AuthResultDto>(ErrorCodes.ValidationError, "Username is required");

            if (string.IsNullOrWhiteSpace(normalizedEmail))
                return Result.Failure<AuthResultDto>(ErrorCodes.ValidationError, "Email is required");

            // Reject requests that cannot currently satisfy restrictive policy before
            // paying BCrypt's cost. The authoritative claim/consumption is repeated
            // transactionally below so races and duplicate identities still roll back.
            var registrationEligibility = await _registrationPolicy.CheckNewUserEligibilityAsync(dto.InviteCode);
            if (!registrationEligibility.IsSuccess)
            {
                return Result.Failure<AuthResultDto>(
                    registrationEligibility.ErrorCode,
                    registrationEligibility.ErrorMessage);
            }

            var passwordHash = _passwordHasher.HashPassword(dto.Password);

            await _unitOfWork.BeginTransactionAsync();
            transactionStarted = true;

            var registrationAuthorization = await _registrationPolicy.AuthorizeNewUserAsync(dto.InviteCode);
            if (!registrationAuthorization.IsSuccess)
            {
                await _unitOfWork.RollbackTransactionAsync();
                transactionStarted = false;
                return Result.Failure<AuthResultDto>(
                    registrationAuthorization.ErrorCode,
                    registrationAuthorization.ErrorMessage);
            }

            var exists = await _unitOfWork.Users.ExistsAsync(normalizedUsername, normalizedEmail);
            if (exists)
            {
                await _unitOfWork.RollbackTransactionAsync();
                transactionStarted = false;
                return Result.Failure<AuthResultDto>(ErrorCodes.Conflict, "An account with that username or email already exists. Sign in with your existing credentials.");
            }

            var user = new User(normalizedUsername, normalizedEmail, passwordHash, dto.DefaultRole);

            await _unitOfWork.Users.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();
            transactionStarted = false;

            var token = GenerateJwtToken(user);
            return Result.Success(new AuthResultDto(token, MapToDto(user)));
        }
        catch (DomainException ex)
        {
            if (transactionStarted)
                await _unitOfWork.RollbackTransactionAsync();

            return Result.Failure<AuthResultDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            if (transactionStarted)
                await _unitOfWork.RollbackTransactionAsync();

            return Result.Failure<AuthResultDto>(ErrorCodes.UnexpectedError, $"Registration failed: {ex.Message}");
        }
    }

    public async Task<Result<AuthResultDto>> ExternalLoginAsync(ExternalLoginDto dto)
    {
        var transactionStarted = false;
        try
        {
            if (!TryValidateJwtSettings(out var jwtValidationError))
                return Result.Failure<AuthResultDto>(ErrorCodes.UnexpectedError, jwtValidationError);

            if (string.IsNullOrWhiteSpace(dto.Provider))
                return Result.Failure<AuthResultDto>(ErrorCodes.ValidationError, "Provider is required");

            if (string.IsNullOrWhiteSpace(dto.ProviderUserId))
                return Result.Failure<AuthResultDto>(ErrorCodes.ValidationError, "Provider user ID is required");

            if (string.IsNullOrWhiteSpace(dto.Email))
                return Result.Failure<AuthResultDto>(ErrorCodes.ValidationError, "Email is required");

            if (string.IsNullOrWhiteSpace(dto.Username))
                return Result.Failure<AuthResultDto>(ErrorCodes.ValidationError, "Username is required");

            // Check if an external login already exists for this provider+userId
            var existingLogin = await _unitOfWork.ExternalLogins.GetByProviderAsync(dto.Provider, dto.ProviderUserId);
            if (existingLogin != null)
            {
                // Existing linked account — update profile and issue token
                var existingUser = await _unitOfWork.Users.GetByIdAsync(existingLogin.UserId);
                if (existingUser == null)
                    return Result.Failure<AuthResultDto>(ErrorCodes.NotFound, "Linked user account not found");

                if (!existingUser.IsActive)
                    return Result.Failure<AuthResultDto>(ErrorCodes.Forbidden, "User account is inactive");

                existingLogin.UpdateProfile(dto.DisplayName, dto.AvatarUrl);
                await _unitOfWork.SaveChangesAsync();

                var token = GenerateJwtToken(existingUser);
                return Result.Success(new AuthResultDto(token, MapToDto(existingUser)));
            }

            // Security: Do NOT auto-link by email. An attacker could create a GitHub
            // account with the same email as an existing Taskdeck user and take over
            // their account, because GitHub does not guarantee email verification.
            // Instead, always create a new account for unlinked external logins.

            // Apply the same cheap policy boundary to new external accounts. Existing
            // linked accounts return above and remain unaffected by registration mode.
            var registrationEligibility = await _registrationPolicy.CheckNewUserEligibilityAsync(dto.InviteCode);
            if (!registrationEligibility.IsSuccess)
            {
                return Result.Failure<AuthResultDto>(
                    registrationEligibility.ErrorCode,
                    registrationEligibility.ErrorMessage);
            }

            var randomPassword = _passwordHasher.HashPassword(Guid.NewGuid().ToString());

            await _unitOfWork.BeginTransactionAsync();
            transactionStarted = true;

            var registrationAuthorization = await _registrationPolicy.AuthorizeNewUserAsync(dto.InviteCode);
            if (!registrationAuthorization.IsSuccess)
            {
                await _unitOfWork.RollbackTransactionAsync();
                transactionStarted = false;
                return Result.Failure<AuthResultDto>(
                    registrationAuthorization.ErrorCode,
                    registrationAuthorization.ErrorMessage);
            }

            var normalizedEmail = dto.Email.Trim();
            var normalizedUsername = dto.Username.Trim();

            // If an account with this email already exists, generate a unique email
            // to avoid conflicts. The user can link accounts manually later.
            var candidateEmail = normalizedEmail;
            if (await _unitOfWork.Users.GetByEmailAsync(candidateEmail) != null)
            {
                candidateEmail = $"{dto.Provider.ToLowerInvariant()}-{dto.ProviderUserId}@external.taskdeck.local";
            }

            // Ensure username uniqueness — append suffix if needed (capped to prevent DoS)
            var candidateUsername = normalizedUsername;
            var suffix = 0;
            const int maxUsernameSuffixAttempts = 100;
            while (await _unitOfWork.Users.GetByUsernameAsync(candidateUsername) != null)
            {
                suffix++;
                if (suffix > maxUsernameSuffixAttempts)
                {
                    var guidFallback = $"{normalizedUsername}-{Guid.NewGuid():N}";
                    candidateUsername = guidFallback[..Math.Min(50, guidFallback.Length)];
                    break;
                }
                candidateUsername = $"{normalizedUsername}{suffix}";
            }

            var newUser = new User(candidateUsername, candidateEmail, randomPassword);

            await _unitOfWork.Users.AddAsync(newUser);

            var newExternalLogin = new ExternalLogin(newUser.Id, dto.Provider, dto.ProviderUserId, dto.DisplayName, dto.AvatarUrl);
            await _unitOfWork.ExternalLogins.AddAsync(newExternalLogin);
            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();
            transactionStarted = false;

            var newToken = GenerateJwtToken(newUser);
            return Result.Success(new AuthResultDto(newToken, MapToDto(newUser)));
        }
        catch (DomainException ex)
        {
            if (transactionStarted)
                await _unitOfWork.RollbackTransactionAsync();

            return Result.Failure<AuthResultDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception)
        {
            if (transactionStarted)
                await _unitOfWork.RollbackTransactionAsync();

            // Do not expose internal details in error messages
            return Result.Failure<AuthResultDto>(ErrorCodes.UnexpectedError, "External login failed due to an unexpected error");
        }
    }

    public async Task<Result<LinkedAccountDto>> CompleteAccountLinkAsync(Guid userId, string provider, string providerUserId, string? displayName, string? avatarUrl)
    {
        try
        {
            if (userId == Guid.Empty)
                return Result.Failure<LinkedAccountDto>(ErrorCodes.ValidationError, "User ID is required");

            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
                return Result.Failure<LinkedAccountDto>(ErrorCodes.NotFound, "User not found");

            if (!user.IsActive)
                return Result.Failure<LinkedAccountDto>(ErrorCodes.Forbidden, "User account is inactive");

            // Check if this provider+userId combo is already linked to a different user
            var existingLogin = await _unitOfWork.ExternalLogins.GetByProviderAsync(provider, providerUserId);
            if (existingLogin != null)
            {
                if (existingLogin.UserId == userId)
                    return Result.Failure<LinkedAccountDto>(ErrorCodes.Conflict, $"This {provider} account is already linked to your account");

                return Result.Failure<LinkedAccountDto>(ErrorCodes.Conflict, $"This {provider} account is already linked to a different user");
            }

            // Check if user already has a linked account for this provider
            var userLogins = await _unitOfWork.ExternalLogins.GetByUserIdAsync(userId);
            if (userLogins.Any(l => l.Provider == provider))
                return Result.Failure<LinkedAccountDto>(ErrorCodes.Conflict, $"Your account is already linked to a {provider} account");

            var newLogin = new ExternalLogin(userId, provider, providerUserId, displayName, avatarUrl);
            await _unitOfWork.ExternalLogins.AddAsync(newLogin);
            await _unitOfWork.SaveChangesAsync();

            return Result.Success(new LinkedAccountDto(
                newLogin.Provider,
                newLogin.ProviderUserId,
                newLogin.ProviderDisplayName,
                newLogin.AvatarUrl,
                newLogin.CreatedAt));
        }
        catch (DomainException ex)
        {
            return Result.Failure<LinkedAccountDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception)
        {
            return Result.Failure<LinkedAccountDto>(ErrorCodes.UnexpectedError, "Account linking failed due to an unexpected error");
        }
    }

    public async Task<Result> UnlinkExternalLoginAsync(Guid userId, string provider)
    {
        try
        {
            if (userId == Guid.Empty)
                return Result.Failure(ErrorCodes.ValidationError, "User ID is required");

            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
                return Result.Failure(ErrorCodes.NotFound, "User not found");

            var logins = await _unitOfWork.ExternalLogins.GetByUserIdAsync(userId);
            var loginToRemove = logins.FirstOrDefault(l => l.Provider == provider);
            if (loginToRemove == null)
                return Result.Failure(ErrorCodes.NotFound, $"No {provider} account is linked");

            await _unitOfWork.ExternalLogins.DeleteAsync(loginToRemove);
            await _unitOfWork.SaveChangesAsync();

            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<AuthResultDto>> RefreshTokenAsync(Guid userId)
    {
        try
        {
            if (!TryValidateJwtSettings(out var jwtValidationError))
                return Result.Failure<AuthResultDto>(ErrorCodes.UnexpectedError, jwtValidationError);

            if (userId == Guid.Empty)
                return Result.Failure<AuthResultDto>(ErrorCodes.ValidationError, "User ID is required");

            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
                return Result.Failure<AuthResultDto>(ErrorCodes.Unauthorized, "User not found");

            if (!user.IsActive)
                return Result.Failure<AuthResultDto>(ErrorCodes.Forbidden, "User account is inactive");

            var token = GenerateJwtToken(user);
            return Result.Success(new AuthResultDto(token, MapToDto(user)));
        }
        catch (DomainException ex)
        {
            return Result.Failure<AuthResultDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception)
        {
            // Do not expose internal details in error messages
            return Result.Failure<AuthResultDto>(ErrorCodes.UnexpectedError, "Token refresh failed due to an unexpected error");
        }
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        try
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
                return Result.Failure(ErrorCodes.NotFound, $"User with ID {userId} not found");

            if (!user.IsActive)
                return Result.Failure(ErrorCodes.Forbidden, "User account is inactive");

            if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
                return Result.Failure(ErrorCodes.AuthenticationFailed, "Current password is incorrect");

            var newHash = _passwordHasher.HashPassword(newPassword);
            user.UpdatePassword(newHash);

            await _unitOfWork.SaveChangesAsync();
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<UserDto>> ValidateTokenAsync(string token)
    {
        try
        {
            if (!TryValidateJwtSettings(out var jwtValidationError))
                return Result.Failure<UserDto>(ErrorCodes.UnexpectedError, jwtValidationError);

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_jwtSettings.SecretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var result = await tokenHandler.ValidateTokenAsync(token, validationParameters);
            if (!result.IsValid)
                return Result.Failure<UserDto>(ErrorCodes.Unauthorized, "Invalid or expired token");

            var userIdClaim = result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Sub)
                              ?? result.ClaimsIdentity.FindFirst(ClaimTypes.NameIdentifier);

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Result.Failure<UserDto>(ErrorCodes.Unauthorized, "Invalid token claims");

            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
                return Result.Failure<UserDto>(ErrorCodes.NotFound, $"User with ID {userId} not found");

            if (!user.IsActive)
                return Result.Failure<UserDto>(ErrorCodes.Forbidden, "User account is inactive");

            return Result.Success(MapToDto(user));
        }
        catch (SecurityTokenException)
        {
            return Result.Failure<UserDto>(ErrorCodes.Unauthorized, "Invalid or expired token");
        }
        catch (DomainException ex)
        {
            return Result.Failure<UserDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<UserDto>(ErrorCodes.UnexpectedError, $"Token validation failed: {ex.Message}");
        }
    }

    public string GenerateJwtToken(User user)
    {
        if (!TryValidateJwtSettings(out var jwtValidationError))
            throw new DomainException(ErrorCodes.UnexpectedError, jwtValidationError);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("username", user.Username),
            new Claim("email", user.Email),
            new Claim(ClaimTypes.Role, user.DefaultRole.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private bool TryValidateJwtSettings(out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(_jwtSettings.SecretKey))
        {
            errorMessage = "JWT configuration is missing a SecretKey";
            return false;
        }

        if (_jwtSettings.SecretKey.Length < JwtSettings.MinSecretKeyLength)
        {
            errorMessage = $"JWT SecretKey must be at least {JwtSettings.MinSecretKeyLength} characters";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_jwtSettings.Issuer))
        {
            errorMessage = "JWT configuration is missing an Issuer";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_jwtSettings.Audience))
        {
            errorMessage = "JWT configuration is missing an Audience";
            return false;
        }

        if (_jwtSettings.ExpirationMinutes <= 0)
        {
            errorMessage = "JWT ExpirationMinutes must be greater than 0";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private async Task<List<User>> ResolveLoginCandidatesAsync(string usernameOrEmail)
    {
        var userByUsername = await _unitOfWork.Users.GetByUsernameAsync(usernameOrEmail);
        var userByEmail = await _unitOfWork.Users.GetByEmailAsync(usernameOrEmail);

        var orderedCandidates = usernameOrEmail.Contains('@')
            ? new[] { userByEmail, userByUsername }
            : new[] { userByUsername, userByEmail };

        var unique = new List<User>();
        foreach (var candidate in orderedCandidates)
        {
            if (candidate is null || unique.Any(u => u.Id == candidate.Id))
                continue;

            unique.Add(candidate);
        }

        return unique;
    }

    private static UserDto MapToDto(User user)
    {
        return new UserDto(
            user.Id,
            user.Username,
            user.Email,
            user.DefaultRole,
            user.IsActive,
            user.CreatedAt,
            user.UpdatedAt);
    }
}
