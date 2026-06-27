using System.Reflection;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmQueueServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAuthorizationService> _authorizationServiceMock;
    private readonly Mock<ILlmQueueRepository> _llmQueueRepoMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly LlmQueueService _service;

    public LlmQueueServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _authorizationServiceMock = new Mock<IAuthorizationService>();
        _llmQueueRepoMock = new Mock<ILlmQueueRepository>();
        _userRepoMock = new Mock<IUserRepository>();

        _unitOfWorkMock.Setup(u => u.LlmQueue).Returns(_llmQueueRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);

        _service = new LlmQueueService(_unitOfWorkMock.Object, _authorizationServiceMock.Object);
    }

    #region AddToQueueAsync Tests

    [Fact]
    public async Task AddToQueueAsync_ShouldReturnSuccess_WithValidData()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedpassword");
        var dto = new CreateLlmRequestDto("voicenote", "payload text", boardId);

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);
        _authorizationServiceMock.Setup(s => s.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _llmQueueRepoMock.Setup(r => r.AddAsync(It.IsAny<LlmRequest>(), default))
            .ReturnsAsync((LlmRequest r, CancellationToken ct) => r);

        // Act
        var result = await _service.AddToQueueAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(userId);
        result.Value.RequestType.Should().Be("voicenote");
        result.Value.Status.Should().Be(RequestStatus.Pending);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task AddToQueueAsync_ShouldNormalizeCapturePayload_WhenCaptureRequestTypeIsUsed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedpassword");
        var dto = new CreateLlmRequestDto(CaptureRequestContract.RequestTypeV1, "Capture this quick note", boardId);
        LlmRequest? persistedRequest = null;

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);
        _authorizationServiceMock.Setup(s => s.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _llmQueueRepoMock.Setup(r => r.AddAsync(It.IsAny<LlmRequest>(), default))
            .Callback<LlmRequest, CancellationToken>((request, _) => persistedRequest = request)
            .ReturnsAsync((LlmRequest request, CancellationToken _) => request);

        // Act
        var result = await _service.AddToQueueAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        persistedRequest.Should().NotBeNull();
        persistedRequest!.RequestType.Should().Be(CaptureRequestContract.RequestTypeV1);
        var payloadResult = CaptureRequestContract.ParsePayload(persistedRequest.Payload);
        payloadResult.IsSuccess.Should().BeTrue();
        payloadResult.Value.Text.Should().Be("Capture this quick note");
        payloadResult.Value.Source.Should().Be(Domain.Enums.CaptureSource.Typed);
    }

    [Fact]
    public async Task AddToQueueAsync_ShouldReturnValidationError_WhenCapturePayloadContainsSpoofedProvenanceAttribution()
    {
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedpassword");
        var dto = new CreateLlmRequestDto(
            CaptureRequestContract.RequestTypeV1,
            $$"""
              {
                "version": 1,
                "text": "Capture this quick note",
                "provenance": {
                  "captureItemId": "{{Guid.NewGuid()}}",
                  "requestedByUserId": "{{Guid.NewGuid()}}"
                }
              }
              """);

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);

        var result = await _service.AddToQueueAsync(userId, dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("must not include server attribution field");
        _llmQueueRepoMock.Verify(r => r.AddAsync(It.IsAny<LlmRequest>(), default), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task AddToQueueAsync_ShouldReturnValidationError_WhenCaptureRequestTypeIsUnsupported()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedpassword");
        var dto = new CreateLlmRequestDto("inbox.capture.voice.v2", "payload");

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);

        // Act
        var result = await _service.AddToQueueAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported capture request type");
        _llmQueueRepoMock.Verify(r => r.AddAsync(It.IsAny<LlmRequest>(), default), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task AddToQueueAsync_ShouldReturnValidationError_WhenCaptureTextExceedsMaxLength()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedpassword");
        var longText = new string('x', CaptureRequestContract.MaxRawTextLength + 1);
        var dto = new CreateLlmRequestDto(CaptureRequestContract.RequestTypeV1, longText);

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);

        // Act
        var result = await _service.AddToQueueAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("cannot exceed");
        _llmQueueRepoMock.Verify(r => r.AddAsync(It.IsAny<LlmRequest>(), default), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task AddToQueueAsync_ShouldReturnForbidden_WhenUserCannotAccessBoard()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedpassword");
        var dto = new CreateLlmRequestDto("voicenote", "payload text", boardId);

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);
        _authorizationServiceMock.Setup(s => s.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));

        // Act
        var result = await _service.AddToQueueAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("access");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task AddToQueueAsync_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var user = new User("testuser", "test@example.com", "hashedpassword");
        var dto = new CreateLlmRequestDto("voicenote", "payload text", boardId);

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(user);
        _authorizationServiceMock.Setup(s => s.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Failure<bool>(ErrorCodes.NotFound, $"Board with ID {boardId} not found"));

        // Act
        var result = await _service.AddToQueueAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task AddToQueueAsync_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var dto = new CreateLlmRequestDto("voicenote", "payload text");

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _service.AddToQueueAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("User");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region GetUserQueueAsync Tests

    [Fact]
    public async Task GetUserQueueAsync_ShouldReturnRequests()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var requests = new List<LlmRequest>
        {
            new LlmRequest(userId, "voicenote", "payload text", boardId),
            new LlmRequest(userId, "transcript", "another payload", boardId)
        };

        _llmQueueRepoMock.Setup(r => r.GetByUserAsync(userId, default))
            .ReturnsAsync(requests);

        // Act
        var result = await _service.GetUserQueueAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    #endregion

    #region GetQueueByStatusAsync Tests

    [Fact]
    public async Task GetQueueByStatusAsync_ShouldReturnRequests()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requests = new List<LlmRequest>
        {
            new LlmRequest(userId, "voicenote", "payload text")
        };

        _llmQueueRepoMock.Setup(r => r.GetByUserAndStatusAsync(userId, RequestStatus.Pending, default))
            .ReturnsAsync(requests);

        // Act
        var result = await _service.GetQueueByStatusAsync(userId, RequestStatus.Pending);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    #endregion

    #region CancelRequestAsync Tests

    [Fact]
    public async Task CancelRequestAsync_ShouldReturnSuccess_WhenUserOwnsRequest()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new LlmRequest(userId, "voicenote", "payload text");

        _llmQueueRepoMock.Setup(r => r.GetByIdAsync(request.Id, default))
            .ReturnsAsync(request);

        // Act
        var result = await _service.CancelRequestAsync(request.Id, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CancelRequestAsync_ShouldReturnForbidden_WhenUserDoesNotOwnRequest()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var request = new LlmRequest(ownerId, "voicenote", "payload text");

        _llmQueueRepoMock.Setup(r => r.GetByIdAsync(request.Id, default))
            .ReturnsAsync(request);

        // Act
        var result = await _service.CancelRequestAsync(request.Id, otherUserId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task CancelRequestAsync_ShouldSucceed_WhenSandboxModeIsEnabled()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var request = new LlmRequest(ownerId, "voicenote", "payload text");
        var sandboxService = new LlmQueueService(
            _unitOfWorkMock.Object,
            _authorizationServiceMock.Object,
            new DevelopmentSandboxSettings { Enabled = true });

        _llmQueueRepoMock.Setup(r => r.GetByIdAsync(request.Id, default))
            .ReturnsAsync(request);

        var result = await sandboxService.CancelRequestAsync(request.Id, otherUserId);

        result.IsSuccess.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    #endregion

    #region ProcessNextRequestAsync Tests

    [Fact]
    public async Task ProcessNextRequestAsync_ShouldReturnSuccess_WhenClaimSucceeds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new LlmRequest(userId, "voicenote", "payload text");
        var pendingUpdatedAt = request.UpdatedAt;

        // The repository now returns the bounded, oldest-first, NON-capture candidates at the
        // database (#1237); the service just claims the first claimable one.
        _llmQueueRepoMock.Setup(r => r.GetOldestPendingNonCaptureAsync(It.IsAny<int>(), default))
            .ReturnsAsync(new[] { request });

        // Per the ILlmQueueRepository contract, a successful claim refreshes the
        // in-memory entity so it reflects the Processing state written to the database.
        _llmQueueRepoMock.Setup(r => r.TryClaimProcessingAsync(request.Id, pendingUpdatedAt, default))
            .Callback(() => request.MarkAsProcessing())
            .ReturnsAsync(true);

        // Act
        var result = await _service.ProcessNextRequestAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RequestStatus.Processing);
        _llmQueueRepoMock.Verify(r => r.TryClaimProcessingAsync(request.Id, pendingUpdatedAt, default), Times.Once);
        // Bounded, type-aware read -- never the unbounded full-backlog GetByStatusAsync.
        _llmQueueRepoMock.Verify(r => r.GetOldestPendingNonCaptureAsync(It.IsAny<int>(), default), Times.Once);
        _llmQueueRepoMock.Verify(r => r.GetByStatusAsync(It.IsAny<RequestStatus>(), default), Times.Never);
    }

    [Fact]
    public async Task ProcessNextRequestAsync_ShouldReturnNotFound_WhenQueueIsEmpty()
    {
        // Arrange
        _llmQueueRepoMock.Setup(r => r.GetOldestPendingNonCaptureAsync(It.IsAny<int>(), default))
            .ReturnsAsync(Array.Empty<LlmRequest>());

        // Act
        var result = await _service.ProcessNextRequestAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ProcessNextRequestAsync_UsesNonCapturePrimitive_SoCaptureRequestsAreNeverClaimed()
    {
        // Capture-skipping now lives IN the query: the service calls GetOldestPendingNonCaptureAsync,
        // which excludes capture rows at the database (#1236/#1237). When only capture work is pending,
        // that bounded read returns empty, so nothing is claimed -- and GetByStatusAsync is never used.
        _llmQueueRepoMock.Setup(r => r.GetOldestPendingNonCaptureAsync(It.IsAny<int>(), default))
            .ReturnsAsync(Array.Empty<LlmRequest>());

        var result = await _service.ProcessNextRequestAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _llmQueueRepoMock.Verify(r => r.TryClaimProcessingAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), default), Times.Never);
        _llmQueueRepoMock.Verify(r => r.GetOldestPendingNonCaptureAsync(It.IsAny<int>(), default), Times.Once);
        _llmQueueRepoMock.Verify(r => r.GetByStatusAsync(It.IsAny<RequestStatus>(), default), Times.Never);
    }

    [Fact]
    public async Task ProcessNextRequestAsync_ShouldClaimOldest_InTheOrderReturned()
    {
        // The repository returns non-capture candidates oldest-first; the service claims the first claimable.
        var userId = Guid.NewGuid();
        var oldestNonCapture = new LlmRequest(userId, "summarize", "oldest non-capture");
        var newestNonCapture = new LlmRequest(userId, "summarize", "newest non-capture");
        var baseTime = DateTimeOffset.UtcNow;
        SetCreatedAt(oldestNonCapture, baseTime);
        SetCreatedAt(newestNonCapture, baseTime.AddMilliseconds(2));

        _llmQueueRepoMock.Setup(r => r.GetOldestPendingNonCaptureAsync(It.IsAny<int>(), default))
            .ReturnsAsync(new[] { oldestNonCapture, newestNonCapture });
        _llmQueueRepoMock.Setup(r => r.TryClaimProcessingAsync(oldestNonCapture.Id, oldestNonCapture.UpdatedAt, default))
            .Callback(() => oldestNonCapture.MarkAsProcessing())
            .ReturnsAsync(true);

        var result = await _service.ProcessNextRequestAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(oldestNonCapture.Id);
        result.Value.Status.Should().Be(RequestStatus.Processing);
    }

    [Fact]
    public async Task ProcessNextRequestAsync_ShouldReturnNotFound_WhenAllClaimsFail()
    {
        // Arrange -- simulates concurrent claim: another worker claimed the item first
        var userId = Guid.NewGuid();
        var request = new LlmRequest(userId, "voicenote", "payload text");

        _llmQueueRepoMock.Setup(r => r.GetOldestPendingNonCaptureAsync(It.IsAny<int>(), default))
            .ReturnsAsync(new[] { request });
        _llmQueueRepoMock.Setup(r => r.TryClaimProcessingAsync(request.Id, request.UpdatedAt, default))
            .ReturnsAsync(false);

        // Act
        var result = await _service.ProcessNextRequestAsync();

        // Assert -- all candidates failed to claim, so NotFound
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ProcessNextRequestAsync_ShouldTryNextCandidate_WhenFirstClaimFails()
    {
        // Arrange -- first candidate already claimed, second succeeds
        var userId = Guid.NewGuid();
        var first = new LlmRequest(userId, "summarize", "first payload");
        var second = new LlmRequest(userId, "summarize", "second payload");
        var baseTime = DateTimeOffset.UtcNow;
        SetCreatedAt(first, baseTime);
        SetCreatedAt(second, baseTime.AddMilliseconds(1));

        _llmQueueRepoMock.Setup(r => r.GetOldestPendingNonCaptureAsync(It.IsAny<int>(), default))
            .ReturnsAsync(new[] { first, second });
        _llmQueueRepoMock.Setup(r => r.TryClaimProcessingAsync(first.Id, first.UpdatedAt, default))
            .ReturnsAsync(false);
        _llmQueueRepoMock.Setup(r => r.TryClaimProcessingAsync(second.Id, second.UpdatedAt, default))
            .Callback(() => second.MarkAsProcessing())
            .ReturnsAsync(true);

        // Act
        var result = await _service.ProcessNextRequestAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(second.Id);
        result.Value.Status.Should().Be(RequestStatus.Processing);
    }

    #endregion

    #region GetQueueStatsAsync Tests

    [Fact]
    public async Task GetQueueStatsAsync_ShouldReturnCorrectCounts()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var pendingRequests = new List<LlmRequest>
        {
            new LlmRequest(userId, "voicenote", "payload1"),
            new LlmRequest(userId, "voicenote", "payload2")
        };
        var processingRequests = new List<LlmRequest>
        {
            new LlmRequest(userId, "voicenote", "payload3")
        };
        var completedRequests = new List<LlmRequest>();
        var failedRequests = new List<LlmRequest>();

        _llmQueueRepoMock.Setup(r => r.GetStatusCountsByUserAsync(userId, default))
            .ReturnsAsync(new Dictionary<RequestStatus, int>
            {
                { RequestStatus.Pending, pendingRequests.Count },
                { RequestStatus.Processing, processingRequests.Count },
                { RequestStatus.Completed, completedRequests.Count },
                { RequestStatus.Failed, failedRequests.Count },
            });

        // Act
        var result = await _service.GetQueueStatsAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PendingCount.Should().Be(2);
        result.Value.ProcessingCount.Should().Be(1);
        result.Value.CompletedCount.Should().Be(0);
        result.Value.FailedCount.Should().Be(0);
    }

    #endregion

    private static readonly PropertyInfo CreatedAtProperty =
        typeof(Entity).GetProperty(nameof(Entity.CreatedAt))
        ?? throw new InvalidOperationException("Expected Entity.CreatedAt property to exist.");

    private static readonly MethodInfo CreatedAtSetter =
        CreatedAtProperty.GetSetMethod(true)
        ?? throw new InvalidOperationException("Expected Entity.CreatedAt setter to exist.");

    private static void SetCreatedAt(LlmRequest request, DateTimeOffset createdAt)
    {
        CreatedAtSetter.Invoke(request, new object[] { createdAt });
    }
}
