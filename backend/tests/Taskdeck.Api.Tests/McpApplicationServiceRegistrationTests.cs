using FluentAssertions;
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
}
