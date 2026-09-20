using System.Data.Common;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// GH-1399 consumer proof: drive the entire export service, including incremental blob copying,
/// against SQLite. The byte oracle uses the previous sequential repository contract, not the
/// new stream, and SQL counting observes actual commands rather than mocked invocation counts.
/// </summary>
public sealed class StreamingExportHistoryConsumerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly IServiceProvider _services;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public StreamingExportHistoryConsumerTests(TestWebApplicationFactory factory) =>
        _services = factory.Services;

    [Theory]
    [InlineData(12, false, false)]
    [InlineData(501, false, false)]
    [InlineData(12, true, false)]
    [InlineData(12, false, true)]
    public async Task StreamExport_PreservesSequentialBytesAndBatchesHistoryReads(
        int artefactCount, bool longHistory, bool emptyHistory)
    {
        await using var fixture = await Fixture.CreateAsync(_services, artefactCount, longHistory, emptyHistory);
        var (expected, expectedReads) = await RenderSequentialTailAsync(fixture);
        fixture.Counter.Reset();
        await using var destination = new MemoryStream();

        var result = await fixture.Service.StreamUserDataExportAsync(fixture.UserId, destination);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        using var document = JsonDocument.Parse(destination.ToArray());
        document.RootElement.GetProperty("data").GetProperty("artefacts").GetArrayLength()
            .Should().Be(artefactCount, "foreign-owner artefacts must not enter the export");
        var bytes = destination.ToArray();
        var start = bytes.AsSpan().IndexOf(",\"artefacts\":["u8);
        start.Should().BeGreaterThanOrEqualTo(0);
        bytes.Skip(start).Should().Equal(expected, "the artefact tail must retain the previous byte contract");
        fixture.Counter.HistoryReads.Should().Be(expectedReads,
            "history reads are bounded pages across an artefact window, not one query per artefact");
    }

    private static async Task<(byte[] Bytes, int Reads)> RenderSequentialTailAsync(Fixture fixture)
    {
        var exported = new List<UserDataExportArtefactDto>();
        var expectedReads = 0;
        for (var offset = 0; ; offset += 500)
        {
            var page = await fixture.Artefacts.GetByUserAsync(fixture.UserId, 500, offset);
            if (page.Count == 0) break;
            var rowsInWindow = 0;
            foreach (var artefact in page)
            {
                var history = new List<UserDataExportArtefactExtractionDto>();
                for (var historyOffset = 0; ; historyOffset += 50)
                {
                    var rows = await fixture.Extractions.GetByArtefactForUserAsync(
                        artefact.Id, fixture.UserId, 50, historyOffset);
                    history.AddRange(rows.Select(row => new UserDataExportArtefactExtractionDto(
                        row.Id, row.ExtractorName, row.ExtractorVersion, row.Warnings,
                        row.ExtractedText, row.TextLength, row.CreatedAt)));
                    if (rows.Count < 50) break;
                }
                rowsInWindow += history.Count;
                var content = await fixture.Artefacts.GetContentForUserAsync(artefact.Id, fixture.UserId);
                content.Should().NotBeNull();
                exported.Add(new UserDataExportArtefactDto(artefact.Id, artefact.BoardId,
                    artefact.Kind.ToString(), artefact.MimeType, artefact.FileName, artefact.ByteSize,
                    artefact.Sha256, artefact.CaptureSource.ToString(), artefact.OriginReference,
                    artefact.CreatedFromCaptureId, artefact.CreatedAt, Convert.ToBase64String(content!), history));
            }
            // A full 50-row page needs one final empty query; an empty history window needs one.
            expectedReads += rowsInWindow / 50 + 1;
            if (page.Count < 500) break;
        }
        return (Encoding.UTF8.GetBytes(",\"artefacts\":" + JsonSerializer.Serialize(exported, Json) + "}}"), expectedReads);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        public TaskdeckDbContext Db { get; }
        public ReadCounter Counter { get; } = new();
        public SourceArtefactRepository Artefacts { get; }
        public ArtefactExtractionRepository Extractions { get; }
        public DataExportService Service { get; }
        public Guid UserId { get; private set; }

        private Fixture(IServiceProvider services)
        {
            _scope = services.CreateScope();
            var unit = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var original = _scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(original.Database.GetConnectionString()!)
                .AddInterceptors(Counter).Options;
            Db = new TaskdeckDbContext(options);
            Artefacts = new SourceArtefactRepository(Db);
            Extractions = new ArtefactExtractionRepository(Db);
            var history = new Mock<IHistoryService>();
            history.Setup(service => service.LogActionAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<AuditAction>(), It.IsAny<Guid?>(), It.IsAny<string?>()))
                .ReturnsAsync(Result.Success());
            Service = new DataExportService(unit, history.Object, Artefacts, Extractions,
                new TranscriptRepository(Db), new WorkspaceInsightRepository(Db));
        }

        public static async Task<Fixture> CreateAsync(IServiceProvider services,
            int count, bool longHistory = false, bool emptyHistory = false)
        {
            var fixture = new Fixture(services);
            try
            {
                var name = Guid.NewGuid().ToString("N");
                var user = new User("export-" + name, name + "@example.com", "hash");
                var foreign = new User("foreign-" + name, "foreign-" + name + "@example.com", "hash");
                fixture.UserId = user.Id;
                fixture.Db.Users.AddRange(user, foreign);
                var timestamp = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
                for (var index = 0; index <= count; index++)
                {
                    var owner = index == count ? foreign.Id : user.Id;
                    var content = Encoding.UTF8.GetBytes("blob " + index + " \u00e9");
                    var artefact = new SourceArtefact(owner, ArtefactKind.TextFile, "text/plain",
                        "note-" + index + ".txt", content.Length, new string('a', 64), CaptureSource.Import);
                    typeof(Entity).GetProperty(nameof(Entity.CreatedAt))!.SetValue(artefact, timestamp.AddMinutes(-index));
                    fixture.Db.SourceArtefacts.Add(artefact);
                    fixture.Db.ArtefactBlobs.Add(new ArtefactBlob(artefact.Id, content));
                    var rowCount = emptyHistory ? 0 : longHistory && index == 0 ? 123 : 1;
                    for (var row = 0; row < rowCount; row++)
                    {
                        var extraction = new ArtefactExtraction(artefact.Id, "fixture", "1.0",
                            ["fixture-warning"], $"text {index}/{row} \u00e9 \"quoted\"\nline");
                        typeof(Entity).GetProperty(nameof(Entity.CreatedAt))!.SetValue(extraction, timestamp);
                        fixture.Db.ArtefactExtractions.Add(extraction);
                    }
                }
                await fixture.Db.SaveChangesAsync();
                fixture.Db.ChangeTracker.Clear();
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _scope.Dispose();
        }
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        public int HistoryReads { get; private set; }
        public void Reset() => HistoryReads = 0;
        private void Record(DbCommand command)
        {
            if (command.CommandText.Contains("ArtefactExtractions", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
                HistoryReads++;
        }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return result;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }
    }
}
