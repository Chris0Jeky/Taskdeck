using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

/// <summary>
/// Tests for revision chain integrity: building ordered revision chains,
/// latest revision resolution, and edge cases.
/// </summary>
public class ProposalRevisionChainTests
{
    private readonly Guid _proposalId = Guid.NewGuid();
    private readonly Guid _editorUserId = Guid.NewGuid();

    [Fact]
    public void LatestRevision_NoRevisions_ReturnsNull()
    {
        // An empty revision list means the original proposal payload is authoritative.
        var revisions = new List<ProposalRevision>();

        var latest = revisions
            .OrderByDescending(r => r.RevisionNumber)
            .FirstOrDefault();

        latest.Should().BeNull();
    }

    [Fact]
    public void LatestRevision_SingleRevision_ReturnsThatRevision()
    {
        var revision = new ProposalRevision(
            _proposalId, 1, _editorUserId, "{\"v1\": true}", "first edit");

        var revisions = new List<ProposalRevision> { revision };

        var latest = revisions
            .OrderByDescending(r => r.RevisionNumber)
            .First();

        latest.Should().Be(revision);
        latest.RevisionNumber.Should().Be(1);
    }

    [Fact]
    public void LatestRevision_ManyRevisions_ReturnsHighestRevisionNumber()
    {
        var rev1 = new ProposalRevision(
            _proposalId, 1, _editorUserId, "{\"v1\": true}", "first edit");
        var rev2 = new ProposalRevision(
            _proposalId, 2, _editorUserId, "{\"v2\": true}", "second edit");
        var rev3 = new ProposalRevision(
            _proposalId, 3, _editorUserId, "{\"v3\": true}", "third edit");

        var revisions = new List<ProposalRevision> { rev1, rev3, rev2 }; // intentionally unordered

        var latest = revisions
            .OrderByDescending(r => r.RevisionNumber)
            .First();

        latest.Should().Be(rev3);
        latest.RevisionNumber.Should().Be(3);
        latest.RevisedPayload.Should().Be("{\"v3\": true}");
    }

    [Fact]
    public void RevisionChain_PreservesAllPriorPayloads()
    {
        var rev1 = new ProposalRevision(
            _proposalId, 1, _editorUserId, "{\"title\": \"Draft\"}", "initial draft");
        var rev2 = new ProposalRevision(
            _proposalId, 2, _editorUserId, "{\"title\": \"Final\"}", "finalized title");

        var revisions = new List<ProposalRevision> { rev1, rev2 };

        // Both revisions retain their payloads -- nothing is overwritten
        revisions[0].RevisedPayload.Should().Be("{\"title\": \"Draft\"}");
        revisions[1].RevisedPayload.Should().Be("{\"title\": \"Final\"}");
    }

    [Fact]
    public void RevisionChain_AllRevisionsReferenceTheSameProposal()
    {
        var rev1 = new ProposalRevision(
            _proposalId, 1, _editorUserId, "{}", "edit 1");
        var rev2 = new ProposalRevision(
            _proposalId, 2, _editorUserId, "{}", "edit 2");
        var rev3 = new ProposalRevision(
            _proposalId, 3, _editorUserId, "{}", "edit 3");

        var revisions = new List<ProposalRevision> { rev1, rev2, rev3 };

        revisions.Should().AllSatisfy(r => r.ProposalId.Should().Be(_proposalId));
    }

    [Fact]
    public void RevisionChain_DifferentEditors_TracksEditorPerRevision()
    {
        var editor1 = Guid.NewGuid();
        var editor2 = Guid.NewGuid();

        var rev1 = new ProposalRevision(
            _proposalId, 1, editor1, "{\"by\": \"alice\"}", "alice's edit");
        var rev2 = new ProposalRevision(
            _proposalId, 2, editor2, "{\"by\": \"bob\"}", "bob's edit");

        rev1.EditorUserId.Should().Be(editor1);
        rev2.EditorUserId.Should().Be(editor2);
    }

    [Fact]
    public void RevisionChain_OrderedByRevisionNumber_ReturnsChronologicalOrder()
    {
        var rev3 = new ProposalRevision(
            _proposalId, 3, _editorUserId, "{\"seq\": 3}", "third");
        var rev1 = new ProposalRevision(
            _proposalId, 1, _editorUserId, "{\"seq\": 1}", "first");
        var rev2 = new ProposalRevision(
            _proposalId, 2, _editorUserId, "{\"seq\": 2}", "second");

        var ordered = new List<ProposalRevision> { rev3, rev1, rev2 }
            .OrderBy(r => r.RevisionNumber)
            .ToList();

        ordered[0].RevisionNumber.Should().Be(1);
        ordered[1].RevisionNumber.Should().Be(2);
        ordered[2].RevisionNumber.Should().Be(3);
    }

    [Fact]
    public void RevisionChain_EachRevisionHasUniqueId()
    {
        var rev1 = new ProposalRevision(
            _proposalId, 1, _editorUserId, "{}", "edit 1");
        var rev2 = new ProposalRevision(
            _proposalId, 2, _editorUserId, "{}", "edit 2");

        rev1.Id.Should().NotBe(rev2.Id);
        rev1.Id.Should().NotBe(Guid.Empty);
        rev2.Id.Should().NotBe(Guid.Empty);
    }
}
