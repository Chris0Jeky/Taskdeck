using FluentAssertions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class TestWebApplicationFactoryTests
{
    [Fact]
    public void GetDatabaseCleanupTargets_ShouldIncludeSqliteSidecars()
    {
        var targets = TestWebApplicationFactory.GetDatabaseCleanupTargets("C:\\temp\\taskdeck-api-tests.db");

        targets.Should().Equal(
            "C:\\temp\\taskdeck-api-tests.db",
            "C:\\temp\\taskdeck-api-tests.db-wal",
            "C:\\temp\\taskdeck-api-tests.db-shm",
            "C:\\temp\\taskdeck-api-tests.db-journal");
    }
}
