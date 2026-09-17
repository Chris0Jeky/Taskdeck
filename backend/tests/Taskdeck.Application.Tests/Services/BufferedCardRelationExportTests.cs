using System.Runtime.CompilerServices;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class BufferedCardRelationExportTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(499)]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(9_999)]
    [InlineData(10_000)]
    public async Task WithinBudget_ShouldPreserveEveryEligibleRowAndOrder(int count)
    {
        var fixture = new Fixture(count);
        var source = new CountedSource(fixture.Rows);

        var result = await BufferedCardRelationExport.ReadAsync(fixture.CardIdsByBoard, source.Read(), default);

        result.Should().Equal(fixture.Rows);
        source.Yielded.Should().Be(count);
        source.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(10_001)]
    [InlineData(20_000)]
    public async Task Overflow_ShouldRefuseWithoutReadingTheRemainingRows(int count)
    {
        var fixture = new Fixture(count);
        var source = new CountedSource(fixture.Rows);

        var failure = await Assert.ThrowsAsync<DomainException>(() =>
            BufferedCardRelationExport.ReadAsync(fixture.CardIdsByBoard, source.Read(), default));

        failure.ErrorCode.Should().Be(ErrorCodes.PayloadTooLarge);
        failure.Message.Should().Contain("streaming export endpoint");
        source.Yielded.Should().Be(10_001);
        source.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task ExcludedRows_ShouldNeitherLeakNorConsumeTheEligibleBudget()
    {
        var fixture = new Fixture(10_000);
        var first = fixture.Rows[0];
        var otherBoard = fixture.Rows[500];
        UserDataExportCardRelationDto[] excluded =
        [
            first with { BoardId = Guid.Empty },
            first with { BoardId = Guid.NewGuid() },
            first with { SourceCardId = Guid.NewGuid() },
            first with { TargetCardId = Guid.NewGuid() },
            first with { TargetCardId = otherBoard.TargetCardId },
            first with { SourceCardId = otherBoard.SourceCardId }
        ];
        var source = new CountedSource(excluded.Concat(fixture.Rows).Concat(excluded));

        var result = await BufferedCardRelationExport.ReadAsync(fixture.CardIdsByBoard, source.Read(), default);

        result.Should().Equal(fixture.Rows);
        source.Yielded.Should().Be(10_012);
        source.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task NoExportedCards_ShouldNotEnumerateTheRelationReader()
    {
        var source = new CountedSource(new Fixture(1).Rows);

        var result = await BufferedCardRelationExport.ReadAsync(
            new Dictionary<Guid, HashSet<Guid>>(), source.Read(), default);

        result.Should().BeEmpty();
        source.Started.Should().BeFalse();
    }

    [Fact]
    public async Task CancelledBeforeEnumeration_ShouldNotOpenTheReader()
    {
        var fixture = new Fixture(1);
        var source = new CountedSource(fixture.Rows);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BufferedCardRelationExport.ReadAsync(fixture.CardIdsByBoard, source.Read(), cancellation.Token));

        source.Started.Should().BeFalse();
    }

    [Fact]
    public async Task CancellationDuringEnumeration_ShouldDisposeAndPropagateTheToken()
    {
        var fixture = new Fixture(500);
        using var cancellation = new CancellationTokenSource();
        var source = new CountedSource(fixture.Rows, yielded => { if (yielded == 2) cancellation.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BufferedCardRelationExport.ReadAsync(fixture.CardIdsByBoard, source.Read(), cancellation.Token));

        source.ObservedToken.Should().Be(cancellation.Token);
        source.Yielded.Should().Be(2);
        source.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task ReaderFailure_ShouldNotReturnAPartialSuccess()
    {
        var fixture = new Fixture(3);
        var expected = new InvalidOperationException("Read failed");
        var source = new CountedSource(fixture.Rows, yielded => { if (yielded == 2) throw expected; });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BufferedCardRelationExport.ReadAsync(fixture.CardIdsByBoard, source.Read(), default));

        failure.Should().BeSameAs(expected);
        source.Disposed.Should().BeTrue();
    }

    private sealed class Fixture
    {
        public Dictionary<Guid, HashSet<Guid>> CardIdsByBoard { get; } = new();
        public List<UserDataExportCardRelationDto> Rows { get; } = [];
        public Fixture(int count)
        {
            // At most 500 unique acyclic edges per board and 48 cards per board: even the
            // 20,000-row fixture fits the separate 10,000-card buffered admission limit.
            do
            {
                var boardId = Guid.NewGuid();
                var cards = Enumerable.Range(0, 48).Select(_ => Guid.NewGuid()).ToArray();
                CardIdsByBoard.Add(boardId, cards.ToHashSet());
                for (var index = 0; index < 500 && Rows.Count < count; index++)
                    Rows.Add(new(boardId, cards[index / 32], cards[16 + index % 32], "blocks"));
            } while (Rows.Count < count);
        }
    }

    private sealed class CountedSource(IEnumerable<UserDataExportCardRelationDto> rows, Action<int>? onYield = null)
    {
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public int Yielded { get; private set; }
        public CancellationToken ObservedToken { get; private set; }
        public async IAsyncEnumerable<UserDataExportCardRelationDto> Read(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started = true;
            ObservedToken = cancellationToken;
            try
            {
                await Task.CompletedTask;
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Yielded++;
                    onYield?.Invoke(Yielded);
                    yield return row;
                }
            }
            finally { Disposed = true; }
        }
    }
}
