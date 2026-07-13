using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Cli.Commands;

internal sealed class InvitesCommandHandler
{
    private const int DefaultExpirationDays = 7;

    private readonly IRegistrationPolicyService _registrationPolicy;

    public InvitesCommandHandler(IRegistrationPolicyService registrationPolicy)
    {
        _registrationPolicy = registrationPolicy;
    }

    public async Task<int> HandleAsync(string command, string[] args)
    {
        return command switch
        {
            "create" => await CreateAsync(args),
            _ => ConsoleOutput.PrintUsageError(
                $"Unknown invite command: '{command}'.",
                "taskdeck invite create [--expires <days>]")
        };
    }

    private async Task<int> CreateAsync(string[] args)
    {
        var expirationDays = DefaultExpirationDays;
        var expiresText = ArgParser.GetOption(args, "--expires");
        if (expiresText is not null)
        {
            var daysText = expiresText.TrimEnd('d', 'D');
            if (!int.TryParse(daysText, out expirationDays)
                || expirationDays is < 1 or > 365)
            {
                return ConsoleOutput.PrintUsageError(
                    $"Invalid --expires value: '{expiresText}'. Provide 1 to 365 days (for example, 7 or 7d).",
                    "taskdeck invite create [--expires <days>]");
            }
        }

        try
        {
            var result = await _registrationPolicy.CreateInviteAsync(TimeSpan.FromDays(expirationDays));
            if (!result.IsSuccess)
                return ConsoleOutput.PrintFailure(result.ErrorCode, result.ErrorMessage);

            ConsoleOutput.WriteJson(new
            {
                id = result.Value.Id,
                code = result.Value.Code,
                displayPrefix = result.Value.DisplayPrefix,
                createdAt = result.Value.CreatedAt,
                expiresAt = result.Value.ExpiresAt,
                message = "Share this invite securely. It can be used once and cannot be retrieved again."
            });

            return ExitCodes.Success;
        }
        catch (DomainException ex)
        {
            return ConsoleOutput.PrintFailure(ex.ErrorCode, ex.Message);
        }
    }
}
