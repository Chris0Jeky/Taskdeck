using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class McpApplicationServiceRegistrationTests
{
    [Fact]
    public void AddMcpApplicationServices_CanConstructProposalRevisionService()
    {
        // #1281: ProposalRevisionService gained an IAutomationPolicyEngine dependency. The minimal
        // MCP container registers IProposalRevisionService, so it must also be able to construct it —
        // otherwise an MCP tool/resource that injects it (or ValidateOnBuild) would fail at resolution.
        var services = new ServiceCollection();
        services.AddScoped(_ => new Mock<IUnitOfWork>().Object);
        services.AddMcpApplicationServices();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var revisionService = scope.ServiceProvider.GetRequiredService<IProposalRevisionService>();

        revisionService.Should().NotBeNull();
    }

    [Fact]
    public void AddMcpApplicationServices_BindsContextFabricSettingsFromConfiguration()
    {
        // ADR-0065 scaffold review: the standalone MCP hosts never call AddTaskdeckSettings, so without
        // this registration the ContextFabric switches would be honoured by the web API and silently
        // ignored by an MCP server writing the same database. CF-01 (#2255) made every switch default
        // on, so the interesting binding case is now an operator turning one OFF.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ContextFabric:DualWriteCaptures"] = "false",
                ["ContextFabric:ReadCapturesFromStore"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped(_ => new Mock<IUnitOfWork>().Object);
        services.AddScoped(_ => new Mock<ICaptureStore>().Object);
        services.AddMcpApplicationServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        var settings = scope.ServiceProvider.GetRequiredService<ContextFabricSettings>();
        settings.DualWriteCaptures.Should().BeFalse();
        settings.ReadCapturesFromStore.Should().BeFalse();
        settings.BackfillCaptures.Should().BeTrue("an unset key keeps its default");
        scope.ServiceProvider.GetRequiredService<ICaptureService>().Should().BeOfType<CaptureService>();
    }

    [Fact]
    public void AddMcpApplicationServices_DefaultsContextFabricSettingsWithoutConfiguration()
    {
        // CF-01 (#2255) turned the Context Fabric on by default. An MCP host with no ContextFabric
        // section must reach the same defaults as the web API, or a capture written through an MCP
        // tool would skip the durable aggregate that the Inbox now reads from.
        var services = new ServiceCollection();
        services.AddScoped(_ => new Mock<IUnitOfWork>().Object);
        services.AddMcpApplicationServices();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var settings = scope.ServiceProvider.GetRequiredService<ContextFabricSettings>();
        settings.DualWriteCaptures.Should().BeTrue();
        settings.BackfillCaptures.Should().BeTrue();
        settings.ReadCapturesFromStore.Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<ICaptureService>().Should().NotBeNull("the 2-arg constructor still resolves without a capture store");
    }
}
