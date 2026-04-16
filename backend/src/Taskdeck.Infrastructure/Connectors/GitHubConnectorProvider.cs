using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Infrastructure.Connectors;

/// <summary>
/// Pilot GitHub connector provider.
/// Context type (read-only): can fetch repo info, issues, PRs.
/// Inbound data routes through the capture pipeline per GP-06.
/// </summary>
public sealed class GitHubConnectorProvider : IConnectorProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubConnectorProvider> _logger;

    public string ProviderId => "github";
    public ConnectorType ConnectorType => ConnectorType.GitHubIssueIntake;
    public ConnectorDirection Direction => ConnectorDirection.Context;

    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);

    public GitHubConnectorProvider(
        HttpClient httpClient,
        ILogger<GitHubConnectorProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ConnectorHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(HealthCheckTimeout);

            // Use the public GitHub API status endpoint — no auth required.
            var response = await _httpClient.GetAsync(
                "https://api.github.com/",
                cts.Token);

            if (response.IsSuccessStatusCode)
            {
                return ConnectorHealthResult.Healthy("GitHub API is reachable.");
            }

            var statusCode = (int)response.StatusCode;
            if (statusCode >= 500)
            {
                return ConnectorHealthResult.Unhealthy(
                    $"GitHub API returned server error: {statusCode}.");
            }

            return ConnectorHealthResult.Degraded(
                $"GitHub API returned non-success status: {statusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Caller cancelled — propagate.
        }
        catch (OperationCanceledException)
        {
            return ConnectorHealthResult.Unhealthy("GitHub API health check timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GitHub API health check failed with network error");
            return ConnectorHealthResult.Unhealthy(
                "GitHub API is unreachable: network error.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during GitHub health check");
            return ConnectorHealthResult.Unhealthy(
                "GitHub API health check failed unexpectedly.");
        }
    }

    public Task<ConnectorCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var capabilities = new ConnectorCapabilities(
            displayName: "GitHub",
            direction: ConnectorDirection.Context,
            supportedEventTypes: new[]
            {
                "issue.opened",
                "issue.closed",
                "pull_request.opened",
                "pull_request.merged",
                "pull_request.closed",
                "push"
            },
            authMethods: new[]
            {
                ConnectorAuthMethod.PersonalAccessToken,
                ConnectorAuthMethod.OAuth2
            },
            rateLimitPerMinute: 60, // Unauthenticated rate limit; 5000 with token.
            description: "Read-only GitHub integration for repository context, issues, and pull requests.");

        return Task.FromResult(capabilities);
    }
}

/// <summary>
/// DTO for GitHub API status response (minimal).
/// </summary>
internal sealed class GitHubApiRootResponse
{
    [JsonPropertyName("current_user_url")]
    public string? CurrentUserUrl { get; set; }
}
