using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// The durable, user-owned Inbox object of the Context Fabric (ADR-0065 §Decision 1; CF-01
/// <c>#2255</c>). A capture is valid as soon as its source material is stored and stays readable
/// through every processing failure; jobs, runs and representations hang off it and never replace
/// it. Until CF-01 completes the ID-preserving backfill, rows are created only as mirrors of the
/// legacy <see cref="LlmRequest"/> capture row (same <see cref="Entity.Id"/>), behind the
/// <c>ContextFabric:DualWriteCaptures</c> flag; the queue row remains the source of truth for Inbox
/// reads. Supersedes the ADR-0005 queue-wrapper model when that slice lands.
/// </summary>
public sealed class Capture : Entity
{
    public const int MaxUserTitleLength = 240;
    public const int MaxUserNoteLength = 2_000;

    public Guid UserId { get; private set; }

    /// <summary>Server clock at intake; the authoritative capture time.</summary>
    public DateTimeOffset CapturedAtServer { get; private set; }

    /// <summary>Client-reported time, kept only as a hint (offline queues replay late).</summary>
    public DateTimeOffset? CapturedAtClient { get; private set; }

    public CaptureModality PrimaryModality { get; private set; }
    public CaptureOriginAdapter OriginAdapter { get; private set; }
    public CaptureProducerKind Producer { get; private set; }
    public CaptureIntentMode Intent { get; private set; }
    public CaptureLifecycleState Lifecycle { get; private set; }

    /// <summary>Derived compatibility field for legacy readers (ADR-0065 §Decision 2).</summary>
    public CaptureSource LegacySource { get; private set; }

    /// <summary>
    /// Optional explicit context hint (a board today; a project after ADR-0060 stage 4). Never
    /// required: context resolution happens at change-planning time (ADR-0065 §Decision 12).
    /// </summary>
    public Guid? ContextBoardId { get; private set; }

    public string? UserTitle { get; private set; }
    public string? UserNote { get; private set; }

    /// <summary>
    /// The legacy queue row this capture mirrors or was backfilled from. Equal to <see cref="Entity.Id"/>
    /// for ID-preserving rows; null for captures created natively after the CF-01 read switch.
    /// </summary>
    public Guid? LegacyRequestId { get; private set; }

    private Capture() : base()
    {
    }

