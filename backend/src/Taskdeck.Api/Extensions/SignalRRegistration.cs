using Microsoft.AspNetCore.SignalR;

namespace Taskdeck.Api.Extensions;

/// <summary>
/// Registers SignalR services with optional Redis backplane support.
/// When <c>SignalR:Redis:ConnectionString</c> is configured, uses Redis as
/// the backplane for multi-instance message propagation. Otherwise, falls
/// back to the default in-memory transport.
/// </summary>
public static class SignalRRegistration
{
    internal const string RedisConnectionStringKey = "SignalR:Redis:ConnectionString";

    public static ISignalRServerBuilder AddTaskdeckSignalR(
        this IServiceCollection services,
        IConfiguration configuration,
        ILogger logger)
    {
        // #2351 / R4: pin the safe value explicitly. Hub exception text must never
        // reach clients; the framework default is false but nothing pinned it. There is
        // deliberately no configuration key that can turn this on. The pin is asserted on
        // the HubOptions this registration produces; a later global Configure<HubOptions>
        // or a per-hub AddHubOptions<THub> elsewhere would not be caught by that test.
        var signalRBuilder = services.AddSignalR(options => options.EnableDetailedErrors = false);

        var redisConnectionString = configuration[RedisConnectionStringKey];

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            signalRBuilder.AddStackExchangeRedis(redisConnectionString, options =>
            {
                options.Configuration.ChannelPrefix =
                    StackExchange.Redis.RedisChannel.Literal("taskdeck");
            });

            logger.LogInformation(
                "SignalR Redis backplane enabled (channel prefix: taskdeck)");
        }
        else
        {
            logger.LogInformation(
                "SignalR using in-memory transport (no Redis connection string configured)");
        }

        return signalRBuilder;
    }

    /// <summary>
    /// Returns <c>true</c> when a Redis backplane connection string is configured.
    /// </summary>
    public static bool IsRedisBackplaneConfigured(IConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration[RedisConnectionStringKey]);
    }
}
