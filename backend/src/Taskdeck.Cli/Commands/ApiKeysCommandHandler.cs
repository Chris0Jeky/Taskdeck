using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Cli.Commands;

internal sealed class ApiKeysCommandHandler
{
    private readonly ApiKeyService _apiKeyService;
    private readonly IUnitOfWork _unitOfWork;

    public ApiKeysCommandHandler(ApiKeyService apiKeyService, IUnitOfWork unitOfWork)
    {
        _apiKeyService = apiKeyService;
        _unitOfWork = unitOfWork;
    }

    public async Task<int> HandleAsync(string command, string[] args)
    {
        return command switch
        {
            "create" => await CreateAsync(args),
            "list" => await ListAsync(args),
            "revoke" => await RevokeAsync(args),
            _ => ConsoleOutput.PrintUsageError(
                $"Unknown api-key command: '{command}'.",
                "taskdeck api-key [create|list|revoke]")
        };
    }

    private async Task<int> CreateAsync(string[] args)
    {
        var name = ArgParser.GetOption(args, "--name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return ConsoleOutput.PrintUsageError(
                "Missing --name.",
                "taskdeck api-key create --name <name> [--expires <days>]");
        }

        var expiresText = ArgParser.GetOption(args, "--expires");
        TimeSpan? expiresIn = null;
        if (expiresText is not null)
        {
            // Support formats: "90d" or "90"
            var daysText = expiresText.TrimEnd('d', 'D');
            if (!int.TryParse(daysText, out var days) || days <= 0)
            {
                return ConsoleOutput.PrintUsageError(
                    $"Invalid --expires value: '{expiresText}'. Provide a positive number of days (e.g., 90 or 90d).",
                    "taskdeck api-key create --name <name> [--expires <days>]");
            }
            expiresIn = TimeSpan.FromDays(days);
        }

        var userId = await GetOrCreateCliActorIdAsync();

        try
        {
            var (plaintextKey, entity) = await _apiKeyService.CreateKeyAsync(userId, name, expiresIn);

            ConsoleOutput.WriteJson(new
            {
                id = entity.Id,
                key = plaintextKey,
                keyPrefix = entity.KeyPrefix_,
                name = entity.Name,
                createdAt = entity.CreatedAt,
                expiresAt = entity.ExpiresAt,
                message = "Save this key — it cannot be retrieved again."
            });

            return ExitCodes.Success;
        }
        catch (DomainException ex)
        {
            return ConsoleOutput.PrintFailure(ex.ErrorCode, ex.Message);
        }
    }

    private async Task<int> ListAsync(string[] args)
    {
        var userId = await GetOrCreateCliActorIdAsync();
        var keys = await _apiKeyService.ListKeysAsync(userId);

        var items = keys.Select(k => new
        {
            id = k.Id,
            keyPrefix = k.KeyPrefix_,
            name = k.Name,
            createdAt = k.CreatedAt,
            expiresAt = k.ExpiresAt,
            revokedAt = k.RevokedAt,
            lastUsedAt = k.LastUsedAt,
            isActive = k.IsActive
        });

        ConsoleOutput.WriteJson(items);
        return ExitCodes.Success;
    }

    private async Task<int> RevokeAsync(string[] args)
    {
        var name = ArgParser.GetOption(args, "--name");
        var idText = ArgParser.GetOption(args, "--id");

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(idText))
        {
            return ConsoleOutput.PrintUsageError(
                "Provide --name or --id to identify the key to revoke.",
                "taskdeck api-key revoke --name <name> | --id <key-id>");
        }

        var userId = await GetOrCreateCliActorIdAsync();

        try
        {
            if (!string.IsNullOrWhiteSpace(idText))
            {
                if (!Guid.TryParse(idText, out var keyId))
                {
                    return ConsoleOutput.PrintUsageError(
                        $"Invalid --id value: '{idText}'.",
                        "taskdeck api-key revoke --id <key-id>");
                }

                await _apiKeyService.RevokeKeyAsync(keyId, userId);
                ConsoleOutput.WriteJson(new { revoked = keyId, status = "ok" });
                return ExitCodes.Success;
            }

            // Revoke by name: find active keys with this name
            var keys = await _apiKeyService.ListKeysAsync(userId);
            var matches = keys.Where(k => k.Name == name && k.IsActive).ToList();
            if (matches.Count == 0)
            {
                return ConsoleOutput.PrintFailure(
                    ErrorCodes.NotFound,
                    $"No active API key found with name '{name}'.");
            }

            if (matches.Count > 1)
            {
                // Ambiguous: multiple active keys share this name — require --id.
                Console.Error.WriteLine($"Multiple active keys found with name '{name}'. Use --id to specify which key to revoke:");
                foreach (var match in matches)
                {
                    Console.Error.WriteLine($"  --id {match.Id}  (prefix: {match.KeyPrefix_}, created: {match.CreatedAt:u})");
                }
                return ExitCodes.Failure;
            }

            var target = matches[0];
            await _apiKeyService.RevokeKeyAsync(target.Id, userId);
            ConsoleOutput.WriteJson(new { revoked = target.Id, name = target.Name, status = "ok" });
            return ExitCodes.Success;
        }
        catch (DomainException ex)
        {
            return ConsoleOutput.PrintFailure(ex.ErrorCode, ex.Message);
        }
    }

    /// <summary>
    /// Returns the CLI system actor's user ID, creating the actor if it does not exist.
    /// Looks up by email (not username) to prevent identity hijacking: the
    /// <c>@system.taskdeck</c> domain is non-routable and cannot be registered
    /// through the normal authentication flow, which checks email uniqueness.
    /// </summary>
    private async Task<Guid> GetOrCreateCliActorIdAsync()
    {
        var existingActor = await _unitOfWork.Users.GetByEmailAsync(CliActorIdentity.ActorEmail);
        if (existingActor is not null)
        {
            return existingActor.Id;
        }

        var actor = new User(
            CliActorIdentity.ActorUsername,
            CliActorIdentity.ActorEmail,
            Guid.NewGuid().ToString("N"));
        await _unitOfWork.Users.AddAsync(actor);
        await _unitOfWork.SaveChangesAsync();
        return actor.Id;
    }
}
