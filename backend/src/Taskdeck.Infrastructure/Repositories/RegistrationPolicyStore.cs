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