    public Capture(
        Guid id,
        Guid userId,
        CaptureModality primaryModality,
        CaptureOriginAdapter originAdapter,
        CaptureProducerKind producer,
        CaptureIntentMode intent,
        CaptureSource legacySource,
        Guid? contextBoardId = null,
        DateTimeOffset? capturedAtClient = null,
        string? userTitle = null,
        string? userNote = null,
        Guid? legacyRequestId = null,
        DateTimeOffset? capturedAtServer = null)
        : base(id)
    {
        if (id == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Capture ID cannot be empty");
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");
        if (!Enum.IsDefined(primaryModality))
            throw new DomainException(ErrorCodes.ValidationError, "Capture modality is invalid");
        if (!Enum.IsDefined(originAdapter))
            throw new DomainException(ErrorCodes.ValidationError, "Capture origin adapter is invalid");
        if (!Enum.IsDefined(producer))
            throw new DomainException(ErrorCodes.ValidationError, "Capture producer is invalid");
        if (!Enum.IsDefined(intent))
            throw new DomainException(ErrorCodes.ValidationError, "Capture intent is invalid");
        if (!Enum.IsDefined(legacySource))
            throw new DomainException(ErrorCodes.ValidationError, "Capture source is invalid");
        if (contextBoardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Board ID cannot be empty");
        if (legacyRequestId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Legacy request ID cannot be empty");

        UserId = userId;
        if (capturedAtServer.HasValue)
        {
            // A mirror or backfill of an existing queue row keeps that row's intake time as its own
            // creation time, so chronological Inbox order survives the read switch.
            CreatedAt = capturedAtServer.Value;
            UpdatedAt = capturedAtServer.Value;
        }

        CapturedAtServer = CreatedAt;
        CapturedAtClient = capturedAtClient;
        PrimaryModality = primaryModality;
        OriginAdapter = originAdapter;
        Producer = producer;
        Intent = intent;
        Lifecycle = CaptureLifecycleState.Received;
        LegacySource = legacySource;
        ContextBoardId = contextBoardId;
        UserTitle = NormalizeBounded(userTitle, MaxUserTitleLength, "Capture title", singleLine: true);
        UserNote = NormalizeBounded(userNote, MaxUserNoteLength, "Capture note", singleLine: false);
        LegacyRequestId = legacyRequestId;
    }

    /// <summary>
    /// Builds the ID-preserving mirror of a legacy queue capture row: the capture takes the queue
    /// row's id so every existing <c>CreatedFromCaptureId</c> / <c>CaptureItemId</c> reference keeps
    /// resolving after the CF-01 read switch. Dimensions come from <see cref="CaptureSourceMapping"/>;
    /// the producer defaults to the mapping's value and may be overridden by the caller that knows
    /// the authenticated principal kind.
    /// </summary>
    public static Capture FromQueueRequest(
        Guid requestId,
        Guid userId,
        CaptureSource source,
        Guid? contextBoardId,
        DateTimeOffset? capturedAtClient,
        string? userTitle,
        CaptureIntentMode intent = CaptureIntentMode.Organize,
        CaptureProducerKind? producerOverride = null,
        DateTimeOffset? capturedAtServer = null)
    {
        var dimensions = CaptureSourceMapping.Resolve(source);

        return new Capture(
            requestId,
            userId,
            dimensions.Modality,
            dimensions.Origin,
            producerOverride ?? dimensions.Producer,
            intent,
            source,
            contextBoardId,
            capturedAtClient,
            userTitle,
            userNote: null,
            legacyRequestId: requestId,
            capturedAtServer: capturedAtServer);
    }

    public void TransitionTo(CaptureLifecycleState next)
    {
        if (!Enum.IsDefined(next))
            throw new DomainException(ErrorCodes.ValidationError, "Capture lifecycle state is invalid");

        if (!CaptureLifecyclePolicy.CanTransition(Lifecycle, next))
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Capture cannot move from {Lifecycle} to {next}");
        }

        if (Lifecycle == next)
            return;

        Lifecycle = next;
        Touch();
    }

    public void SetIntent(CaptureIntentMode intent)
    {
        if (!Enum.IsDefined(intent))
            throw new DomainException(ErrorCodes.ValidationError, "Capture intent is invalid");

        if (Intent == intent)
            return;

        Intent = intent;
        Touch();
    }

    /// <summary>
    /// Records or clears the explicit context hint. A null hint is a valid state, never an error:
    /// resolution happens later (ADR-0065 §Decision 12).
    /// </summary>
    public void SetContextBoard(Guid? boardId)
    {
        if (boardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Board ID cannot be empty");

        if (ContextBoardId == boardId)
            return;

        ContextBoardId = boardId;
        Touch();
    }

    public void Retitle(string? userTitle)
    {
        var normalized = NormalizeBounded(userTitle, MaxUserTitleLength, "Capture title", singleLine: true);
        if (string.Equals(UserTitle, normalized, StringComparison.Ordinal))
            return;

        UserTitle = normalized;
        Touch();
    }

    /// <summary>
    /// Trims, bounds and sanitises free text. Control characters are replaced by spaces rather than
    /// rejected: the legacy capture contract accepts them in a title hint, so a mirrored row must
    /// never fail where the queue row succeeds (the dual-write must be behaviour-preserving in both
    /// flag states). A single-line field (the title) also flattens LF and TAB; a note keeps them.
    /// Length is still enforced, matching the legacy cap.
    /// </summary>
    private static string? NormalizeBounded(string? value, int maxLength, string label, bool singleLine)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var sanitized = string.Create(value.Length, (value, singleLine), static (span, state) =>
        {
            var (source, flatten) = state;
            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];
                var keep = !char.IsControl(character) || (!flatten && (character == '\n' || character == '\t'));
                span[index] = keep ? character : ' ';
            }
        });

        var trimmed = sanitized.Trim();
        if (trimmed.Length == 0)
            return null;

        if (trimmed.Length > maxLength)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"{label} cannot exceed {maxLength} characters");
        }

        return trimmed;
    }
}
