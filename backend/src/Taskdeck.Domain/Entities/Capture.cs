using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// The durable, user-owned Inbox object of the Context Fabric (ADR-0065 §Decision 1; CF-01
/// <c>#2255</c>). A capture is valid as soon as its source assets are stored and stays readable
/// through every processing failure; jobs, runs, representations and candidates hang off it and
/// never replace it. Its state is three orthogonal axes — the user's <see cref="Disposition"/>,
/// the <see cref="ProcessingSummary"/> projected from jobs, and the <see cref="ActionState"/>
/// projected from planning records — with <see cref="Timeline"/> as the one-line projection the UI
/// shows (amended 2026-08-30 after the external audit: one lifecycle enum could not represent a
/// kept, partially processed, already-acted capture without losing information).
/// Until CF-01 completes the ID-preserving backfill, rows are created only as mirrors of the legacy
/// <see cref="LlmRequest"/> capture row (same <see cref="Entity.Id"/>), behind the
/// <c>ContextFabric:DualWriteCaptures</c> flag; the queue row remains the source of truth for Inbox
/// reads. Supersedes the ADR-0005 queue-wrapper model when that slice lands.
/// </summary>
public sealed class Capture : Entity
{
    public const int MaxUserTitleLength = 240;
    public const int MaxUserNoteLength = 2_000;
    public const int MaxSourceAssets = 32;

    private readonly List<SourceAsset> _sourceAssets = new();

    /// <summary>The owning principal (a user today). Ownership is never the same question as who produced the capture.</summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// The principal that produced the capture when it is not the owner — an agent profile, an
    /// integration connector, a service account. Null means the owner produced it. Server-stamped;
    /// never client-supplied (GP-02).
    /// </summary>
    public Guid? ProducedByPrincipalId { get; private set; }

    public CaptureProducerKind ProducerKind { get; private set; }

    /// <summary>Server clock at intake; the authoritative capture time.</summary>
    public DateTimeOffset CapturedAtServer { get; private set; }

    /// <summary>Client-reported time, kept only as a hint (offline queues replay late).</summary>
    public DateTimeOffset? CapturedAtClient { get; private set; }

    /// <summary>
    /// Summary of the first asset's modality for lists and compatibility readers. Routing operates
    /// per <see cref="SourceAsset"/>; this field never selects a processor.
    /// </summary>
    public CaptureModality PrimaryModality { get; private set; }

    public CaptureOriginAdapter OriginAdapter { get; private set; }

    /// <summary>What the user asked for; may be <see cref="CaptureIntentMode.Auto"/>.</summary>
    public CaptureIntentMode RequestedIntent { get; private set; }

    /// <summary>
    /// The intent processing acts under; never <see cref="CaptureIntentMode.Auto"/>. Equal to
    /// <see cref="RequestedIntent"/> for an explicit request; null until a run resolves an
    /// <see cref="CaptureIntentMode.Auto"/> request.
    /// </summary>
    public CaptureIntentMode? EffectiveIntent { get; private set; }

    /// <summary>The processing run that inferred <see cref="EffectiveIntent"/> from an <see cref="CaptureIntentMode.Auto"/> request (CF-03); null when the user chose explicitly.</summary>
    public Guid? IntentResolvedByRunId { get; private set; }

    public CaptureUserDisposition Disposition { get; private set; }
    public CaptureProcessingSummary ProcessingSummary { get; private set; }
    public CaptureActionState ActionState { get; private set; }

    /// <summary>The user-legible step, projected from the three axes; never stored as the only truth.</summary>
    public CaptureTimelineStep Timeline => CaptureTimeline.Project(Disposition, ProcessingSummary, ActionState);

    /// <summary>
    /// Compatibility snapshot of the legacy <see cref="CaptureSource"/> taken at intake for readers
    /// of the queue-row contract (ADR-0065 §Decision 2). A snapshot, not a derived value: it is
    /// persisted so a mirrored row reads back exactly what its queue row said. Native captures
    /// after the read switch take it from <see cref="CaptureSourceMapping.ToLegacySource"/>.
    /// </summary>
    public CaptureSource LegacySourceSnapshot { get; private set; }

    /// <summary>
    /// Optional explicit context hint (a board today; a project after ADR-0060 stage 4). Never
    /// required: context resolution happens at change-planning time (ADR-0065 §Decision 12).
    /// </summary>
    public Guid? ContextBoardId { get; private set; }

    public string? UserTitle { get; private set; }

