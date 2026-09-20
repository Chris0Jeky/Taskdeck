using System.Data.Common;
using System.Runtime.CompilerServices;
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
using Taskdeck.Domain.Exceptions;
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
    [InlineData(12, false, false, false)]
    [InlineData(50, false, false, false)]
    [InlineData(501, false, false, false)]
    [InlineData(12, true, false, false)]
    [InlineData(12, false, true, false)]
    [InlineData(501, false, true, false)]
    [InlineData(12, false, false, true)]
    public async Task StreamExport_PreservesSequentialBytesAndBatchesHistoryReads(
        int artefactCount, bool longHistory, bool emptyHistory, bool sparseHistory)
    {
        await using var fixture = await Fixture.CreateAsync(
            _services, artefactCount, longHistory, emptyHistory, sparseHistory);
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

    [Theory]
    [InlineData("success")]
    [InlineData("destination")]
    [InlineData("cancellation")]
    [InlineData("missing-blob")]
    public async Task StreamExport_BoundsLookaheadAndDisposesHistoryOnEveryExit(string exit)
    {
        await using var fixture = await Fixture.CreateAsync(_services, 12, longHistory: true);
        if (exit == "missing-blob")
        {
            var first = (await fixture.Artefacts.GetByUserAsync(fixture.UserId, 500, 0))[0];
            var blob = await fixture.Db.ArtefactBlobs.SingleAsync(row => row.SourceArtefactId == first.Id);
            fixture.Db.ArtefactBlobs.Remove(blob);
            await fixture.Db.SaveChangesAsync();
        }

        var lifetime = new StreamLifetime();
        var observedRepository = new Mock<IArtefactExtractionRepository>(MockBehavior.Strict);
        observedRepository.Setup(repository => repository.StreamByArtefactsForUserAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyCollection<Guid> ids, Guid userId, CancellationToken token) =>
                lifetime.Observe(fixture.Extractions.StreamByArtefactsForUserAsync(ids, userId, token), token));
        var service = fixture.CreateService(observedRepository.Object);
        using var cancellation = new CancellationTokenSource();
        fixture.Counter.Reset();
        var observedFirstArtefact = false;
        var firstMaterialised = -1;
        var firstReads = -1;
        var firstYielded = -1;
        await using var destination = new ArtefactProbeStream((_, token) =>
        {
            observedFirstArtefact = true;
            // Capture evidence here, assert outside the export catch-all so a swallowed test
            // assertion cannot masquerade as the expected destination or blob failure.
            firstMaterialised = fixture.Counter.Materialised;
            firstReads = fixture.Counter.HistoryReads;
            firstYielded = lifetime.Yielded;
            if (exit == "destination") throw new IOException("Synthetic destination failure");
            if (exit == "cancellation")
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
        });

        if (exit == "cancellation")
        {
            Func<Task> export = async () =>
                await service.StreamUserDataExportAsync(fixture.UserId, destination, cancellation.Token);
            await export.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var result = await service.StreamUserDataExportAsync(fixture.UserId, destination, cancellation.Token);
            result.IsSuccess.Should().Be(exit == "success", result.ErrorMessage);
            if (exit != "success") result.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
        }

        observedFirstArtefact.Should().BeTrue();
        firstMaterialised.Should().Be(50,
            "the first artefact write may retain one payload page, not the complete 134-row history window");
        firstReads.Should().Be(1);
        firstYielded.Should().Be(1, "the consumer keeps one lookahead row outside the repository page");
        lifetime.Started.Should().BeTrue();
        lifetime.Disposed.Should().BeTrue("success, output failure, missing blobs and cancellation all release the iterator");
        fixture.Counter.HistoryReads.Should().Be(exit == "success" ? 3 : 1,
            "early failure must not advance into another history page");
        fixture.History.Verify(history => history.LogActionAsync("User", fixture.UserId,
            AuditAction.DataExported, fixture.UserId, It.IsAny<string>()),
            exit == "success" ? Times.Once() : Times.Never());
        destination.CanWrite.Should().BeTrue("the caller owns the destination stream");
        // Both history and incremental blob copying use this context/connection. An independent
        // follow-up command must remain usable after either completion or early disposal.
        (await fixture.Db.Users.AsNoTracking().CountAsync(user => user.Id == fixture.UserId)).Should().Be(1);
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
        // The old tail writes Base64 directly, unlike JsonSerializer's default string encoder,
        // which can escape '+'. Preserve that token's original spelling as well as its bytes.
        var objects = exported.Select(artefact => JsonSerializer.Serialize(artefact, Json).Replace(
            "\"contentBase64\":" + JsonSerializer.Serialize(artefact.ContentBase64, Json),
            "\"contentBase64\":\"" + artefact.ContentBase64 + "\"", StringComparison.Ordinal));
        return (Encoding.UTF8.GetBytes(",\"artefacts\":[" + string.Join(",", objects) + "]}}"), expectedReads);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly IServiceScope _scope;
        private readonly IUnitOfWork _unit;
        public TaskdeckDbContext Db { get; }
        public ReadCounter Counter { get; } = new();
        public Mock<IHistoryService> History { get; } = new();
        public SourceArtefactRepository Artefacts { get; }
        public ArtefactExtractionRepository Extractions { get; }
        public DataExportService Service { get; }
        public Guid UserId { get; private set; }

        private Fixture(IServiceProvider services)
        {
            _scope = services.CreateScope();
            _unit = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var original = _scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(original.Database.GetConnectionString()!)
                .AddInterceptors(Counter, MaterialisationObserver.Instance).Options;
            Db = new TaskdeckDbContext(options);
            MaterialisationObserver.Instance.Register(Db, Counter);
            Artefacts = new SourceArtefactRepository(Db);
            Extractions = new ArtefactExtractionRepository(Db);
            History.Setup(service => service.LogActionAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<AuditAction>(), It.IsAny<Guid?>(), It.IsAny<string?>()))
                .ReturnsAsync(Result.Success());
            Service = CreateService(Extractions);
        }

        public DataExportService CreateService(IArtefactExtractionRepository extractions) =>
            new(_unit, History.Object, Artefacts, extractions,
                new TranscriptRepository(Db), new WorkspaceInsightRepository(Db));

        public static async Task<Fixture> CreateAsync(IServiceProvider services,
            int count, bool longHistory = false, bool emptyHistory = false, bool sparseHistory = false)
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
                    // Exercise '+', '/', padding and non-ASCII text in the raw Base64 token.
                    var content = new byte[] { 251, 255, 254 }.Concat(
                        Encoding.UTF8.GetBytes("blob " + index + " \u00e9")).ToArray();
                    var artefact = new SourceArtefact(owner, ArtefactKind.TextFile, "text/plain",
                        "note-" + index + ".txt", content.Length, new string('a', 64), CaptureSource.Import);
                    typeof(Entity).GetProperty(nameof(Entity.CreatedAt))!.SetValue(artefact, timestamp.AddMinutes(-index));
                    fixture.Db.SourceArtefacts.Add(artefact);
                    fixture.Db.ArtefactBlobs.Add(new ArtefactBlob(artefact.Id, content));
                    var rowCount = emptyHistory || (sparseHistory && index % 3 != 0)
                        ? 0 : longHistory && index == 0 ? 123 : 1;
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

    private sealed class StreamLifetime
    {
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public int Yielded { get; private set; }
        public async IAsyncEnumerable<ArtefactExtraction> Observe(IAsyncEnumerable<ArtefactExtraction> rows,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Started = true;
            try
            {
                await foreach (var row in rows.WithCancellation(cancellationToken))
                {
                    Yielded++;
                    yield return row;
                }
            }
            finally { Disposed = true; }
        }
    }

    private sealed class ArtefactProbeStream(Action<ReadOnlyMemory<byte>, CancellationToken> firstArtefact) : MemoryStream
    {
        private bool _inArtefacts;
        private bool _fired;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Span.SequenceEqual(",\"artefacts\":["u8)) _inArtefacts = true;
            if (_inArtefacts && !_fired && buffer.Span.StartsWith("{\"id\":"u8))
            {
                _fired = true;
                firstArtefact(buffer, cancellationToken);
            }
            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    // EF treats materialisation interceptors as singleton services: reuse one instance and route
    // counters by weak context identity instead of creating a service provider for every fixture.
    private sealed class MaterialisationObserver : IMaterializationInterceptor
    {
        public static MaterialisationObserver Instance { get; } = new();
        private readonly ConditionalWeakTable<DbContext, ReadCounter> _counters = new();
        public void Register(DbContext context, ReadCounter counter) => _counters.Add(context, counter);
        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            if (entity is ArtefactExtraction && _counters.TryGetValue(data.Context, out var counter))
                counter.RecordMaterialised();
            return entity;
        }
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        public int HistoryReads { get; private set; }
        public int Materialised { get; private set; }
        public void Reset() { HistoryReads = 0; Materialised = 0; }
        public void RecordMaterialised() => Materialised++;
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
