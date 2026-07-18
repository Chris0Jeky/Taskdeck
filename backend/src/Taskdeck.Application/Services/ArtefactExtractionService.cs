using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Explicit, size-bounded extraction orchestration. No HTTP endpoint or upload
/// hook calls this service in GEN-02; future workers may invoke it off the
/// request path without changing the persisted extraction contract.
/// </summary>
public sealed class ArtefactExtractionService : IArtefactExtractionService
{
    private readonly ISourceArtefactRepository _artefacts;
    private readonly IArtefactExtractionRepository _extractions;
    private readonly IReadOnlyList<IArtefactTextExtractor> _extractors;
    private readonly ArtefactStorageSettings _settings;
    private readonly ArtefactExtractionGate? _gate;
    private readonly ILogger<ArtefactExtractionService>? _logger;

    public ArtefactExtractionService(
        ISourceArtefactRepository artefacts,
        IArtefactExtractionRepository extractions,
        IEnumerable<IArtefactTextExtractor> extractors,
        ArtefactStorageSettings? settings = null,
        ILogger<ArtefactExtractionService>? logger = null,
        ArtefactExtractionGate? gate = null)
    {
        _artefacts = artefacts;
        _extractions = extractions;
        _extractors = extractors.ToArray();
        _settings = settings ?? new ArtefactStorageSettings();
        _gate = gate;
        _logger = logger;
    }

