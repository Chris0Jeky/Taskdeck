using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Unit contract for #2452: related-proposal evidence must match on each candidate's
/// EFFECTIVE operation set (latest pending revision, approved pin, or the decision-time
/// revision of a rejected proposal), never on its immutable creation-time rows, and must
/// exclude the proposal under review. Every inclusion case is paired with the negative
/// control that fails if the service silently fell back to the raw operation rows.
/// </summary>
public class RelatedProposalEvidenceServiceTests
{
    private const int CandidatePageSize = 100;

    private readonly Mock<IProposalEvidenceCandidateStore> _candidates = new();
    private readonly Mock<IProposalRevisionRepository> _revisions = new();
    private readonly RelatedProposalEvidenceService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _currentProposalId = Guid.NewGuid();
    private readonly Guid _cardId = Guid.NewGuid();
    private readonly Guid _otherCardId = Guid.NewGuid();

    public RelatedProposalEvidenceServiceTests()
    {
        _service = new RelatedProposalEvidenceService(_candidates.Object, _revisions.Object);
        SetupRevisions();
    }

    private ProposalEvidenceScope Scope => new(_boardId, _userId);

    #region Pending duplicate detection

    [Fact]
    public async Task HasOtherPendingProposal_ExcludesTheProposalUnderReview()
    {
        // Negative control for self-exclusion: the ONLY pending proposal targeting the
        // card is the one being reviewed, so there is no duplicate.
        var current = PendingProposal("update", _cardId);
        SetupPendingPage(current);

        var result = await _service.HasOtherPendingProposalTargetingCardAsync(
            Scope, current.Id, _cardId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasOtherPendingProposal_FindsTargetIntroducedByLatestPendingRevision()
    {
        // Raw rows target another card; the latest pending revision moves onto _cardId.
        var related = PendingProposal("update", _otherCardId);
        var revision = Revision(related, 1, "update", _cardId);
        SetupPendingPage(related);
        SetupRevisions((related, revision));

        var result = await _service.HasOtherPendingProposalTargetingCardAsync(
            Scope, _currentProposalId, _cardId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasOtherPendingProposal_IgnoresTargetRemovedByLatestPendingRevision()
    {
        // Raw rows target _cardId -- the pre-fix SQL predicate's false positive.
        var related = PendingProposal("update", _cardId);
        var revision = Revision(related, 1, "update", _otherCardId);
        SetupPendingPage(related);
        SetupRevisions((related, revision));

        var result = await _service.HasOtherPendingProposalTargetingCardAsync(
            Scope, _currentProposalId, _cardId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasOtherPendingProposal_UsesTheHighestRevisionNumber_NotTheFirst()
    {
        var related = PendingProposal("update", _otherCardId);
        var superseded = Revision(related, 1, "update", _otherCardId);
        var latest = Revision(related, 2, "update", _cardId);
        SetupPendingPage(related);
        SetupRevisions((related, superseded), (related, latest));

        var result = await _service.HasOtherPendingProposalTargetingCardAsync(
            Scope, _currentProposalId, _cardId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasOtherPendingProposal_PagesPastAFullFirstPage()
    {
        var firstPage = Enumerable.Range(0, CandidatePageSize)
            .Select(_ => PendingProposal("update", _otherCardId))
            .ToList();
        var match = PendingProposal("update", _cardId);

        _candidates
            .Setup(store => store.ReadPendingPageAsync(
                Scope, 0, CandidatePageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AutomationProposal>)firstPage);
        _candidates
            .Setup(store => store.ReadPendingPageAsync(
                Scope, CandidatePageSize, CandidatePageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AutomationProposal>)new List<AutomationProposal> { match });

        var result = await _service.HasOtherPendingProposalTargetingCardAsync(
            Scope, _currentProposalId, _cardId);

        result.Should().BeTrue();
        _candidates.Verify(
            store => store.ReadPendingPageAsync(
                Scope, CandidatePageSize, CandidatePageSize, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Related card history

    [Fact]
    public async Task GetLatestOtherProposal_SkipsTheProposalUnderReview_AndReturnsTheNextMatch()
    {
        // The proposal under review is the newest row targeting the card. Before #2452 the
        // repository returned it and the ledger lost the genuinely related proposal.
        var current = PendingProposal("update", _cardId);
        var related = PendingProposal("update", _cardId);
        SetupHistoryPage(current, related);

        var result = await _service.GetLatestOtherProposalTargetingCardAsync(
            Scope, current.Id, _cardId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(related.Id);
    }

    [Fact]
    public async Task GetLatestOtherProposal_FollowsTheApprovedPin_NotALaterUnpinnedRevision()
    {
        // Raw rows target another card; the approved pin targets _cardId; a post-approval
        // revision moves away again and must NOT displace the pin.
        var applied = PendingProposal("move", _otherCardId);
        var pinned = Revision(applied, 1, "update", _cardId);
        applied.Approve(_userId, pinned.Id);
        applied.MarkAsApplied();
        var later = Revision(applied, 2, "archive", _otherCardId);
        SetupHistoryPage(applied);
        SetupRevisions((applied, pinned), (applied, later));

        var result = await _service.GetLatestOtherProposalTargetingCardAsync(
            Scope, _currentProposalId, _cardId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(applied.Id);
    }

    [Fact]
    public async Task GetLatestOtherProposal_ReturnsNull_WhenNoEffectiveOperationTargetsTheCard()
    {
        var related = PendingProposal("update", _cardId);
        var revision = Revision(related, 1, "update", _otherCardId);
        SetupHistoryPage(related);
        SetupRevisions((related, revision));

        var result = await _service.GetLatestOtherProposalTargetingCardAsync(
            Scope, _currentProposalId, _cardId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestOtherProposal_FallsBackToOriginalOperations_WhenNoRevisionExists()
    {
        var related = PendingProposal("update", _cardId);
        SetupHistoryPage(related);

        var result = await _service.GetLatestOtherProposalTargetingCardAsync(
            Scope, _currentProposalId, _cardId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(related.Id);
    }

    #endregion

    #region Similar past decisions

    [Fact]
    public async Task GetTerminalByEffectiveAction_MatchesTheApprovedPinAndDropsTheRejectedFalsePositive()
    {
        var applied = PendingProposal("move", _otherCardId);
        var pinned = Revision(applied, 1, "update", _cardId);
        applied.Approve(_userId, pinned.Id);
        applied.MarkAsApplied();

        // Raw rows say "update" -- the pre-fix action predicate's false positive. The
        // decision-time revision says "move", so this proposal is not in the cohort.
        var rejected = PendingProposal("update", _cardId);
        var rejectedRevision = Revision(rejected, 1, "move", _otherCardId);
        rejected.Reject(_userId, "Use another card");

        SetupTerminalPage(applied, rejected);
        SetupRevisions((applied, pinned), (rejected, rejectedRevision));

        var result = await _service.GetTerminalProposalsByEffectiveActionAsync(
            Scope, _currentProposalId, "update", 200);

        result.Should().ContainSingle().Which.Id.Should().Be(applied.Id);
    }

    [Fact]
    public async Task GetTerminalByEffectiveAction_ExcludesTheProposalUnderReview()
    {
        var current = PendingProposal("update", _cardId);
        current.Reject(_userId, "superseded");
        SetupTerminalPage(current);

        var result = await _service.GetTerminalProposalsByEffectiveActionAsync(
            Scope, current.Id, "update", 200);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTerminalByEffectiveAction_BoundsTheNumberOfDecisionsInspected()
    {
        // The lookback bounds decisions INSPECTED, not matches found: a limit of 150 must
        // read one full page plus a 50-row remainder and then stop.
        var page = Enumerable.Range(0, CandidatePageSize)
            .Select(_ => TerminalProposal("archive"))
            .ToList();
        var remainder = Enumerable.Range(0, 50)
            .Select(_ => TerminalProposal("archive"))
            .ToList();

        _candidates
            .Setup(store => store.ReadTerminalPageAsync(
                Scope, 0, CandidatePageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AutomationProposal>)page);
        _candidates
            .Setup(store => store.ReadTerminalPageAsync(
                Scope, CandidatePageSize, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AutomationProposal>)remainder);

        var result = await _service.GetTerminalProposalsByEffectiveActionAsync(
            Scope, _currentProposalId, "archive", 150);

        result.Should().HaveCount(150);
        _candidates.Verify(
            store => store.ReadTerminalPageAsync(
                Scope, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GetTerminalByEffectiveAction_ReturnsEmpty_ForABlankActionOrNonPositiveLimit()
    {
        (await _service.GetTerminalProposalsByEffectiveActionAsync(
            Scope, _currentProposalId, "   ", 200)).Should().BeEmpty();
        (await _service.GetTerminalProposalsByEffectiveActionAsync(
            Scope, _currentProposalId, "update", 0)).Should().BeEmpty();

        _candidates.Verify(
            store => store.ReadTerminalPageAsync(
                It.IsAny<ProposalEvidenceScope>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Argument validation

    [Fact]
    public async Task HasOtherPendingProposal_RejectsAnEmptyRequestingUser()
    {
        var act = () => _service.HasOtherPendingProposalTargetingCardAsync(
            new ProposalEvidenceScope(_boardId, Guid.Empty), _currentProposalId, _cardId);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetLatestOtherProposal_RejectsAnEmptyCardId()
    {
        var act = () => _service.GetLatestOtherProposalTargetingCardAsync(
            Scope, _currentProposalId, Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region Fixtures

    private void SetupPendingPage(params AutomationProposal[] proposals) =>
        _candidates
            .Setup(store => store.ReadPendingPageAsync(
                Scope, 0, CandidatePageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AutomationProposal>)proposals.ToList());

    private void SetupHistoryPage(params AutomationProposal[] proposals) =>
        _candidates
            .Setup(store => store.ReadHistoryPageAsync(
                Scope, 0, CandidatePageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AutomationProposal>)proposals.ToList());

    private void SetupTerminalPage(params AutomationProposal[] proposals) =>
        _candidates
            .Setup(store => store.ReadTerminalPageAsync(
                Scope, 0, CandidatePageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AutomationProposal>)proposals.ToList());

    private void SetupRevisions(params (AutomationProposal Proposal, ProposalRevision Revision)[] pairs)
    {
        var all = pairs.ToList();

        _revisions
            .Setup(repo => repo.GetRefsByProposalIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) =>
                (IReadOnlyList<ProposalRevisionRef>)all
                    .Where(pair => ids.Contains(pair.Proposal.Id))
                    .Select(pair => new ProposalRevisionRef(
                        pair.Revision.Id,
                        pair.Revision.ProposalId,
                        pair.Revision.RevisionNumber,
                        pair.Revision.RevisedAt))
                    .ToList());

        _revisions
            .Setup(repo => repo.GetByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) =>
                (IReadOnlyList<ProposalRevision>)all
                    .Select(pair => pair.Revision)
                    .Where(revision => ids.Contains(revision.Id))
                    .ToList());
    }

    private AutomationProposal PendingProposal(string actionType, Guid targetCardId)
    {
        var proposal = new AutomationProposal(
            ProposalSourceType.Manual,
            _userId,
            $"Proposal {actionType}",
            RiskLevel.Low,
            Guid.NewGuid().ToString("N"),
            _boardId);

        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            sequence: 0,
            actionType,
            targetType: "card",
            parameters: "{}",
            idempotencyKey: Guid.NewGuid().ToString("N"),
            targetId: targetCardId.ToString("D")));
        return proposal;
    }

    private AutomationProposal TerminalProposal(string actionType)
    {
        var proposal = PendingProposal(actionType, Guid.NewGuid());
        proposal.Reject(_userId, "historical decision");
        return proposal;
    }

    private ProposalRevision Revision(
        AutomationProposal proposal,
        int revisionNumber,
        string actionType,
        Guid targetCardId)
    {
        var payload = JsonSerializer.Serialize(new
        {
            operations = new[]
            {
                new
                {
                    sequence = 0,
                    actionType,
                    targetType = "card",
                    targetId = targetCardId.ToString("D"),
                    parameters = "{}",
                    idempotencyKey = Guid.NewGuid().ToString("N")
                }
            }
        });

        return new ProposalRevision(
            proposal.Id,
            revisionNumber,
            _userId,
            payload,
            $"revision {revisionNumber}");
    }

    #endregion
}
