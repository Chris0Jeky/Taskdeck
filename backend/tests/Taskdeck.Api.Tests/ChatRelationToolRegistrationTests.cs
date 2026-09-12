using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Tools;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ChatRelationToolRegistrationTests(HostedWorkerDisabledTestWebApplicationFactory factory)
    : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    [Fact]
    public void ChatRegistry_ResolvesBoardRelationReaderThatMatchesTheAdvertisedSchema()
    {
        using var scope = factory.Services.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<ToolExecutorRegistry>();
        registry.GetExecutor("get_board_card_relations").Should().BeOfType<GetBoardCardRelationsExecutor>();
        ReadToolSchemas.GetAll().Select(schema => schema.Name).Should().Contain("get_board_card_relations");
    }
}
