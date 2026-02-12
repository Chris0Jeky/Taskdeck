using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class UserService : IUserService
{
    private readonly IUnitOfWork _unitOfWork;

    public UserService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<UserDto>> CreateUserAsync(CreateUserDto dto)
    {
        try
        {
            var exists = await _unitOfWork.Users.ExistsAsync(dto.Username, dto.Email);
            if (exists)
                return Result.Failure<UserDto>(ErrorCodes.Conflict, "A user with the same username or email already exists");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            var user = new User(dto.Username, dto.Email, passwordHash, dto.DefaultRole);

            await _unitOfWork.Users.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

            return Result.Success(MapToDto(user));
        }
        catch (DomainException ex)
        {
            return Result.Failure<UserDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<UserDto>> GetUserByIdAsync(Guid userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            return Result.Failure<UserDto>(ErrorCodes.NotFound, $"User with ID {userId} not found");

        return Result.Success(MapToDto(user));
    }

    public async Task<Result<UserDto>> GetUserByUsernameAsync(string username)
    {
        var user = await _unitOfWork.Users.GetByUsernameAsync(username);
        if (user == null)
            return Result.Failure<UserDto>(ErrorCodes.NotFound, $"User with username '{username}' not found");

        return Result.Success(MapToDto(user));
    }

    public async Task<Result<UserDto>> GetUserByEmailAsync(string email)
    {
        var user = await _unitOfWork.Users.GetByEmailAsync(email);
        if (user == null)
            return Result.Failure<UserDto>(ErrorCodes.NotFound, $"User with email '{email}' not found");

        return Result.Success(MapToDto(user));
    }

    public async Task<Result<UserDto>> UpdateUserAsync(Guid userId, UpdateUserDto dto)
    {
        try
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
                return Result.Failure<UserDto>(ErrorCodes.NotFound, $"User with ID {userId} not found");

            if (!string.IsNullOrWhiteSpace(dto.Username))
            {
                var existingByUsername = await _unitOfWork.Users.GetByUsernameAsync(dto.Username);
                if (existingByUsername is not null && existingByUsername.Id != userId)
                {
                    return Result.Failure<UserDto>(
                        ErrorCodes.Conflict,
                        $"Username '{dto.Username}' is already in use");
                }
            }

            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                var existingByEmail = await _unitOfWork.Users.GetByEmailAsync(dto.Email);
                if (existingByEmail is not null && existingByEmail.Id != userId)
                {
                    return Result.Failure<UserDto>(
                        ErrorCodes.Conflict,
                        $"Email '{dto.Email}' is already in use");
                }
            }

            user.UpdateProfile(dto.Username, dto.Email);
            await _unitOfWork.SaveChangesAsync();

            return Result.Success(MapToDto(user));
        }
        catch (DomainException ex)
        {
            return Result.Failure<UserDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result> DeactivateUserAsync(Guid userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            return Result.Failure(ErrorCodes.NotFound, $"User with ID {userId} not found");

        user.Deactivate();
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> ActivateUserAsync(Guid userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            return Result.Failure(ErrorCodes.NotFound, $"User with ID {userId} not found");

        user.Activate();
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<IEnumerable<UserDto>>> ListUsersAsync()
    {
        var users = await _unitOfWork.Users.GetAllAsync();
        return Result.Success(users.Select(MapToDto));
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
            user.UpdatedAt
        );
    }
}
