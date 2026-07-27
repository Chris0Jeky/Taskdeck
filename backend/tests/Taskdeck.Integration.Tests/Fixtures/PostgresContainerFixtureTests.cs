using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Integration.Tests.Fixtures;

public sealed class PostgresContainerFixtureTests
{
    [Fact]
    public async Task Public_constructor_defers_container_construction()
    {
        var fixture = new PostgresContainerFixture();

        try
        {
            // Exercise the real public constructor without changing process-global Docker
            // configuration; the original eager constructor leaves this field non-null.
            var containerField = typeof(PostgresContainerFixture).GetField(
                "_container",
                BindingFlags.Instance | BindingFlags.NonPublic);

            containerField.Should().NotBeNull();
            containerField!.GetValue(fixture).Should().BeNull(
                "the public constructor must not evaluate the Testcontainers builder");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

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
