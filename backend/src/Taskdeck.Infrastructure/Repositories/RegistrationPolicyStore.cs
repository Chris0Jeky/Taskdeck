using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class RegistrationPolicyStore : IRegistrationPolicyStore
{
    private readonly TaskdeckDbContext _context;

    public RegistrationPolicyStore(TaskdeckDbContext context)
    {
        _context = context;
    }

    public Task<bool> IsFirstUserBootstrapClaimedAsync(
        CancellationToken cancellationToken = default)
    {
        return _context.RegistrationBootstraps
            .AsNoTracking()
            .AnyAsync(bootstrap => bootstrap.Id == RegistrationBootstrap.SingletonId, cancellationToken);
    }

    public async Task<bool> IsInviteAvailableAsync(
        string codeHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var invite = await _context.RegistrationInvites
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.CodeHash == codeHash, cancellationToken);

        return invite is { ConsumedAt: null } && invite.ExpiresAt > now;
    }

    public async Task<bool> TryClaimFirstUserBootstrapAsync(
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken = default)
    {
        var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT OR IGNORE INTO "RegistrationBootstraps" ("Id", "ClaimedAt")
            VALUES ({RegistrationBootstrap.SingletonId}, {claimedAt});
            """,
            cancellationToken);

        return affected == 1;
    }

    public async Task<bool> TryConsumeInviteAsync(
        string codeHash,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken = default)
    {
        var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "RegistrationInvites"
            SET "ConsumedAt" = {consumedAt}, "UpdatedAt" = {consumedAt}
            WHERE "CodeHash" = {codeHash}
              AND "ConsumedAt" IS NULL
              AND "ExpiresAt" > {consumedAt};
            """,
            cancellationToken);

        return affected == 1;
    }

    public async Task AddInviteAsync(
        RegistrationInvite invite,
        CancellationToken cancellationToken = default)
    {
        await _context.RegistrationInvites.AddAsync(invite, cancellationToken);
    }
}