    /// <summary>A short user annotation about the capture. The captured material itself is a <see cref="SourceAsset"/>, never this field.</summary>
    public string? UserNote { get; private set; }

    /// <summary>
    /// The legacy queue row this capture mirrors or was backfilled from. Equal to <see cref="Entity.Id"/>
    /// for ID-preserving rows; null for captures created natively after the CF-01 read switch.
    /// </summary>
    public Guid? LegacyRequestId { get; private set; }

    /// <summary>
    /// The immutable inputs, in <see cref="SourceAsset.Ordinal"/> order. Sorted rather than returned
    /// raw because the backing collection is filled by the persistence layer, which makes no row
    /// order guarantee: ordinal is the aggregate own order and every reader depends on it.
    /// </summary>
    public IReadOnlyList<SourceAsset> SourceAssets => Ordered.ToList();

    /// <summary>
    /// The inputs as they stand now: every asset that nothing has superseded. A post-intake edit
    /// appends a corrected asset rather than rewriting one, so the intake record stays in
    /// <see cref="SourceAssets"/> while readers of "the current text" use this view.
    /// </summary>
    public IReadOnlyList<SourceAsset> ActiveSourceAssets =>
        Ordered.Where(asset => asset.IsActive).ToList();

    private IEnumerable<SourceAsset> Ordered => _sourceAssets.OrderBy(asset => asset.Ordinal);

    private Capture() : base()
    {
    }

