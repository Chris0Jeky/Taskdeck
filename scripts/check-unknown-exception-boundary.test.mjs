import test from 'node:test'
import assert from 'node:assert/strict'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import {
  ALLOWLIST,
  PERSISTED_STATE_FILES,
  findCatchBlockFindings,
  findMcpFindings,
  findPersistedStateFindings,
  scanTree,
} from './check-unknown-exception-boundary.mjs'

const repoRoot = resolve(fileURLToPath(new URL('..', import.meta.url)))
const mcpPath = 'backend/src/Taskdeck.Api/Mcp/ExampleTools.cs'
const persistedPath = PERSISTED_STATE_FILES[0]

function lineOf(source, lineNumber) {
  return source.split(/\r?\n/)[lineNumber - 1]
}

// ---------------------------------------------------------------------------
// Rule 1 — MCP payloads must not return raw ErrorMessage.
// ---------------------------------------------------------------------------

test('rejects a raw ErrorMessage passed to an Error helper', () => {
  const source = `
    private static string Error(Result result)
    {
        return Error(result.ErrorMessage);
    }
`
  const findings = findMcpFindings(source, mcpPath)
  assert.equal(findings.length, 1)
  assert.equal(findings[0].rule, 'mcp-error-message')
})

test('rejects a raw ErrorMessage serialized into an anonymous error object', () => {
  const source = `
    private static string Error(Result result)
    {
        return JsonSerializer.Serialize(new
        {
            error = result.ErrorMessage
        }, BoardResources.SerializerOptions);
    }
`
  assert.equal(findMcpFindings(source, mcpPath).length, 1)
})

test('rejects a raw ErrorMessage interpolated into a thrown MCP exception', () => {
  const source = `
        if (!result.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to get board: {result.ErrorMessage}");
`
  assert.equal(findMcpFindings(source, mcpPath).length, 1)
})

test('accepts the sanitized Error(Result) helper', () => {
  const source = `
    private static string Error(Result result)
    {
        return Error(SensitiveDataRedactor.SanitizeLlmFailureMessage(
            result.ErrorCode,
            result.ErrorMessage));
    }
`
  assert.deepEqual(findMcpFindings(source, mcpPath), [])
})

test('accepts the reviewed unexpected-error ternary that keeps curated domain text', () => {
  const source = `
    private static string Error(Result result)
    {
        var message = string.Equals(
            result.ErrorCode,
            ErrorCodes.UnexpectedError,
            StringComparison.Ordinal)
                ? SensitiveDataRedactor.GenericUnexpectedFailureMessage
                : result.ErrorMessage;

        return Error(message);
    }
`
  assert.deepEqual(findMcpFindings(source, mcpPath), [])
})

test('accepts a throw that routes through the sanitizing PublicFailureMessage helper', () => {
  const source = `
        if (!result.IsSuccess)
            throw new InvalidOperationException($"MCP: failed: {PublicFailureMessage(result)}");
`
  assert.deepEqual(findMcpFindings(source, mcpPath), [])
})

test('rejects a raw sibling member even when another member is sanitized', () => {
  const source = `
    private static string Error(Result result)
    {
        return JsonSerializer.Serialize(new
        {
            error = PublicFailureMessage(result),
            detail = result.ErrorMessage
        }, BoardResources.SerializerOptions);
    }
`
  const findings = findMcpFindings(source, mcpPath)
  assert.equal(findings.length, 1, 'the raw sibling must not be excused by its sanitized neighbour')
  assert.equal(findings[0].rule, 'mcp-error-message')
})

test('accepts a two-member payload when both members are wrapped', () => {
  const source = `
    private static string Error(Result result)
    {
        return JsonSerializer.Serialize(new
        {
            error = PublicFailureMessage(result),
            detail = SensitiveDataRedactor.SanitizeLlmFailureMessage(result.ErrorCode, result.ErrorMessage)
        }, BoardResources.SerializerOptions);
    }
`
  assert.deepEqual(findMcpFindings(source, mcpPath), [])
})

test('flags a new raw member added inside an allowlisted statement', () => {
  const source = `
        return JsonSerializer.Serialize(new
        {
            id = c.Id,
            retryCount = c.RetryCount,
            errorMessage = c.ErrorMessage,
            providerError = c.Provider.ErrorMessage
        }, BoardResources.SerializerOptions);
`
  const path = 'backend/src/Taskdeck.Api/Mcp/CaptureResources.cs'
  const findings = findMcpFindings(source, path)
  assert.equal(findings.length, 1, 'the allowlist covers only its own line, not the whole statement')
  assert.match(lineOf(source, findings[0].line), /providerError/)
})

