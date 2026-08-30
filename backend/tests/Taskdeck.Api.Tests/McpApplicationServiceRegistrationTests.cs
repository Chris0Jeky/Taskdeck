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
        // this registration ContextFabric:DualWriteCaptures would be honoured by the web API and
        // silently ignored by an MCP server writing the same database.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ContextFabric:DualWriteCaptures"] = "true" })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped(_ => new Mock<IUnitOfWork>().Object);
        services.AddScoped(_ => new Mock<ICaptureStore>().Object);
        services.AddMcpApplicationServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ContextFabricSettings>().DualWriteCaptures.Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<ICaptureService>().Should().BeOfType<CaptureService>();
    }

    [Fact]
    public void AddMcpApplicationServices_DefaultsContextFabricSettingsWithoutConfiguration()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new Mock<IUnitOfWork>().Object);
        services.AddMcpApplicationServices();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ContextFabricSettings>().DualWriteCaptures.Should().BeFalse();
        scope.ServiceProvider.GetRequiredService<ICaptureService>().Should().NotBeNull("the 2-arg constructor still resolves without a capture store");
    }
}