    public Capture(
        Guid id,
        Guid userId,
        CaptureModality primaryModality,
        CaptureOriginAdapter originAdapter,
        CaptureProducerKind producerKind,
        CaptureIntentMode requestedIntent,
        CaptureSource legacySourceSnapshot,
        Guid? contextBoardId = null,
        DateTimeOffset? capturedAtClient = null,
        string? userTitle = null,
        string? userNote = null,
        Guid? legacyRequestId = null,
        DateTimeOffset? capturedAtServer = null,
        Guid? producedByPrincipalId = null)
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
        if (!Enum.IsDefined(producerKind))
            throw new DomainException(ErrorCodes.ValidationError, "Capture producer is invalid");
        if (!Enum.IsDefined(requestedIntent))
            throw new DomainException(ErrorCodes.ValidationError, "Capture intent is invalid");
        if (!Enum.IsDefined(legacySourceSnapshot))
            throw new DomainException(ErrorCodes.ValidationError, "Capture source is invalid");
        if (contextBoardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Board ID cannot be empty");
        if (legacyRequestId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Legacy request ID cannot be empty");
        if (producedByPrincipalId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Producer principal ID cannot be empty");

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
        ProducerKind = producerKind;
        ProducedByPrincipalId = producedByPrincipalId;
        RequestedIntent = requestedIntent;
        EffectiveIntent = requestedIntent == CaptureIntentMode.Auto ? null : requestedIntent;
        Disposition = CaptureUserDisposition.Active;
        ProcessingSummary = CaptureProcessingSummary.Idle;
        ActionState = CaptureActionState.Unplanned;
        LegacySourceSnapshot = legacySourceSnapshot;
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
    /// the authenticated principal kind. A legacy disposition, when the row carries one, maps onto
    /// <see cref="Disposition"/> through <see cref="CaptureUserDispositionMapping"/>.
    /// </summary>
    public static Capture FromQueueRequest(
        Guid requestId,
        Guid userId,
        CaptureSource source,
        Guid? contextBoardId,
        DateTimeOffset? capturedAtClient,
        string? userTitle,
        CaptureIntentMode requestedIntent = CaptureIntentMode.Organize,
        CaptureProducerKind? producerOverride = null,
        DateTimeOffset? capturedAtServer = null,
        CaptureDisposition? legacyDisposition = null,
        Guid? producedByPrincipalId = null,
        string? sourceText = null,
        string? externalReference = null,
        CaptureProcessingSummary? processingSummary = null,
        CaptureActionState? actionState = null,
        CaptureUserDisposition? userDisposition = null)
    {
        var dimensions = CaptureSourceMapping.Resolve(source);

        var capture = new Capture(
            requestId,
            userId,
            dimensions.Modality,
            dimensions.Origin,
            producerOverride ?? dimensions.Producer,
            requestedIntent,
            source,
            contextBoardId,
            capturedAtClient,
            userTitle,
            userNote: null,
            legacyRequestId: requestId,
            capturedAtServer: capturedAtServer,
            producedByPrincipalId: producedByPrincipalId);

        // Assets and the machine-derived axes are seeded BEFORE the user's disposition, because an
        // archived capture rejects both (archived is terminal). A legacy row that was cancelled or
        // put away still has to arrive with its source material and its outcomes intact -- archiving
        // is a decision about the Inbox, never an erasure of what was captured or produced (#2255).
        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            capture.AddInlineTextSource(sourceText);
        }

        if (!string.IsNullOrWhiteSpace(externalReference))
        {
            capture.AddExternalReferenceSource(externalReference);
        }

        if (processingSummary.HasValue)
        {
            capture.RecordProcessingSummary(processingSummary.Value);
        }

        if (actionState.HasValue)
        {
            capture.RecordActionState(actionState.Value);
        }

        // The explicit axis wins when the caller resolved one (the backfill derives it from the queue
        // row's status as well as its recorded disposition); otherwise the recorded disposition maps.
        if (userDisposition.HasValue)
        {
            if (!Enum.IsDefined(userDisposition.Value))
                throw new DomainException(ErrorCodes.ValidationError, "Capture disposition is invalid");

            capture.Disposition = userDisposition.Value;
        }
        else if (legacyDisposition.HasValue)
        {
            capture.Disposition = CaptureUserDispositionMapping.FromLegacy(legacyDisposition.Value);
        }

        return capture;
    }

    /// <summary>Appends typed or pasted text as the next immutable asset.</summary>
    public SourceAsset AddInlineTextSource(string text, string mediaType = SourceAsset.PlainTextMediaType, string? originalName = null)
    {
        var asset = SourceAsset.FromInlineText(Id, _sourceAssets.Count, text, mediaType, originalName);
        AddSourceAsset(asset);
        return asset;
    }

    /// <summary>Appends a locator the user supplied (a URL, a share-target reference) as the next immutable asset.</summary>
    public SourceAsset AddExternalReferenceSource(string reference, string? originalName = null)
    {
        var asset = SourceAsset.FromExternalReference(Id, _sourceAssets.Count, reference, originalName);
        AddSourceAsset(asset);
        return asset;
    }

    /// <summary>
    /// Records a post-intake correction of the capture's text (CF-01 <c>#2255</c>, late review
    /// finding on <c>#2320</c>). Sources are immutable, so the edit is a <b>new</b> inline text
    /// asset that supersedes the current one — never an in-place rewrite: what the user originally
    /// typed, pasted or dictated stays readable, and every derived representation can still name the
    /// exact asset it was built from. When the capture carries no active text asset yet the
    /// correction is simply the first one.
    /// </summary>
    public SourceAsset SupersedeInlineTextSource(
        string text,
        string mediaType = SourceAsset.PlainTextMediaType,
        string? originalName = null)
    {
        EnsureNotArchived("edit the source of");

        var current = Ordered
            .LastOrDefault(asset => asset.IsActive && asset.StorageKind == SourceAssetStorageKind.InlineText);

        // Constructed first: a rejected correction (blank, over the cap) throws here, before the
        // capture is touched at all, so a failed edit never leaves it without a current source.
        var replacement = SourceAsset.FromInlineText(Id, _sourceAssets.Count, text, mediaType, originalName);
        if (current is not null)
        {
            replacement.RecordSupersedes(current.Id);
            current.MarkSupersededBy(replacement.Id);
        }

        AddSourceAsset(replacement);
        return replacement;
    }

    /// <summary>
    /// The text of the capture as it stands now: the newest inline text asset nothing has
    /// superseded. Null when the capture holds no inline text (a voice note before transcription,
    /// a bare external reference).
    /// </summary>
    public string? CurrentText =>
        Ordered
            .LastOrDefault(asset => asset.IsActive && asset.StorageKind == SourceAssetStorageKind.InlineText)
            ?.TextPayload?.Text;

    /// <summary>
    /// Appends the next immutable asset. The first asset stored decides <see cref="PrimaryModality"/>
    /// (a summary for lists — the constructor's value is only the mapping's guess until an asset
    /// exists); later assets never change it, and routing reads each asset's own modality.
    /// </summary>
    public void AddSourceAsset(SourceAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        EnsureNotArchived("add a source to");

        if (asset.CaptureId != Id)
            throw new DomainException(ErrorCodes.ValidationError, "Source asset belongs to a different capture");
        if (asset.Ordinal != _sourceAssets.Count)
            throw new DomainException(ErrorCodes.ValidationError, $"Source asset ordinal must be {_sourceAssets.Count}");
        // The cap bounds how many inputs a capture HAS, not how long its correction history is: a
        // superseded asset is history, so an edit is net-zero against the limit and a capture the
        // shipped contract lets a user edit can never be rejected here.
        if (_sourceAssets.Count(existing => existing.IsActive) >= MaxSourceAssets)
            throw new DomainException(ErrorCodes.ValidationError, $"A capture cannot hold more than {MaxSourceAssets} active source assets");

        if (_sourceAssets.Count == 0)
        {
            PrimaryModality = asset.Modality;
        }

        _sourceAssets.Add(asset);
        Touch();
    }

    /// <summary>Preserve as a record (<see cref="CaptureIntentMode.Remember"/>); processing may still run later.</summary>
    public void Keep()
    {
        EnsureNotArchived("keep");
        if (Disposition == CaptureUserDisposition.Kept)
            return;

        Disposition = CaptureUserDisposition.Kept;
        Touch();
    }

    /// <summary>Put away; terminal. Existing processing and action outcomes are not erased.</summary>
    public void Archive()
    {
        if (Disposition == CaptureUserDisposition.Archived)
            return;

        Disposition = CaptureUserDisposition.Archived;
        Touch();
    }

    /// <summary>Return a kept capture to the Inbox.</summary>
    public void Reactivate()
    {
        EnsureNotArchived("reactivate");
        if (Disposition == CaptureUserDisposition.Active)
            return;

        Disposition = CaptureUserDisposition.Active;
        Touch();
    }

    /// <summary>Rewrites the processing projection from the authoritative job records (CF-03 runner).</summary>
    public void RecordProcessingSummary(CaptureProcessingSummary summary)
    {
        if (!Enum.IsDefined(summary))
            throw new DomainException(ErrorCodes.ValidationError, "Capture processing summary is invalid");
        EnsureNotArchived("process");
        if (ProcessingSummary == summary)
            return;

        ProcessingSummary = summary;
        Touch();
    }

    /// <summary>Rewrites the action projection from the authoritative planning records (CF-08 / CF-09 / CF-21).</summary>
    public void RecordActionState(CaptureActionState state)
    {
        if (!Enum.IsDefined(state))
            throw new DomainException(ErrorCodes.ValidationError, "Capture action state is invalid");
        EnsureNotArchived("plan against");
        if (ActionState == state)
            return;

        ActionState = state;
        Touch();
    }

    /// <summary>The user changes what they asked for. <see cref="CaptureIntentMode.Auto"/> clears the effective intent until a run resolves it.</summary>
    public void SetRequestedIntent(CaptureIntentMode intent)
    {
        if (!Enum.IsDefined(intent))
            throw new DomainException(ErrorCodes.ValidationError, "Capture intent is invalid");
        EnsureNotArchived("re-intend");
        if (RequestedIntent == intent)
            return;

        RequestedIntent = intent;
        EffectiveIntent = intent == CaptureIntentMode.Auto ? null : intent;
        IntentResolvedByRunId = null;
        Touch();
    }

    /// <summary>
    /// A processing run records the intent it inferred for an <see cref="CaptureIntentMode.Auto"/>
    /// request. The inference is never silent: it names the run (ADR-0065 §Decision 2).
    /// </summary>
    public void ResolveIntent(CaptureIntentMode effectiveIntent, Guid resolvedByRunId)
    {
        if (!Enum.IsDefined(effectiveIntent) || effectiveIntent == CaptureIntentMode.Auto)
            throw new DomainException(ErrorCodes.ValidationError, "Effective intent must be Remember, Organize or Act");
        if (resolvedByRunId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Run ID cannot be empty");
        if (RequestedIntent != CaptureIntentMode.Auto)
            throw new DomainException(ErrorCodes.ValidationError, "Only an Auto request can be resolved by a run");
        EnsureNotArchived("resolve intent for");

        EffectiveIntent = effectiveIntent;
        IntentResolvedByRunId = resolvedByRunId;
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
        EnsureNotArchived("re-target");

        if (ContextBoardId == boardId)
            return;

        ContextBoardId = boardId;
        Touch();
    }

    public void Retitle(string? userTitle)
    {
        var normalized = NormalizeBounded(userTitle, MaxUserTitleLength, "Capture title", singleLine: true);
        EnsureNotArchived("retitle");
        if (string.Equals(UserTitle, normalized, StringComparison.Ordinal))
            return;

        UserTitle = normalized;
        Touch();
    }

    private void EnsureNotArchived(string verb)
    {
        if (Disposition == CaptureUserDisposition.Archived)
        {
            throw new DomainException(ErrorCodes.ValidationError, $"Cannot {verb} an archived capture");
        }
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
