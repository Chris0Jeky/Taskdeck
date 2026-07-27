using FluentAssertions;
using Xunit;

namespace Taskdeck.Integration.Tests.Fixtures;

public sealed class PostgresContainerFixtureTests
{
    [Fact]
    public async Task Docker_unavailable_does_not_construct_a_container_and_disposes_safely()
    {
        var factoryInvocationCount = 0;
        var fixture = new PostgresContainerFixture(
            dockerAvailabilityCheck: () => false,
            containerFactory: () =>
            {
                factoryInvocationCount++;
                throw new InvalidOperationException(
                    "The container factory must not run when Docker is unavailable.");
            });

        await fixture.InitializeAsync();
        var dispose = async () => await fixture.DisposeAsync();

        fixture.IsAvailable.Should().BeFalse();
        factoryInvocationCount.Should().Be(0);
        await dispose.Should().NotThrowAsync();
    }
}
