using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class McpStdioTransportTests
{
    [Fact]
    public async Task ProductionStdioTransport_SearchesSeededCardAndReadsBoardsResource()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            $"taskdeck-mcp-stdio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);

        try
        {
            var dbPath = Path.Combine(testRoot, "taskdeck.db");
            var sentinel = $"stdio-sentinel-{Guid.NewGuid():N}";
            var userId = await SeedWorkspaceAsync(dbPath, sentinel);
            var apiDll = ResolveProductionApiAssembly();
            var stderr = new ConcurrentQueue<string>();
            var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
            // The real SDK request-scope failure is visible only when the Generic Host validates
            // service lifetimes. Keep that validation on while launching the production entry point.
            environment["DOTNET_ENVIRONMENT"] = "Development";
            environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            environment["ConnectionStrings__DefaultConnection"] = TestSqlite.ConnectionString(dbPath);
            environment["Connectors__EncryptionKey"] =
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
            environment["Database__Backup__Enabled"] = "false";
            environment["McpServer__DefaultUserId"] = userId.ToString();
            environment["Llm__Provider"] = "Mock";
            environment["Llm__EnableLiveProviders"] = "false";

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "Taskdeck.Api production stdio regression",
                Command = "dotnet",
                Arguments = [apiDll, "--mcp"],
                WorkingDirectory = Path.GetDirectoryName(apiDll),
                InheritEnvironmentVariables = false,
                EnvironmentVariables = environment,
                ShutdownTimeout = TimeSpan.FromSeconds(5),
                StandardErrorLines = line => stderr.Enqueue(line)
            });

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using var client = await McpClient.CreateAsync(
                transport,
                cancellationToken: timeout.Token);

            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            tools.Select(tool => tool.Name).Should().Contain("search_cards");
            tools.Should().HaveCount(11, StderrContext(stderr));

            var searchResult = await client.CallToolAsync(
                "search_cards",
                new Dictionary<string, object?> { ["query"] = sentinel },
                cancellationToken: timeout.Token);
            searchResult.IsError.Should().NotBeTrue(StderrContext(stderr));
            searchResult.Content
                .OfType<TextContentBlock>()
                .Select(content => content.Text)
                .Should().ContainSingle(text => text.Contains(sentinel, StringComparison.Ordinal));

            var boardsResource = await client.ReadResourceAsync(
                "taskdeck://boards",
                cancellationToken: timeout.Token);
            boardsResource.Contents
                .OfType<TextResourceContents>()
                .Select(content => content.Text)
                .Should().ContainSingle(text => text.Contains(sentinel, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static async Task<Guid> SeedWorkspaceAsync(string dbPath, string sentinel)
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(TestSqlite.ConnectionString(dbPath))
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(5000))
            .Options;

        await using var db = new TaskdeckDbContext(options);
        await db.Database.MigrateAsync();

        var user = new User(
            $"stdio-{Guid.NewGuid():N}",
            $"stdio-{Guid.NewGuid():N}@example.com",
            "synthetic-password-hash");
        var board = new Board($"{sentinel}-board", ownerId: user.Id);
        var column = new Column(board.Id, "Backlog", 0);
        var card = new Card(board.Id, column.Id, sentinel);

        db.AddRange(user, board, column, card);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static string ResolveProductionApiAssembly()
    {
        var targetFrameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = targetFrameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Could not resolve the test build configuration.");
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "Taskdeck.Api",
            "bin",
            configuration,
            targetFrameworkDirectory.Name,
            "Taskdeck.Api.dll"));
        File.Exists(path).Should().BeTrue($"the production API assembly must exist at {path}");
        return path;
    }

    private static string StderrContext(ConcurrentQueue<string> stderr)
    {
        var tail = stderr.TakeLast(20).ToArray();
        return tail.Length == 0
            ? "the child emitted no stderr diagnostics"
            : $"child stderr tail: {string.Join(Environment.NewLine, tail)}";
    }
}
