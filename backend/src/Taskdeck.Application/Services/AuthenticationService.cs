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

            var users = await ResolveLoginCandidatesAsync(dto.UsernameOrEmail);
            if (users.Count == 0)
                return Result.Failure<AuthResultDto>(ErrorCodes.AuthenticationFailed, "Invalid username/email or password");

            User? authenticatedUser = null;
            foreach (var candidate in users)
            {
                if (!BCrypt.Net.BCrypt.Verify(dto.Password, candidate.PasswordHash))
                    continue;

                if (!candidate.IsActive)
                    return Result.Failure<AuthResultDto>(ErrorCodes.Forbidden, "User account is inactive");

                authenticatedUser = candidate;
                break;
            }

            if (authenticatedUser == null)
                return Result.Failure<AuthResultDto>(ErrorCodes.AuthenticationFailed, "Invalid username/email or password");

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

            var exists = await _unitOfWork.Users.ExistsAsync(dto.Username, dto.Email);
            if (exists)
                return Result.Failure<AuthResultDto>(ErrorCodes.Conflict, "A user with that username or email already exists");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            var user = new User(dto.Username, dto.Email, passwordHash, dto.DefaultRole);

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

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("username", user.Username),
            new Claim("email", user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
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