    public async Task<Result<ArtefactExtractionDto>> ExtractAsync(
        Guid userId,
        Guid sourceArtefactId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.ValidationError,
                "User ID cannot be empty");
        }
        if (sourceArtefactId == Guid.Empty)
        {
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.ValidationError,
                "Source artefact ID cannot be empty");
        }

        var artefact = await _artefacts.GetByIdForUserAsync(
            sourceArtefactId,
            userId,
            cancellationToken);
        if (artefact is null)
        {
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.NotFound,
                "Artefact not found");
        }

        var extractor = _extractors.FirstOrDefault(candidate => candidate.CanExtract(artefact.MimeType));
        if (extractor is null)
        {
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.ValidationError,
                $"No local text extractor supports MIME type '{artefact.MimeType}'");
        }
        if (!HasValidIdentity(extractor.ExtractorName, ArtefactExtraction.MaxExtractorNameLength) ||
            !HasValidIdentity(extractor.ExtractorVersion, ArtefactExtraction.MaxExtractorVersionLength) ||
            extractor.InputByteLimit is <= 0 or > int.MaxValue)
        {
            _logger?.LogError(
                "Artefact extractor registration has invalid identity for artefact {ArtefactId}",
                sourceArtefactId);
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.UnexpectedError,
                "The selected artefact extractor is misconfigured");
        }

        ArtefactExtractionResult? extractorResult = artefact.ByteSize > extractor.InputByteLimit
            ? WarningResult(extractor, ArtefactExtractionWarningCodes.InputTooLarge)
            : null;
        if (extractorResult is null)
        {
            await using var stream = new BoundedMemoryStream(extractor.InputByteLimit);
            var contentFound = false;
            try
            {
                contentFound = await _artefacts.CopyContentForUserAsync(
                    sourceArtefactId,
                    userId,
                    stream,
                    cancellationToken);
            }
            catch (ExtractionInputTooLargeException)
            {
                extractorResult = WarningResult(
                    extractor,
                    ArtefactExtractionWarningCodes.InputTooLarge);
            }

            if (extractorResult is null)
            {
                if (!contentFound)
                {
                    return Result.Failure<ArtefactExtractionDto>(
                        ErrorCodes.NotFound,
                        "Artefact not found");
                }

                stream.Position = 0;

                // Bound the parser's wall-clock, not just its input size. PdfPig's
                // PdfDocument.Open(...) runs synchronously and does not honour a
                // CancellationToken, so a parser-bomb PDF that stays under the
                // byte/page/character caps can still spin the parse thread for an
                // unbounded time. Enforce a budget with a CancelAfter linked to the
                // caller's token and run the parse on a worker so a runaway parse can
                // be abandoned while the request returns. The budget token is also
                // passed into the extractor, which observes it cooperatively between
                // pages/words — the clean path for bombs that reach the page loop.
                // Classification note: an extractor that swallowed the budget OCE and
                // rethrew a non-OCE fault at the instant the budget fires would race
                // the recorded code between extraction-timeout and extractor-error —
                // both are warning-bearing rows and exactly one is written, so either
                // outcome is safe; the shipped extractors propagate OCE, so this
                // cannot occur today.
                // The CTS is disposed inline only on paths where the worker has
                // provably completed; abandonment paths defer disposal to a
                // worker-completion continuation so the abandoned parse can never
                // touch a disposed token source.
                //
                // Extraction permit: take a permit immediately before spawning the
                // parse worker so the permit tracks parse-THREAD occupancy. When the
                // gate is saturated — every permit held, including by abandoned bombs
                // still spinning box-wide — reject pre-parse without creating a worker
                // or an extraction-history row (capacity is a transient property of the
                // box, not of this artefact; unlike the timeout/decoded-size outcomes,
                // it is not recorded). Callers retry on TooManyRequests. Release mirrors
                // budgetCts disposal exactly: inline in the finally on completed paths,
                // deferred to the worker-completion continuation on abandoned paths.
                if (_gate is not null && !_gate.TryAcquire())
                {
                    _logger?.LogWarning(
                        "Local extraction rejected for artefact {ArtefactId}: extraction capacity is saturated ({MaxConcurrency} concurrent)",
                        sourceArtefactId,
                        _gate.MaxConcurrency);
                    return Result.Failure<ArtefactExtractionDto>(
                        ErrorCodes.TooManyRequests,
                        "Local extraction is at capacity; retry shortly");
                }

                var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var workerAbandoned = false;
                try
                {
                    var budget = _settings.ExtractionTimeout;
                    if (budget > TimeSpan.Zero)
                        budgetCts.CancelAfter(budget);

                    var extractTask = Task.Run(
                        () => extractor.ExtractAsync(stream, budgetCts.Token),
                        CancellationToken.None);
                    try
                    {
                        extractorResult = await extractTask.WaitAsync(budgetCts.Token);
                    }
                    catch (OperationCanceledException)
                        when (budgetCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        // Budget exhausted (not caller cancellation). Abandon the
                        // parse. Honest cost: .NET cannot stop a thread that never
                        // observes cancellation, so the abandoned worker keeps holding
                        // one thread-pool thread at full CPU until PdfPig's synchronous
                        // parse runs to completion — only the REQUEST is bounded, not
                        // the parser's resource consumption, and nothing here caps how
                        // many abandoned parses can accumulate under concurrent
                        // parser-bomb submissions (tracked: #1379; must land before
                        // this service is wired to a request or worker path). The
                        // abandoned worker holds no file or DB handle — the blob was
                        // copied into the in-memory stream and the connection closed
                        // before extraction began. Record a single content-free
                        // timeout outcome.
                        workerAbandoned = true;
                        AbandonExtraction(extractTask, budgetCts);
                        _logger?.LogWarning(
                            "Local extraction exceeded the {BudgetSeconds}s wall-clock budget for artefact {ArtefactId} with extractor {ExtractorName}; recording a timeout outcome",
                            budget.TotalSeconds,
                            sourceArtefactId,
                            extractor.ExtractorName);
                        extractorResult = WarningResult(
                            extractor,
                            ArtefactExtractionWarningCodes.ExtractionTimeout);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        // Caller-driven cancellation (request aborted): abandon the
                        // worker and propagate without recording an extraction
                        // outcome. A stray OCE from a future extractor whose own token
                        // is unrelated to ours falls through to the extractor-error
                        // handler below instead of masquerading as caller cancellation.
                        workerAbandoned = true;
                        AbandonExtraction(extractTask, budgetCts);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // WaitAsync only propagates the task's own exception once the
                        // task has completed, so the worker is finished on this path.
                        _logger?.LogWarning(
                            "Local extraction failed for artefact {ArtefactId} with extractor {ExtractorName}; exception type {ExceptionType}",
                            sourceArtefactId,
                            extractor.ExtractorName,
                            ex.GetType().Name);
                        extractorResult = WarningResult(
                            extractor,
                            ArtefactExtractionWarningCodes.ExtractorError);
                    }
                }
                finally
                {
                    if (!workerAbandoned)
                    {
                        budgetCts.Dispose();
                        // Completed path (normal result, extractor fault, or store
                        // failure below): the worker is done, so release the permit
                        // here. Abandoned paths skip this and release in the worker
                        // continuation instead, holding the permit until the runaway
                        // thread actually finishes.
                        _gate?.Release();
                    }
                }
            }
        }

        var sanitized = SanitizeResult(extractor, extractorResult);
        var extraction = new ArtefactExtraction(
            sourceArtefactId,
            extractor.ExtractorName,
            extractor.ExtractorVersion,
            sanitized.Warnings,
            sanitized.ExtractedText);

        var storeResult = await _extractions.TryAddForUserAsync(
            extraction,
            userId,
            cancellationToken);
        return storeResult switch
        {
            ArtefactExtractionStoreResult.Stored => Result.Success(Map(extraction)),
            ArtefactExtractionStoreResult.UserInactive => Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.Unauthorized,
                "The authenticated user is no longer active"),
            ArtefactExtractionStoreResult.SourceArtefactUnavailable => Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.NotFound,
                "Artefact not found"),
            _ => throw new InvalidOperationException($"Unknown extraction store result: {storeResult}")
        };
    }

    public async Task<Result<ArtefactExtractionDto>> GetLatestAsync(
        Guid userId,
        Guid sourceArtefactId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || sourceArtefactId == Guid.Empty)
        {
            return Result.Failure<ArtefactExtractionDto>(
                ErrorCodes.ValidationError,
                "User ID and source artefact ID are required");
        }

        var extraction = await _extractions.GetLatestForArtefactForUserAsync(
            sourceArtefactId,
            userId,
            cancellationToken);
        return extraction is null
            ? Result.Failure<ArtefactExtractionDto>(ErrorCodes.NotFound, "Artefact extraction not found")
            : Result.Success(Map(extraction));
    }

    private static ArtefactExtractionResult SanitizeResult(
        IArtefactTextExtractor extractor,
        ArtefactExtractionResult? result)
    {
        if (result is null)
        {
            return new ArtefactExtractionResult(
                string.Empty,
                [ArtefactExtractionWarningCodes.ExtractorContractError],
                extractor.ExtractorName,
                extractor.ExtractorVersion);
        }

        var contractError =
            !string.Equals(result.ExtractorName, extractor.ExtractorName, StringComparison.Ordinal) ||
            !string.Equals(result.ExtractorVersion, extractor.ExtractorVersion, StringComparison.Ordinal) ||
            result.ExtractedText is null ||
            result.Warnings is null;

        var warnings = new List<string>();
        if (result.Warnings is not null)
        {
            foreach (var warning in result.Warnings)
            {
                if (string.IsNullOrWhiteSpace(warning) ||
                    warning.Length > ArtefactExtraction.MaxWarningLength ||
                    warning.Any(char.IsControl) ||
                    ArtefactTextNormalization.HasUnpairedSurrogate(warning))
                {
                    contractError = true;
                    continue;
                }

                if (!warnings.Contains(warning, StringComparer.Ordinal))
                    warnings.Add(warning);
            }

            if (result.Warnings.Count > ArtefactExtraction.MaxWarningCount ||
                warnings.Count > ArtefactExtraction.MaxWarningCount)
            {
                contractError = true;
            }
        }

        var text = ArtefactTextNormalization.NormalizeLineEndings(result.ExtractedText ?? string.Empty);
        if (ArtefactTextNormalization.HasUnpairedSurrogate(text))
        {
            text = string.Empty;
            contractError = true;
        }
        if (text.Length > ArtefactExtraction.MaxExtractedTextLength)
        {
            text = ArtefactTextNormalization.TruncateWithoutSplittingSurrogatePair(
                text,
                ArtefactExtraction.MaxExtractedTextLength);
            AddPriorityWarning(warnings, ArtefactExtractionWarningCodes.CharacterLimit);
        }

        if (contractError)
            AddPriorityWarning(warnings, ArtefactExtractionWarningCodes.ExtractorContractError);

        warnings = warnings
            .Distinct(StringComparer.Ordinal)
            .Take(ArtefactExtraction.MaxWarningCount)
            .ToList();

        return new ArtefactExtractionResult(
            text,
            warnings,
            extractor.ExtractorName,
            extractor.ExtractorVersion);
    }

    private void AbandonExtraction(Task extractTask, CancellationTokenSource budgetCts)
    {
        // The request returns before this worker necessarily finishes, and the
        // request-scoped input stream is disposed on return, so an abandoned parse
        // typically faults (e.g. ObjectDisposedException) on its next read. Observe
        // that fault so it is not surfaced as an UnobservedTaskException, and only
        // dispose the budget CTS once the worker has fully completed so the
        // abandoned parse can never touch a disposed token source. (Cancelled
        // workers need no observation — cancelled tasks do not raise
        // UnobservedTaskException.)
        _ = extractTask.ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    _logger?.LogDebug(
                        "Abandoned artefact extraction worker faulted after the request completed; exception type {ExceptionType}",
                        task.Exception!.GetBaseException().GetType().Name);
                }

                budgetCts.Dispose();
                // Release the permit only now that the abandoned worker has actually
                // finished: the whole point of the gate is that a runaway parse holds
                // capacity for its full lifetime, not just for the request that spawned
                // it. This is the exactly-once release for every abandoned path.
                _gate?.Release();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static bool HasValidIdentity(string value, int maxLength)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= maxLength &&
           !value.Any(char.IsControl) &&
           !ArtefactTextNormalization.HasUnpairedSurrogate(value);

    private static void AddPriorityWarning(List<string> warnings, string warning)
    {
        if (warnings.Contains(warning, StringComparer.Ordinal))
            return;
        while (warnings.Count >= ArtefactExtraction.MaxWarningCount)
            warnings.RemoveAt(warnings.Count - 1);
        warnings.Insert(0, warning);
    }

    private static ArtefactExtractionResult WarningResult(
        IArtefactTextExtractor extractor,
        string warning)
        => new(
            string.Empty,
            [warning],
            extractor.ExtractorName,
            extractor.ExtractorVersion);

    private static ArtefactExtractionDto Map(ArtefactExtraction extraction)
        => new(
            extraction.Id,
            extraction.SourceArtefactId,
            extraction.ExtractorName,
            extraction.ExtractorVersion,
            extraction.Warnings,
            extraction.ExtractedText,
            extraction.TextLength,
            extraction.CreatedAt);

    private sealed class BoundedMemoryStream : MemoryStream
    {
        private readonly long _maxBytes;

        public BoundedMemoryStream(long maxBytes)
            : base(capacity: (int)Math.Min(maxBytes, 64 * 1024))
        {
            _maxBytes = maxBytes;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureWithinLimit(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureWithinLimit(buffer.Length);
            base.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureWithinLimit(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureWithinLimit(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override void WriteByte(byte value)
        {
            EnsureWithinLimit(1);
            base.WriteByte(value);
        }

        private void EnsureWithinLimit(int count)
        {
            if (Position > _maxBytes - count)
                throw new ExtractionInputTooLargeException();
        }
    }

    private sealed class ExtractionInputTooLargeException : IOException;
}