test('never flags a log statement that includes ErrorMessage', () => {
  const source = `
        _logger.LogError(
            "MCP {OperationName} failed: Error={ErrorMessage}",
            operationName,
            result.ErrorMessage);
`
  assert.deepEqual(findMcpFindings(source, mcpPath), [])
})

test('ignores an ErrorMessage read that is not an outbound payload', () => {
  const source = `
        var failureCode = result.ErrorMessage is null ? 0 : 1;
`
  assert.deepEqual(findMcpFindings(source, mcpPath), [])
})

// ---------------------------------------------------------------------------
// Rule 2 — persisted failure state must not store unknown-exception text.
// ---------------------------------------------------------------------------

test('rejects ex.Message persisted from an unknown-exception catch', () => {
  const source = `
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent run '{RunId}' failed unexpectedly", run.Id);
            run.MarkFailed(ex.Message);
        }
`
  const findings = findPersistedStateFindings(source, persistedPath)
  assert.equal(findings.length, 1)
  assert.equal(findings[0].rule, 'persisted-unknown-failure')
})

test('rejects ex.ToString() assigned to a persisted failure property', () => {
  const source = `
        catch (Exception ex)
        {
            commandRun.ErrorMessage = ex.ToString();
        }
`
  assert.equal(findPersistedStateFindings(source, persistedPath).length, 1)
})

test('rejects unknown-exception text interpolated into a returned failure result', () => {
  const source = `
        catch (Exception ex)
        {
            return Result.Failure<AgentRunDto>(ErrorCodes.UnexpectedError, $"Agent run failed: {ex.Message}");
        }
`
  assert.equal(findPersistedStateFindings(source, persistedPath).length, 1)
})

test('accepts the generic replacement for unknown-exception failure state', () => {
  const source = `
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Agent run '{RunId}' failed unexpectedly", run.Id);
            run.MarkFailed(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
            return Result.Failure<AgentRunDto>(
                ErrorCodes.UnexpectedError,
                SensitiveDataRedactor.GenericUnexpectedFailureMessage);
        }
`
  assert.deepEqual(findPersistedStateFindings(source, persistedPath), [])
})

test('accepts an explicitly sanitized exception summary', () => {
  const source = `
        catch (Exception ex)
        {
            commandRun.Fail(SensitiveDataRedactor.SummarizeException(ex));
        }
`
  assert.deepEqual(findPersistedStateFindings(source, persistedPath), [])
})

test('never flags a known-domain catch that passes its curated message through', () => {
  const source = `
        catch (DomainException ex)
        {
            commandRun.Fail(ex.Message);
            return Result.Failure<CommandRunDto>(ex.ErrorCode, ex.Message);
        }
`
  assert.deepEqual(findPersistedStateFindings(source, persistedPath), [])
})

test('never flags logging inside a nested save-failure catch', () => {
  const source = `
        catch (Exception ex)
        {
            run.MarkFailed(SensitiveDataRedactor.GenericUnexpectedFailureMessage);
            try { await _unitOfWork.SaveChangesAsync(CancellationToken.None); }
            catch (Exception saveEx)
            {
                _logger?.LogError(saveEx, "Failed to persist failure state for run '{RunId}'", run.Id);
            }
        }
`
  assert.deepEqual(findPersistedStateFindings(source, persistedPath), [])
})

// ---------------------------------------------------------------------------
// Rule 2 over the MCP region — a raw ex.Message in a new MCP tool.
// ---------------------------------------------------------------------------

test('rejects raw ex.Message returned from a catch in an MCP tool', () => {
  const source = `
        try
        {
            return await DoWorkAsync();
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
`
  const findings = findCatchBlockFindings(source, mcpPath, 'mcp-unknown-exception-text')
  assert.equal(findings.length, 1)
  assert.equal(findings[0].rule, 'mcp-unknown-exception-text')
})

test('rejects raw ex.Message interpolated into an MCP resource throw', () => {
  const source = `
        catch (Exception ex)
        {
            throw new InvalidOperationException($"MCP: board load failed: {ex.Message}");
        }
`
  assert.equal(findCatchBlockFindings(source, mcpPath, 'mcp-unknown-exception-text').length, 1)
})

test('exempts the telemetry catches that only log', () => {
  const source = `
        catch (Exception ex)
        {
            // Telemetry failures must never break MCP operations.
            _logger.LogDebug(ex, "Failed to record MCP operation completion telemetry");
        }
`
  assert.deepEqual(findCatchBlockFindings(source, mcpPath, 'mcp-unknown-exception-text'), [])
})

