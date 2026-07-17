using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Mcp;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Api.Middleware;

/// <summary>
/// Middleware that authenticates MCP HTTP requests using API keys.
/// Extracts a Bearer token from the Authorization header, hashes it with SHA-256,
/// looks up the hash in the ApiKeys table, and sets the user ID in
/// HttpContext.Items for <see cref="HttpUserContextProvider"/>.
///
/// Only active on the MCP endpoint path (/mcp). REST API endpoints continue
/// to use JWT authentication.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    /// <summary>
    /// Authentication type stamped on the ClaimsIdentity for a valid API key so downstream
    /// middleware (e.g. TokenValidationMiddleware) can distinguish API-key principals from JWT ones.
    /// </summary>
    public const string AuthenticationType = "ApiKey";

    /// <summary>
    /// Context item containing the validated API key ID for per-key rate-limit partitioning.
    /// The opaque database ID is used instead of the raw key or user-visible prefix.
    /// </summary>
    public const string ApiKeyIdItemKey = "McpApiKeyId";

    /// <summary>
    /// Context item set when API-key authentication rejects the request, BEFORE the 401 body is
    /// written. <see cref="McpAuthenticationRateLimitingMiddleware"/> consumes the pre-auth IP
    /// failure budget on this marker in a finally block, so a client that aborts the connection
    /// mid-response (making the write throw) cannot evade the failure charge — the key
    /// parse/lookup work has already been spent by that point.
    /// </summary>
    public const string AuthenticationFailedItemKey = "McpAuthenticationFailed";

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, TaskdeckDbContext dbContext)
    {
        // Only authenticate MCP endpoint requests
        if (!context.Request.Path.StartsWithSegments(McpEndpointMapping.HttpRoute, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                "Missing Authorization header. Provide a Bearer token with your API key.");
            return;
        }

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                "Invalid Authorization header format. Use: Bearer tdsk_...");
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();

        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(ApiKey.KeyPrefix))
        {
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                "Invalid API key format. Keys must start with 'tdsk_'.");
            return;
        }

        // Hash the provided key and look up in the database. The owner's active state is resolved in
        // THIS single ApiKeys query (a correlated projection on the UserId FK), NOT in a separate Users
        // SELECT afterwards. That fold is the #1404 fix: a stale-owner key — an active key row whose
        // owning user was deactivated or hard-deleted — must be rejected with 401 (charging the pre-auth
        // IP failure budget) BEFORE the per-key quota charge below, so sustained traffic eventually
        // trips the pre-auth IP pre-check before any DB work. It also removes the standalone Users
        // lookup from the happy path entirely (one query, not two). Only scalar columns are projected;
        // key/owner active state is still computed in memory (below) so expiry is evaluated against the
        // same wall clock as before, not pushed into SQL.
        var keyHash = HashKey(token);

        var authRecord = await dbContext.ApiKeys
            .AsNoTracking()
            .Where(k => k.KeyHash == keyHash)
            .Select(k => new ApiKeyAuthProjection
            {
                Id = k.Id,
                UserId = k.UserId,
                ExpiresAt = k.ExpiresAt,
                RevokedAt = k.RevokedAt,
                // Null when the owner row is absent (hard-deleted user); the flag value when present. A
                // stale-owner key therefore fails the `OwnerIsActive == true` gate in either case.
                OwnerIsActive = dbContext.Users
                    .Where(u => u.Id == k.UserId)
                    .Select(u => (bool?)u.IsActive)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(context.RequestAborted);

        if (authRecord is null)
        {
            _logger.LogWarning("MCP API key authentication failed: key not found (prefix: {Prefix})",
                token.Length >= 8 ? token[..8] : "short");
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                "Invalid API key.");
            return;
        }

        // Key state (revoked/expired) computed in memory from the projected timestamps — identical to
        // the entity's IsActive property.
        var keyIsActive = authRecord.RevokedAt is null
            && (authRecord.ExpiresAt is null || authRecord.ExpiresAt > DateTimeOffset.UtcNow);
        if (!keyIsActive)
        {
            var reason = authRecord.RevokedAt is not null ? "revoked" : "expired";
            _logger.LogWarning("MCP API key authentication failed: key is {Reason} (id: {KeyId})",
                reason, authRecord.Id);
            // Return generic message to avoid leaking key state (revoked vs expired)
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                "Invalid API key.");
            return;
        }

        // Stale-owner key: an active key row whose owning user is deactivated (IsActive=false) or
        // hard-deleted (owner row absent → OwnerIsActive is null). Rejected here, BEFORE the per-key
        // budget charge, precisely so WriteErrorResponse charges the pre-auth IP failure budget on every
        // attempt. Charging the per-key budget first (the #1401 order that VALID keys still get, below)
        // would hide this behind a 429 that never spends the IP budget, so the SHA-256 + ApiKeys lookup
        // would keep running indefinitely (#1404). This is the SAME budget a genuinely invalid key
        // charges, so sustained stale-owner traffic exhausts the IP bucket and trips the pre-auth
        // pre-check before any DB work — restoring pre-#1401 behaviour for stale-owner keys.
        if (authRecord.OwnerIsActive != true)
        {
            _logger.LogWarning("MCP API key authentication failed: user inactive (userId: {UserId})", authRecord.UserId);
            await WriteErrorResponse(context, StatusCodes.Status401Unauthorized,
                "User account is inactive or has been deleted.");
            return;
        }

        // Enforce the per-key request budget (McpPerApiKey) at the EARLIEST point the key identity is
        // confirmed usable — right after the key row is resolved and BOTH the key and its owner are
        // confirmed active, and BEFORE the last-used write below (#1384). The owner check is folded into
        // the initial lookup above, so there is no separate user-account query to shield; the per-key
        // budget remains the FIRST gate a genuinely valid key meets (the #1401 ordering, unchanged). The
        // opaque key ID is the budget partition, so store it first. This is the SINGLE charge point for
        // the per-key budget: the /mcp endpoint no longer carries an endpoint-stage McpPerApiKey policy,
        // so a request passing this check is never charged again downstream. A valid-but-over-quota key
        // is rejected with 429 here without setting AuthenticationFailedItemKey and without a 401, so the
        // pre-auth IP FAILURE budget is not spent — valid keys never touch that budget (#1368/#1381),
        // only failed authentications do.
        //
        // Resolved optionally: the limiter is registered only when rate limiting is enabled, so when it
        // is absent the check is skipped (no MCP throttling when disabled), matching prior behaviour.
        // Deliberately NO null-conditional on RequestServices: ASP.NET Core populates the scoped
        // provider before application middleware runs, and a ?. here would add a second SILENT skip
        // path for a security charge (fail-open). The only intended skip is GetService returning null
        // when rate limiting is disabled; a genuinely missing provider should throw loudly.
        context.Items[ApiKeyIdItemKey] = authRecord.Id;

        var perKeyLimiter = context.RequestServices.GetService<McpPerApiKeyRateLimiter>();
        if (perKeyLimiter is not null)
        {
            using var perKeyLease = perKeyLimiter.AttemptAcquire(context);
            if (!perKeyLease.IsAcquired)
            {
                _logger.LogDebug("MCP per-key rate limit exceeded for key {KeyId}", authRecord.Id);
                await McpPerApiKeyRateLimiter.WriteRejectedAsync(context, perKeyLease, context.RequestAborted);
                return;
            }
        }

        // Set the authenticated user ID for HttpUserContextProvider. The API key ID item was set
        // above, before the per-key budget check.
        context.Items[HttpUserContextProvider.UserIdItemKey] = authRecord.UserId;

        // Establish an authenticated principal so the global authorization FallbackPolicy
        // (RequireAuthenticatedUser, #1132 AC4) is satisfied for valid-key MCP requests — a valid
        // API key IS authentication. It carries only the user id (no JWT 'iat', so
        // TokenValidationMiddleware's JWT-invalidation check is a no-op for it, and that middleware
        // short-circuits on this AuthenticationType). Set before UseAuthentication runs: JwtBearer
        // reads the "Bearer tdsk_..." header and fails to validate it as a JWT, returning a null
        // Principal that AuthenticationMiddleware does not assign over context.User, so this survives.
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, authRecord.UserId.ToString()) },
            authenticationType: AuthenticationType));

        // Update last-used timestamp before continuing the pipeline. This is a non-critical usage
        // stamp: a failure here must NOT fail an otherwise-valid authentication, so it is caught and
        // logged inside UpdateLastUsedAsync (never rethrown) — the request proceeds regardless.
        await UpdateLastUsedAsync(dbContext, authRecord.Id);

        await _next(context);
    }

    private static string HashKey(string plaintextKey)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintextKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task UpdateLastUsedAsync(TaskdeckDbContext dbContext, Guid keyId)
    {
        // Hoist the timestamps into locals BEFORE building the ExecuteUpdate. Passing
        // DateTimeOffset.UtcNow directly into SetProperty makes EF Core treat it as a value
        // expression it must translate to SQL; the SQLite provider cannot map UtcNow and refuses the
        // whole statement ("The following lambda argument to 'SetProperty' does not represent a valid
        // value: 'DateTimeOffset.UtcNow'"), so the previous form silently never wrote LastUsedAt (or
        // UpdatedAt) at all (#1402). A captured local is evaluated client-side and passed as a
        // parameter, which translates cleanly. The LastUsedAt local is typed as the nullable target
        // property so no (DateTimeOffset?)-cast node is introduced either. Both stamps share one
        // instant.
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? lastUsedAt = now;
        try
        {
            // Direct update to avoid concurrency issues with the read-only (AsNoTracking) query above.
            await dbContext.ApiKeys
                .Where(k => k.Id == keyId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(k => k.LastUsedAt, lastUsedAt)
                    .SetProperty(k => k.UpdatedAt, now));
        }
        catch (Exception ex)
        {
            // Non-critical: last-used is a usage stamp, not an auth gate. Swallowing here keeps a
            // failed timestamp write from failing an otherwise-valid authentication, but it is logged
            // at Warning (not silently) so a regression like #1402 — where the write was broken for
            // months — is visible in operational logs instead of hidden.
            _logger.LogWarning(ex, "Failed to update API key last-used timestamp for key {KeyId}", keyId);
        }
    }

    /// <summary>
    /// Projection of the single ApiKeys authentication lookup: the scalar key columns needed to compute
    /// key active-state and identify the key, plus the owner's active flag resolved via a correlated
    /// subquery on the UserId FK (there is no ApiKey→User navigation). <see cref="OwnerIsActive"/> is
    /// null when the owner row is absent (hard-deleted user). Folding the owner check into this query is
    /// the #1404 fix: it removes the separate Users SELECT and lets a stale-owner key be rejected (and
    /// charged to the pre-auth IP failure budget) before the per-key quota charge.
    /// </summary>
    private sealed class ApiKeyAuthProjection
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }
        public DateTimeOffset? RevokedAt { get; init; }
        public bool? OwnerIsActive { get; init; }
    }

    private static async Task WriteErrorResponse(HttpContext context, int statusCode, string message)
    {
        // Mark the authentication failure BEFORE any response write: if the client aborts while
        // the 401 body is being written, WriteAsJsonAsync throws and unwinds past the pre-auth
        // failure-budget middleware — the marker guarantees its finally block still counts the
        // failed attempt (the lookup cost was already paid).
        context.Items[AuthenticationFailedItemKey] = true;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
            ErrorCodes.Unauthorized,
            message));
    }
}
