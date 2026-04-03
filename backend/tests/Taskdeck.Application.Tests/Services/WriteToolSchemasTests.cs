using System.Text.Json;
using FluentAssertions;
using Xunit;
using Taskdeck.Application.Services.Tools;

namespace Taskdeck.Application.Tests.Services;

public class WriteToolSchemasTests
{
    [Fact]
    public void GetAll_Returns6Schemas()
    {
        var schemas = WriteToolSchemas.GetAll();
        schemas.Count.Should().Be(6);
    }

    [Fact]
    public void GetAll_AllHaveUniqueNames()
    {
        var schemas = WriteToolSchemas.GetAll();
        var names = schemas.Select(s => s.Name).Distinct().ToList();
        names.Count.Should().Be(schemas.Count);
    }

    [Fact]
    public void GetAll_AllNamesStartWithPropose()
    {
        var schemas = WriteToolSchemas.GetAll();
        foreach (var schema in schemas)
        {
            schema.Name.Should().StartWith("propose_",
                because: "write tools must use the propose_ prefix per GP-06");
        }
    }

    [Fact]
    public void ProposeCreateCard_RequiresTitle()
    {
        var schema = WriteToolSchemas.ProposeCreateCard();
        schema.Name.Should().Be("propose_create_card");
        schema.Required.Should().Contain("title");
        schema.Description.Should().Contain("proposal");
    }

    [Fact]
    public void ProposeCreateCard_HasOptionalParameters()
    {
        var schema = WriteToolSchemas.ProposeCreateCard();
        var props = schema.ParametersSchema.GetProperty("properties");
        props.TryGetProperty("column_name", out _).Should().BeTrue();
        props.TryGetProperty("description", out _).Should().BeTrue();
        props.TryGetProperty("labels", out _).Should().BeTrue();
    }

    [Fact]
    public void ProposeMoveCard_RequiresCardIdAndTargetColumn()
    {
        var schema = WriteToolSchemas.ProposeMoveCard();
        schema.Name.Should().Be("propose_move_card");
        schema.Required.Should().Contain("card_id");
        schema.Required.Should().Contain("target_column");
    }

    [Fact]
    public void ProposeArchiveCard_RequiresCardId()
    {
        var schema = WriteToolSchemas.ProposeArchiveCard();
        schema.Name.Should().Be("propose_archive_card");
        schema.Required.Should().Contain("card_id");
    }

    [Fact]
    public void ProposeUpdateCard_RequiresCardId()
    {
        var schema = WriteToolSchemas.ProposeUpdateCard();
        schema.Name.Should().Be("propose_update_card");
        schema.Required.Should().Contain("card_id");
        // title, description, labels are all optional
        schema.Required.Should().NotContain("title");
        schema.Required.Should().NotContain("description");
        schema.Required.Should().NotContain("labels");
    }

    [Fact]
    public void ProposeBulkMove_RequiresSourceAndTargetColumns()
    {
        var schema = WriteToolSchemas.ProposeBulkMove();
        schema.Name.Should().Be("propose_bulk_move");
        schema.Required.Should().Contain("source_column");
        schema.Required.Should().Contain("target_column");
        // card_ids is optional
        schema.Required.Should().NotContain("card_ids");
    }

    [Fact]
    public void ProposeCreateColumn_RequiresName()
    {
        var schema = WriteToolSchemas.ProposeCreateColumn();
        schema.Name.Should().Be("propose_create_column");
        schema.Required.Should().Contain("name");
        // position is optional
        schema.Required.Should().NotContain("position");
    }

    [Theory]
    [MemberData(nameof(AllSchemas))]
    public void AllSchemas_HaveValidJsonParametersSchema(string name)
    {
        var schema = WriteToolSchemas.GetAll().First(s => s.Name == name);
        var json = schema.ParametersSchema.GetRawText();
        var reparsed = JsonDocument.Parse(json);
        reparsed.RootElement.GetProperty("type").GetString().Should().Be("object");
    }

    [Theory]
    [MemberData(nameof(AllSchemas))]
    public void AllSchemas_HaveAdditionalPropertiesFalse(string name)
    {
        var schema = WriteToolSchemas.GetAll().First(s => s.Name == name);
        schema.ParametersSchema.TryGetProperty("additionalProperties", out var ap).Should().BeTrue();
        ap.GetBoolean().Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(AllSchemas))]
    public void AllSchemas_DescriptionMentionsProposal(string name)
    {
        var schema = WriteToolSchemas.GetAll().First(s => s.Name == name);
        schema.Description.Should().Contain("proposal",
            because: "write tool descriptions should make it clear they create proposals");
    }

    public static IEnumerable<object[]> AllSchemas()
    {
        return WriteToolSchemas.GetAll().Select(s => new object[] { s.Name });
    }
}