test('exempts a catch that reports only the exception type name', () => {
  const source = `
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, LogSanitizer.SafeExceptionDescription(ex));
            activity?.SetTag(TaskdeckTelemetryTags.McpErrorType, ex.GetType().Name);
        }
`
  assert.deepEqual(findCatchBlockFindings(source, mcpPath, 'mcp-unknown-exception-text'), [])
})

// ---------------------------------------------------------------------------
// Allowlist and the real tree.
// ---------------------------------------------------------------------------

test('every allowlist entry names a line, a reason and an issue', () => {
  assert.ok(ALLOWLIST.length > 0)
  for (const entry of ALLOWLIST) {
    assert.match(entry.path, /^backend\/src\/.+\.cs$/)
    assert.ok(entry.line.length > 0, 'allowlist entry needs an exact line to key on')
    assert.ok(!entry.line.includes('\n'), 'allowlist entries key on one line, never a statement')
    assert.match(entry.issue, /^#\d+$/)
    assert.ok(entry.reason.length > 20, 'allowlist entry needs a stated reason')
  }
})

test('the real tree has zero unknown-exception boundary findings', async () => {
  const findings = await scanTree(repoRoot)
  assert.deepEqual(
    findings.map((finding) => `${finding.path}:${finding.line} ${finding.message}`),
    [],
  )
})

test('the webhook delivery worker is guarded as a persisted-state surface', () => {
  assert.ok(
    PERSISTED_STATE_FILES.includes(
      'backend/src/Taskdeck.Api/Workers/OutboundWebhookDeliveryWorker.cs',
    ),
    'OutboundWebhookDeliveryWorker persists failure text into OutboundWebhookDelivery.LastErrorMessage (#2474), so it must stay in PERSISTED_STATE_FILES',
  )
})

// ---------------------------------------------------------------------------
// #2473 blind spot 1 — intermediate-variable laundering (rule 1).
// Single method body, single hop: a local that takes a raw ErrorMessage is
// treated as an ErrorMessage occurrence when it is later returned or thrown.
// ---------------------------------------------------------------------------

test('rejects a raw ErrorMessage laundered through a local before the return', () => {
  const source = `
    private static string Error(Result result)
    {
        var detail = result.ErrorMessage;
        return Error(detail);
    }
`
  const findings = findMcpFindings(source, mcpPath)
  assert.equal(findings.length, 1)
  assert.equal(findings[0].rule, 'mcp-error-message')
  assert.match(lineOf(source, findings[0].line), /return Error\(detail\)/)
})

test('rejects a typed local that launders ErrorMessage into a serialized payload', () => {
  const source = `
    private static string Error(Result result)
    {
        string detail = result.ErrorMessage;
        return JsonSerializer.Serialize(new
        {
            error = detail
        }, BoardResources.SerializerOptions);
    }
`
  assert.equal(findMcpFindings(source, mcpPath).length, 1)
})

test('accepts a local that is already sanitized where it is assigned', () => {
  const source = `
    private static string Error(Result result)
    {
        var detail = SensitiveDataRedactor.SanitizeLlmFailureMessage(result.ErrorCode, result.ErrorMessage);
        return Error(detail);
    }
`
  assert.deepEqual(findMcpFindings(source, mcpPath), [])
})

test('accepts a laundered local that a sanitizer wraps at the return', () => {
  const source = `
    private static string Error(Result result)
    {
        var detail = result.ErrorMessage;
        return Error(SensitiveDataRedactor.Redact(detail));
    }
`
  assert.deepEqual(findMcpFindings(source, mcpPath), [])
})

test('drops a laundered local at the end of its own method body', () => {
  const source = `
    private static void Capture(Result result)
    {
        var detail = result.ErrorMessage;
        Store(detail);
    }

    private static string Error(string detail)
    {
        return Error(detail);
    }
`
  assert.deepEqual(findMcpFindings(source, mcpPath), [])
})

test('stops laundering at one hop rather than chasing a second local', () => {
  const source = `
    private static string Error(Result result)
    {
        var detail = result.ErrorMessage;
        var copy = detail;
        return Error(copy);
    }
`
  const findings = findMcpFindings(source, mcpPath)
  assert.equal(findings.length, 0, 'single-hop tracking stops at the first copy by design')
})

// ---------------------------------------------------------------------------
// #2473 blind spot 2 — the guarded-ternary exemption.
// ---------------------------------------------------------------------------

test('rejects a null-conditional ErrorMessage sitting beside a guarded ternary', () => {
  const source = `
    private static string Error(Result result)
    {
        return Error(
            string.Equals(result.ErrorCode, ErrorCodes.UnexpectedError, StringComparison.Ordinal)
                ? SensitiveDataRedactor.GenericUnexpectedFailureMessage
                : SensitiveDataRedactor.GenericUnexpectedFailureMessage,
            result?.ErrorMessage);
    }
`
  const findings = findMcpFindings(source, mcpPath)
  assert.equal(findings.length, 1)
  assert.match(lineOf(source, findings[0].line), /result\?\.ErrorMessage/)
})

test('rejects a raw ErrorMessage passed as a named argument', () => {
  const source = `
    private static string Error(Result result)
    {
        return Error(
            code: result.ErrorCode,
            message: result.ErrorMessage);
    }
`
  const findings = findMcpFindings(source, mcpPath)
  assert.equal(findings.length, 1)
  assert.match(lineOf(source, findings[0].line), /message: result\.ErrorMessage/)
})

test('accepts an outbound guarded ternary whose condition tests the same result', () => {
  const source = `
    private static string Error(Result result)
    {
        return Error(string.Equals(result.ErrorCode, ErrorCodes.UnexpectedError, StringComparison.Ordinal)
            ? SensitiveDataRedactor.GenericUnexpectedFailureMessage
            : result.ErrorMessage);
    }
`
  assert.deepEqual(findMcpFindings(source, mcpPath), [])
})

test('rejects a guarded-looking ternary whose condition tests a different result', () => {
  const source = `
    private static string Error(Result result, Result inner)
    {
        return Error(string.Equals(inner.ErrorCode, ErrorCodes.UnexpectedError, StringComparison.Ordinal)
            ? SensitiveDataRedactor.GenericUnexpectedFailureMessage
            : result.ErrorMessage);
    }
`
  assert.equal(findMcpFindings(source, mcpPath).length, 1)
})

// ---------------------------------------------------------------------------
// #2473 blind spot 3 — rule 2 sanitization is judged per occurrence.
// ---------------------------------------------------------------------------

test('rejects ex.Message concatenated onto a LogSanitizer call inside a catch', () => {
  const source = `
        catch (Exception ex)
        {
            return Error(LogSanitizer.SanitizeForLog(operationName) + ex.Message);
        }
`
  const findings = findCatchBlockFindings(source, mcpPath, 'mcp-unknown-exception-text')
  assert.equal(findings.length, 1)
  assert.equal(findings[0].rule, 'mcp-unknown-exception-text')
})

test('rejects a raw ex.Message beside a redacted sibling in one persisted statement', () => {
  const source = `
        catch (Exception ex)
        {
            commandRun.Fail(SensitiveDataRedactor.Redact(context) + ex.Message);
        }
`
  assert.equal(findPersistedStateFindings(source, persistedPath).length, 1)
})

test('accepts an ex.Message that LogSanitizer itself wraps', () => {
  const source = `
        catch (Exception ex)
        {
            return Error(LogSanitizer.SanitizeForLog(ex.Message));
        }
`
  assert.deepEqual(findCatchBlockFindings(source, mcpPath, 'mcp-unknown-exception-text'), [])
})

test('accepts a redacted exception message persisted from a catch', () => {
  const source = `
        catch (Exception ex)
        {
            commandRun.Fail(SensitiveDataRedactor.Redact(ex.Message));
        }
`
  assert.deepEqual(findPersistedStateFindings(source, persistedPath), [])
})

// ---------------------------------------------------------------------------
// #2473 blind spot 4 — literal masking must not swallow the rest of a line.
// ---------------------------------------------------------------------------

test('flags an ErrorMessage after a literal that ends in an escaped backslash', () => {
  const source = `
        return Error("prefix \\\\" + result.ErrorMessage);
`
  assert.equal(findMcpFindings(source, mcpPath).length, 1)
})

test('keeps an escaped quote inside a literal masked', () => {
  const source = `
        return Error("quoted \\".ErrorMessage\\" text");
`
  assert.deepEqual(findMcpFindings(source, mcpPath), [])
})

test('flags an ErrorMessage after a verbatim literal that ends with a backslash', () => {
  const source = `
        return Error(@"C:\\temp\\" + result.ErrorMessage);
`
  assert.equal(findMcpFindings(source, mcpPath).length, 1)
})

test('keeps a verbatim literal with doubled quotes fully masked', () => {
  const source = `
        return Error(@"pattern "".ErrorMessage"" only");
`
  assert.deepEqual(findMcpFindings(source, mcpPath), [])
})
