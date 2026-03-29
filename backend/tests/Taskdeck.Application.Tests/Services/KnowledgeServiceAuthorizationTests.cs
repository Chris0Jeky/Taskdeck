using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class KnowledgeServiceAuthorizationTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IKnowledgeSearchService> _searchServiceMock;
    private readonly Mock<IKnowledgeDocumentRepository> _documentRepoMock;
    private readonly Mock<IKnowledgeChunkRepository> _chunkRepoMock;
    private readonly KnowledgeService _service;

    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();

    public KnowledgeServiceAuthorizationTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _searchServiceMock = new Mock<IKnowledgeSearchService>();
        _documentRepoMock = new Mock<IKnowledgeDocumentRepository>();
        _chunkRepoMock = new Mock<IKnowledgeChunkRepository>();

        _unitOfWorkMock.SetupGet(u => u.KnowledgeDocuments).Returns(_documentRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.KnowledgeChunks).Returns(_chunkRepoMock.Object);

        _service = new KnowledgeService(_unitOfWorkMock.Object, _searchServiceMock.Object);
    }

    private KnowledgeDocument CreateOwnedDocument()
    {
        return new KnowledgeDocument(
            _ownerUserId,
            "Owner's Document",
            "Some content for the document.",
            KnowledgeSourceType.Manual);
    }

    [Fact]
    public async Task GetDocumentAsync_DifferentUser_ReturnsForbidden()
    {
        var document = CreateOwnedDocument();
        _documentRepoMock
            .Setup(r => r.GetByIdAsync(document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var result = await _service.GetDocumentAsync(_otherUserId, document.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("do not have access");
    }

    [Fact]
    public async Task UpdateDocumentAsync_DifferentUser_ReturnsForbidden()
    {
        var document = CreateOwnedDocument();
        _documentRepoMock
            .Setup(r => r.GetByIdAsync(document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var dto = new UpdateKnowledgeDocumentDto("New Title", "New content");
        var result = await _service.UpdateDocumentAsync(_otherUserId, document.Id, dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("do not have access");

        // Verify no save was attempted
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchiveDocumentAsync_DifferentUser_ReturnsForbidden()
    {
        var document = CreateOwnedDocument();
        _documentRepoMock
            .Setup(r => r.GetByIdAsync(document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var result = await _service.ArchiveDocumentAsync(_otherUserId, document.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("do not have access");

        // Verify no save was attempted
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetDocumentAsync_OwnerUser_ReturnsSuccess()
    {
        var document = CreateOwnedDocument();
        _documentRepoMock
            .Setup(r => r.GetByIdAsync(document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var result = await _service.GetDocumentAsync(_ownerUserId, document.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(document.Id);
        result.Value.Title.Should().Be("Owner's Document");
    }

    [Fact]
    public async Task GetDocumentAsync_EmptyUserId_ReturnsValidationError()
    {
        var result = await _service.GetDocumentAsync(Guid.Empty, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GetDocumentAsync_NonExistentDocument_ReturnsNotFound()
    {
        var documentId = Guid.NewGuid();
        _documentRepoMock
            .Setup(r => r.GetByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeDocument?)null);

        var result = await _service.GetDocumentAsync(_ownerUserId, documentId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }
}
