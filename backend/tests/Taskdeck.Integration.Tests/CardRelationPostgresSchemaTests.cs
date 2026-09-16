using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Integration.Tests;

public sealed class CardRelationPostgresSchemaTests
{
    [Fact]
    public void NpgsqlCreateScript_QuotesCardRelationCheckConstraintIdentifiers()
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseNpgsql("Host=localhost;Database=taskdeck_schema_regression")
            .Options;

        using var db = new TaskdeckDbContext(options);
        var createScript = db.Database.GenerateCreateScript();

        createScript.Should().Contain(
            "CHECK (\"RelationType\" IN ('relates-to', 'blocks', 'duplicates', 'spawned-from'))");
        createScript.Should().Contain("CHECK (\"SourceCardId\" <> \"TargetCardId\")");
        createScript.Should().Contain(
            "CHECK (\"RelationType\" <> 'relates-to' OR \"SourceCardId\" < \"TargetCardId\")");
    }
}
