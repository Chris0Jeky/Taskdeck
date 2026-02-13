using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Infrastructure.Identity;

/// <summary>
/// Implementation of IUserContext that extracts user identity from HTTP context claims.
/// </summary>
public class UserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || user.Identity?.IsAuthenticated != true)
                return null;

            // Try both 'sub' (standard JWT claim) and 'nameid' (alternative)
            return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? user.FindFirst("sub")?.Value;
        }
    }

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;

    public string? Role => _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value;
}
