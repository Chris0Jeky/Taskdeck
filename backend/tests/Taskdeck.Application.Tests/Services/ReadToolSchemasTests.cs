using System.Text.Json;
using FluentAssertions;
using Xunit;
using Taskdeck.Application.Services.Tools;

namespace Taskdeck.Application.Tests.Services;

public class ReadToolSchemasTests
{
    [Fact]
    public void GetAll_Returns5Schemas()
    {
        var schemas = ReadToolSchemas.GetAll();
        schemas.Count.Should().Be(5);
    }

    [Fact]
    public void GetAll_AllHaveUniqueNames()
    {
        var schemas = ReadToolSchemas.GetAll();
        var names = schemas.Select(s => s.Name).Distinct().ToList();
        names.Count.Should().Be(schemas.Count);
    }

    [Fact]
    public void ListBoardColumns_HasCorrectSchema()
    {
        var schema = ReadToolSchemas.ListBoardColumns();
        schema.Name.Should().Be("list_board_columns");
        schema.Description.Should().NotBeNullOrEmpty();
        schema.Required.Should().BeEmpty();
        schema.ParametersSchema.GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public void ListCardsInColumn_RequiresColumnName()
    {
        var schema = ReadToolSchemas.ListCardsInColumn();
        schema.Name.Should().Be("list_cards_in_column");
        schema.Required.Should().Contain("column_name");
        schema.ParametersSchema.GetProperty("properties")
            .GetProperty("column_name")
            .GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void GetCardDetails_RequiresCardId()
    {
        var schema = ReadToolSchemas.GetCardDetails();
        schema.Name.Should().Be("get_card_details");
        schema.Required.Should().Contain("card_id");
    }

    [Fact]
    public void SearchCards_RequiresQuery()
    {
        var schema = ReadToolSchemas.SearchCards();
        schema.Name.Should().Be("search_cards");
        schema.Required.Should().Contain("query");
    }

    [Fact]
    public void GetBoardLabels_HasNoRequiredParameters()
    {
        var schema = ReadToolSchemas.GetBoardLabels();
        schema.Name.Should().Be("get_board_labels");
        schema.Required.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllSchemas))]
    public void AllSchemas_HaveValidJsonParametersSchema(string name)
    {
        var schema = ReadToolSchemas.GetAll().First(s => s.Name == name);
        // Should be valid JSON that can be round-tripped
        var json = schema.ParametersSchema.GetRawText();
        var reparsed = JsonDocument.Parse(json);
        reparsed.RootElement.GetProperty("type").GetString().Should().Be("object");
    }

    public static IEnumerable<object[]> AllSchemas()
    {
        return ReadToolSchemas.GetAll().Select(s => new object[] { s.Name });
    }
}
