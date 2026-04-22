using Microsoft.Extensions.Options;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Validation;

/// <summary>
/// Cross-property validation for <see cref="WorkerSettings"/>.
/// Ensures RetryBackoffSeconds has at least as many entries as MaxRetries.
/// </summary>
public sealed class WorkerSettingsValidator : IValidateOptions<WorkerSettings>
{
    public ValidateOptionsResult Validate(string? name, WorkerSettings options)
    {
        var failures = new List<string>();

        if (options.RetryBackoffSeconds is not null
            && options.MaxRetries > 0
            && options.RetryBackoffSeconds.Length < options.MaxRetries)
        {
            failures.Add(
                $"RetryBackoffSeconds has {options.RetryBackoffSeconds.Length} entries but MaxRetries " +
                $"is {options.MaxRetries}. RetryBackoffSeconds.Length must be >= MaxRetries.");
        }

        if (options.RetryBackoffSeconds is not null)
        {
            for (var i = 0; i < options.RetryBackoffSeconds.Length; i++)
            {
                if (options.RetryBackoffSeconds[i] < 0)
                {
                    failures.Add($"RetryBackoffSeconds[{i}] is {options.RetryBackoffSeconds[i]} but must be non-negative.");
                }
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
