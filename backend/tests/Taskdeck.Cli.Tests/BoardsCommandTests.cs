using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class BoardsCommandTests
{
    [Fact]
    public async Task BoardsList_EmptyDatabase_ReturnsEmptyJsonArray()
    {
        await using var harness = new CliTestHarness("cli-boards");

        var result = await harness.RunAsync("boards list --json");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task BoardsCreate_MissingName_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-boards");

        var result = await harness.RunAsync("boards create --json");

        // With "--json" stripped, there are no remaining args so create fails.
        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Missing board name");
    }

    [Fact]
    public async Task BoardsCreate_WithDescription_IncludesDescription()
    {
        await using var harness = new CliTestHarness("cli-boards");

        var result = await harness.RunAsync("boards create DescBoard \"A nice description\" --json");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("name").GetString().Should().Be("DescBoard");
        doc.RootElement.GetProperty("description").GetString().Should().Be("A nice description");
    }

    [Fact]
    public async Task BoardsUpdate_WithName_UpdatesBoardName()
    {
        await using var harness = new CliTestHarness("cli-boards");

        var createResult = await harness.RunAsync("boards create OrigName --json");
        createResult.ExitCode.Should().Be(0, createResult.StdErr);
        using var createDoc = JsonDocument.Parse(createResult.StdOut);
        var boardId = createDoc.RootElement.GetProperty("id").GetGuid();

        var updateResult = await harness.RunAsync($"boards update --board {boardId} --name NewName --json");

        updateResult.ExitCode.Should().Be(0, updateResult.StdErr);
        using var updateDoc = JsonDocument.Parse(updateResult.StdOut);
        updateDoc.RootElement.GetProperty("name").GetString().Should().Be("NewName");
    }

    [Fact]
    public async Task BoardsUpdate_NoUpdateValues_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-boards");

        var createResult = await harness.RunAsync("boards create NoUpdBoard --json");
        createResult.ExitCode.Should().Be(0, createResult.StdErr);
        using var createDoc = JsonDocument.Parse(createResult.StdOut);
        var boardId = createDoc.RootElement.GetProperty("id").GetGuid();

        var updateResult = await harness.RunAsync($"boards update --board {boardId} --json");

        updateResult.ExitCode.Should().Be(2);
        updateResult.StdErr.Should().Contain("No update values");
    }

    [Fact]
    public async Task BoardsUpdate_ArchiveAndUnarchiveTogether_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-boards");

        var createResult = await harness.RunAsync("boards create ConflictBoard --json");
        createResult.ExitCode.Should().Be(0, createResult.StdErr);
        using var createDoc = JsonDocument.Parse(createResult.StdOut);
        var boardId = createDoc.RootElement.GetProperty("id").GetGuid();

        var updateResult = await harness.RunAsync($"boards update --board {boardId} --archive --unarchive --json");

        updateResult.ExitCode.Should().Be(2);
        updateResult.StdErr.Should().Contain("--archive and --unarchive");
    }

    [Fact]
    public async Task BoardsArchiveUnarchive_FiltersListAndRestoresBoard()
    {
        await using var harness = new CliTestHarness("cli-boards");

        var createResult = await harness.RunAsync("boards create ArchiveBoard --json");
        createResult.ExitCode.Should().Be(0, createResult.StdErr);
        using var createDoc = JsonDocument.Parse(createResult.StdOut);
        var boardId = createDoc.RootElement.GetProperty("id").GetGuid();

        var archiveResult = await harness.RunAsync($"boards update --board {boardId} --archive --json");
        archiveResult.ExitCode.Should().Be(0, archiveResult.StdErr);
        using var archiveDoc = JsonDocument.Parse(archiveResult.StdOut);
        archiveDoc.RootElement.GetProperty("isArchived").GetBoolean().Should().BeTrue();

        var listResult = await harness.RunAsync("boards list --json");
        listResult.ExitCode.Should().Be(0, listResult.StdErr);
        using var listDoc = JsonDocument.Parse(listResult.StdOut);
        listDoc.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .Should()
            .NotContain(boardId);

        var includeArchivedResult = await harness.RunAsync("boards list --include-archived --json");
        includeArchivedResult.ExitCode.Should().Be(0, includeArchivedResult.StdErr);
        using var includeArchivedDoc = JsonDocument.Parse(includeArchivedResult.StdOut);
        includeArchivedDoc.RootElement.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == boardId)
            .GetProperty("isArchived").GetBoolean().Should().BeTrue();

        var unarchiveResult = await harness.RunAsync($"boards update --board {boardId} --unarchive --json");
        unarchiveResult.ExitCode.Should().Be(0, unarchiveResult.StdErr);
        using var unarchiveDoc = JsonDocument.Parse(unarchiveResult.StdOut);
        unarchiveDoc.RootElement.GetProperty("isArchived").GetBoolean().Should().BeFalse();

        var restoredListResult = await harness.RunAsync("boards list --json");
        restoredListResult.ExitCode.Should().Be(0, restoredListResult.StdErr);
        using var restoredListDoc = JsonDocument.Parse(restoredListResult.StdOut);
        restoredListDoc.RootElement.EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == boardId)
            .GetProperty("isArchived").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Boards_UnknownCommand_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-boards");

        var result = await harness.RunAsync("boards unknown");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Unknown boards command");
    }
}
