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

    public AuthenticationService(IUnitOfWork unitOfWork, JwtSettings jwtSettings)
    {
        _unitOfWork = unitOfWork;
        _jwtSettings = jwtSettings;
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
                if (!BCrypt.Net.BCrypt.Verify(dto.Password, candidate.PasswordHash))
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

            var exists = await _unitOfWork.Users.ExistsAsync(normalizedUsername, normalizedEmail);
            if (exists)
                return Result.Failure<AuthResultDto>(ErrorCodes.Conflict, "An account with that username or email already exists. Sign in with your existing credentials.");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            var user = new User(normalizedUsername, normalizedEmail, passwordHash, dto.DefaultRole);

            await _unitOfWork.Users.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

            var token = GenerateJwtToken(user);
            return Result.Success(new AuthResultDto(token, MapToDto(user)));
        }
        catch (DomainException ex)
        {
            return Result.Failure<AuthResultDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<AuthResultDto>(ErrorCodes.UnexpectedError, $"Registration failed: {ex.Message}");
        }
    }

    public async Task<Result<AuthResultDto>> ExternalLoginAsync(ExternalLoginDto dto)
    {
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

            // New user — create account with a random unusable password hash
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

            var randomPassword = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());
            var newUser = new User(candidateUsername, candidateEmail, randomPassword);

            await _unitOfWork.Users.AddAsync(newUser);

            var newExternalLogin = new ExternalLogin(newUser.Id, dto.Provider, dto.ProviderUserId, dto.DisplayName, dto.AvatarUrl);
            await _unitOfWork.ExternalLogins.AddAsync(newExternalLogin);
            await _unitOfWork.SaveChangesAsync();

            var newToken = GenerateJwtToken(newUser);
            return Result.Success(new AuthResultDto(newToken, MapToDto(newUser)));
        }
        catch (DomainException ex)
        {
            return Result.Failure<AuthResultDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception)
        {
            // Do not expose internal details in error messages
            return Result.Failure<AuthResultDto>(ErrorCodes.UnexpectedError, "External login failed due to an unexpected error");
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

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
                return Result.Failure(ErrorCodes.AuthenticationFailed, "Current password is incorrect");

            var newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
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

    private string GenerateJwtToken(User user)
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

        if (_jwtSettings.SecretKey.Length < 32)
        {
            errorMessage = "JWT SecretKey must be at least 32 characters";
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
