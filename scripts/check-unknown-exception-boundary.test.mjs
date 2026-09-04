import test from 'node:test'
import assert from 'node:assert/strict'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import {
  ALLOWLIST,
  PERSISTED_STATE_FILES,
  findMcpFindings,
  findPersistedStateFindings,
  scanTree,
} from './check-unknown-exception-boundary.mjs'

const repoRoot = resolve(fileURLToPath(new URL('..', import.meta.url)))
const mcpPath = 'backend/src/Taskdeck.Api/Mcp/ExampleTools.cs'
const persistedPath = PERSISTED_STATE_FILES[0]

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
// Allowlist and the real tree.
// ---------------------------------------------------------------------------

test('every allowlist entry names a reason and an issue', () => {
  assert.ok(ALLOWLIST.length > 0)
  for (const entry of ALLOWLIST) {
    assert.match(entry.path, /^backend\/src\/.+\.cs$/)
    assert.ok(entry.pattern.length > 0, 'allowlist entry needs a pattern')
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
