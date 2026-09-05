#!/usr/bin/env node

// Focused guard for issue #2351: block NEW raw unknown-exception flows from reaching an
// LLM-facing MCP payload or a persisted worker/agent/command failure field.
//
// Two rules, both deliberately narrow:
//   1. `mcp-error-message` — in MCP files, each raw `.ErrorMessage` member expression reached in a
//      return/throw/serialize position must itself be wrapped in a sanitizing call. A local that
//      takes a raw `.ErrorMessage` inside the same block counts as an occurrence when it is later
//      returned (one hop, never across methods).
//   2. `unknown-exception-text` — in MCP files and in the listed persisted-state files, a
//      `catch (Exception ex)` block may not carry `ex.Message` / `ex.StackTrace` / `ex.ToString()`
//      outward. Sanitization is judged per occurrence, exactly as in rule 1.
//
// Neither rule inspects log statements, and neither inspects known-domain catches
// (`catch (DomainException ex)`), because the shipped trust model deliberately passes curated
// domain messages through. Everything outside these two regions is out of scope by design; the
// residuals that sit outside them are recorded in the inventory rather than suppressed here.
//
// This stays a statement matcher, not a compiler: it has no C# parser and no dependency. Its known
// limits are written down in the inventory rather than papered over here.
//
// Reviewed surface inventory: docs/security/UNKNOWN_EXCEPTION_SURFACE_INVENTORY.md
// Policy: docs/security/SECURITY_LOGGING_REDACTION.md

import { readFile, readdir } from 'node:fs/promises'
import { resolve, join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const repoRoot = resolve(fileURLToPath(new URL('..', import.meta.url)))

// Region 1: every MCP tool/resource file returns strings straight into an LLM transcript.
export const MCP_DIRECTORY = 'backend/src/Taskdeck.Api/Mcp'

// Region 2: files whose catch blocks write failure text into durable rows. Extending this list
// is how a new persisted-state surface joins the guard.
export const PERSISTED_STATE_FILES = [
  'backend/src/Taskdeck.Application/Services/AgentRuntime.cs',
  'backend/src/Taskdeck.Application/Services/OpsCliService.cs',
  'backend/src/Taskdeck.Api/Workers/OutboundWebhookDeliveryWorker.cs',
]

// Calls that actually sanitize the value handed to them. An `.ErrorMessage` occurrence counts as
// safe only when one of these encloses it — being merely co-located in the same statement as a
// sanitized sibling member is not enough.
export const SANITIZER_CALLS = new Set([
  'SanitizeLlmFailureMessage',
  'Redact',
  'SummarizeException',
  'PublicFailureMessage',
  'SafeExceptionDescription',
])

// Rule 2 accepts a narrower set: a call that must generalize or redact the exception text it is
// handed. Rule 1's `PublicFailureMessage` / `SafeExceptionDescription` helpers take a `Result` or
// an `Exception`, not raw exception text, so they are not listed here.
export const EXCEPTION_TEXT_SANITIZERS = new Set([
  'Redact',
  'SummarizeException',
  'SanitizeLlmFailureMessage',
])

// Log sanitizers count for rule 2 only when they wrap the occurrence itself. A statement that
// merely mentions `LogSanitizer.` elsewhere does not excuse a raw `ex.Message` beside it.
export const LOG_SANITIZER_TYPES = ['LogSanitizer', 'LogValueSanitizer']

// Logging is explicitly out of scope: structured logs are a trusted operator sink.
const LOGGING_PATTERN = /(?:_logger|\bLogger)\s*[?.]|\bLog(?:Error|Warning|Information|Debug|Critical|Trace)\s*\(/

// A statement that opens a local: `var x = ...`, `string x = ...` or a bare `x = ...`. Used by the
// single-hop laundering pass in rule 1.
const LOCAL_ASSIGNMENT = /^\s*(?:(?:var|string|object|dynamic)\??\s+)?([A-Za-z_]\w*)\s*=(?!=)/

// Known deliberate cases. Each entry is keyed to a path plus the exact source LINE that carries the
// occurrence, never the whole statement, so a new raw member added to an already-allowlisted
// multi-line statement is still flagged.
export const ALLOWLIST = [
  {
    path: 'backend/src/Taskdeck.Api/Mcp/CaptureResources.cs',
    line: 'errorMessage = c.ErrorMessage',
    issue: '#2443',
    reason:
      'Capture.ErrorMessage is sanitized on the write side before it is ever persisted ' +
      '(LlmQueueToProposalWorker.cs SanitizeLlmFailureMessage, TranscriptTriageWorker.cs ' +
      'SanitizeLlmFailureMessage), so the read projection replays an already-safe stored value.',
  },
]

function isAllowlisted(path, lineText) {
  const normalized = lineText.trim()
  return ALLOWLIST.some((entry) => path.endsWith(entry.path) && normalized.includes(entry.line))
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/**
 * True when the character at `index` is escaped by an ODD run of preceding backslashes. `"a\\"`
 * ends its literal (the backslash is itself escaped); `"a\""` does not.
 */
function isBackslashEscaped(text, index) {
  let backslashes = 0
  for (let cursor = index - 1; cursor >= 0 && text[cursor] === '\\'; cursor -= 1) backslashes += 1
  return backslashes % 2 === 1
}

/**
 * Blank out string literals and line comments so brace/paren depth counting is not thrown off by
 * punctuation inside message text. Length is preserved so offsets stay aligned with the source.
 * Interpolation holes (`{ ... }` inside a `$"..."`) are NOT blanked: an interpolated
 * `.ErrorMessage` is exactly the flow this guard exists to catch.
 *
 * Verbatim (`@"..."`) literals are tracked separately: a backslash is an ordinary character there
 * and `""` is the escaped quote. Literals are still closed at a newline, so a verbatim literal that
 * spans lines is masked per line rather than as a whole; the inventory records that limit.
 */
export function maskLiterals(text, { preserveInterpolation = false } = {}) {
  let masked = ''
  let inString = false
  let inChar = false
  let verbatim = false
  let holeDepth = 0

  for (let index = 0; index < text.length; index += 1) {
    const char = text[index]

    if (char === '\n') {
      masked += char
      inString = false
      inChar = false
      verbatim = false
      holeDepth = 0
      continue
    }

    // A line comment ends the logical line for scanning purposes.
    if (!inString && !inChar && char === '/' && text[index + 1] === '/') {
      const lineEnd = text.indexOf('\n', index)
      const stop = lineEnd === -1 ? text.length : lineEnd
      masked += ' '.repeat(stop - index)
      index = stop - 1
      continue
    }

    if (inString && preserveInterpolation && char === '{') {
      holeDepth += 1
      masked += char
      continue
    }
    if (inString && preserveInterpolation && char === '}' && holeDepth > 0) {
      holeDepth -= 1
      masked += char
      continue
    }
    if (inString && holeDepth > 0) {
      masked += char
      continue
    }

    if (!inChar && char === '"') {
      if (!inString) {
        verbatim = text[index - 1] === '@' || (text[index - 1] === '$' && text[index - 2] === '@')
        inString = true
        masked += '"'
        continue
      }
      if (verbatim) {
        // `""` is one escaped quote inside a verbatim literal, not a close followed by an open.
        if (text[index + 1] === '"') {
          masked += '  '
          index += 1
          continue
        }
        inString = false
        verbatim = false
        masked += '"'
        continue
      }
      if (isBackslashEscaped(text, index)) {
        masked += ' '
        continue
      }
      inString = false
      masked += '"'
      continue
    }

    if (!inString && char === "'") {
      if (!inChar) {
        inChar = true
        masked += "'"
        continue
      }
      if (isBackslashEscaped(text, index)) {
        masked += ' '
        continue
      }
      inChar = false
      masked += "'"
      continue
    }

    masked += inString || inChar ? ' ' : char
  }

  return masked
}

function depthDelta(maskedLine) {
  let delta = 0
  for (const char of maskedLine) {
    if (char === '(' || char === '[') delta += 1
    if (char === ')' || char === ']') delta -= 1
  }
  return delta
}

function braceDelta(maskedText) {
  let delta = 0
  for (const char of maskedText) {
    if (char === '{') delta += 1
    if (char === '}') delta -= 1
  }
  return delta
}

/**
 * Group source lines into whole statements so a multi-line `return JsonSerializer.Serialize(new
 * { ... })` is judged as one unit rather than line by line.
 */
export function splitStatements(source) {
  const lines = source.split(/\r?\n/)
  const statements = []
  let buffer = []
  let startLine = 1
  let depth = 0

  lines.forEach((line, index) => {
    if (buffer.length === 0) startLine = index + 1
    const masked = maskLiterals(line)
    buffer.push(line)
    depth += depthDelta(masked)
    if (depth < 0) depth = 0
    const trimmed = masked.trimEnd()
    const closes = trimmed.endsWith(';') || trimmed.endsWith('{') || trimmed.endsWith('}')
    if (depth === 0 && closes) {
      statements.push({ text: buffer.join('\n'), startLine })
      buffer = []
    }
  })

  if (buffer.length > 0) statements.push({ text: buffer.join('\n'), startLine })
  return statements
}

/**
 * Walk outward from `index`, collecting the FULL callee name of every call expression that encloses
 * it. `Error(SensitiveDataRedactor.SanitizeLlmFailureMessage(code, result.ErrorMessage))` yields
 * ['SensitiveDataRedactor.SanitizeLlmFailureMessage', 'Error']. Callers match on the last segment
 * when the receiving type does not matter, and on the whole name when it does.
 */
export function enclosingCallees(scanText, index) {
  const callees = []
  let depth = 0

  for (let cursor = index - 1; cursor >= 0; cursor -= 1) {
    const char = scanText[cursor]
    if (char === ')') {
      depth += 1
      continue
    }
    if (char !== '(') continue
    if (depth > 0) {
      depth -= 1
      continue
    }

    // Unmatched '(' — read the callee name immediately before it.
    let end = cursor
    while (end > 0 && /\s/.test(scanText[end - 1])) end -= 1
    let start = end
    while (start > 0 && /[\w.]/.test(scanText[start - 1])) start -= 1
    const name = scanText.slice(start, end)
    if (name) callees.push(name)
  }

  return callees
}

function lastSegment(name) {
  return name.split('.').pop()
}

function wrappedBySanitizer(callees) {
  return callees.some((callee) => SANITIZER_CALLS.has(lastSegment(callee)))
}

function wrappedByExceptionTextSanitizer(callees) {
  return callees.some((callee) => {
    if (EXCEPTION_TEXT_SANITIZERS.has(lastSegment(callee))) return true
    return LOG_SANITIZER_TYPES.some((type) => callee.startsWith(`${type}.`))
  })
}

function lineNumberAt(text, index) {
  let line = 1
  for (let cursor = 0; cursor < index; cursor += 1) {
    if (text[cursor] === '\n') line += 1
  }
  return line
}

function lineTextAt(text, index) {
  const start = text.lastIndexOf('\n', index - 1) + 1
  const end = text.indexOf('\n', index)
  return text.slice(start, end === -1 ? text.length : end)
}

function isComparisonEquals(scanText, cursor) {
  return (
    scanText[cursor - 1] === '=' ||
    scanText[cursor - 1] === '!' ||
    scanText[cursor - 1] === '<' ||
    scanText[cursor - 1] === '>' ||
    scanText[cursor + 1] === '='
  )
}

/**
 * Start of the sub-expression that contains `index`: the enclosing '(' / '[', or the nearest
 * depth-0 ';', ',', '{', '}' or assignment '='.
 */
function subExpressionStart(scanText, index) {
  let depth = 0
  for (let cursor = index - 1; cursor >= 0; cursor -= 1) {
    const char = scanText[cursor]
    if (char === ')' || char === ']') {
      depth += 1
      continue
    }
    if (char === '(' || char === '[') {
      if (depth === 0) return cursor + 1
      depth -= 1
      continue
    }
    if (depth > 0) continue
    if (char === ';' || char === ',' || char === '{' || char === '}') return cursor + 1
    if (char === '=' && !isComparisonEquals(scanText, cursor)) return cursor + 1
  }
  return 0
}

/**
 * Locate the `?` of the conditional expression whose arm holds `index`, or null when `index` is not
 * a ternary arm. The walk stops at the boundaries of the enclosing sub-expression, so a named
 * argument (`message: result.ErrorMessage`) and a null-conditional read (`result?.ErrorMessage`)
 * are not mistaken for arms.
 */
function enclosingTernaryQuestion(scanText, index) {
  let depth = 0
  for (let cursor = index - 1; cursor >= 0; cursor -= 1) {
    const char = scanText[cursor]
    if (char === ')' || char === ']') {
      depth += 1
      continue
    }
    if (char === '(' || char === '[') {
      if (depth === 0) return null
      depth -= 1
      continue
    }
    if (depth > 0) continue
    if (char === ';' || char === ',' || char === '{' || char === '}') return null
    if (char === '=' && !isComparisonEquals(scanText, cursor)) return null
    if (char === ':') {
      // `::` is a namespace alias, never a conditional arm separator.
      if (scanText[cursor - 1] === ':' || scanText[cursor + 1] === ':') cursor -= 1
      continue
    }
    if (char === '?') {
      // `?.`, `?[` and `??` are null-conditional / null-coalescing, not a conditional operator.
      if (scanText[cursor + 1] === '.' || scanText[cursor + 1] === '[') continue
      if (scanText[cursor + 1] === '?' || scanText[cursor - 1] === '?') continue
      return cursor
    }
  }
  return null
}

/**
 * The receiver of a `.ErrorMessage` / `?.ErrorMessage` read: `result` in both `result.ErrorMessage`
 * and `result?.ErrorMessage`. Empty when the receiver is not a plain identifier chain.
 */
function receiverOf(scanText, dotIndex) {
  let cursor = dotIndex - 1
  while (cursor >= 0 && /\s/.test(scanText[cursor])) cursor -= 1
  if (scanText[cursor] === '?') {
    cursor -= 1
    while (cursor >= 0 && /\s/.test(scanText[cursor])) cursor -= 1
  }
  const end = cursor + 1
  let start = end
  while (start > 0 && /[\w.]/.test(scanText[start - 1])) start -= 1
  return scanText.slice(start, end)
}

/**
 * The one reviewed exemption for rule 1: a conditional expression whose condition tests THIS
 * result's `ErrorCode` against `ErrorCodes.UnexpectedError` and whose other arm supplies
 * `GenericUnexpectedFailureMessage`. Anything else — a different receiver, a named argument, a
 * null-conditional read, a sibling argument next to such a ternary — is checked normally.
 */
function isReviewedGuardedTernaryArm(scanText, occurrenceIndex) {
  const receiver = receiverOf(scanText, occurrenceIndex)
  if (!receiver) return false

  const questionIndex = enclosingTernaryQuestion(scanText, occurrenceIndex)
  if (questionIndex === null) return false

  const condition = scanText.slice(subExpressionStart(scanText, questionIndex), questionIndex)
  if (!condition.includes('ErrorCodes.UnexpectedError')) return false

  const errorCodeRead = new RegExp(`\\b${escapeRegExp(receiver)}\\s*\\??\\s*\\.\\s*ErrorCode\\b`)
  if (!errorCodeRead.test(condition)) return false

  return scanText.slice(questionIndex).includes('GenericUnexpectedFailureMessage')
}

/**
 * Every `.ErrorMessage` occurrence in `scanText` that no sanitizer wraps and that the reviewed
 * ternary exemption does not cover.
 */
function rawErrorMessageOccurrences(scanText) {
  const occurrences = []
  const pattern = /\.\s*ErrorMessage\b/g
  let match
  while ((match = pattern.exec(scanText)) !== null) {
    if (wrappedBySanitizer(enclosingCallees(scanText, match.index))) continue
    if (isReviewedGuardedTernaryArm(scanText, match.index)) continue
    occurrences.push(match.index)
  }
  return occurrences
}

/**
 * The first use of a laundered local in `scanText` that no sanitizer wraps, or -1. Member accesses
 * (`other.detail`), calls (`detail(...)`) and reassignments are not uses of the local's value.
 */
function launderedUseIndex(scanText, name) {
  const pattern = new RegExp(`\\b${escapeRegExp(name)}\\b`, 'g')
  let match
  while ((match = pattern.exec(scanText)) !== null) {
    if (scanText[match.index - 1] === '.') continue
    const after = scanText.slice(match.index + name.length)
    if (/^\s*\(/.test(after)) continue
    if (/^\s*=(?!=)/.test(after)) continue
    if (wrappedBySanitizer(enclosingCallees(scanText, match.index))) continue
    return match.index
  }
  return -1
}

/**
 * Rule 1 — MCP payloads. Evaluated per `.ErrorMessage` OCCURRENCE, not per statement: a sanitized
 * sibling member in the same anonymous object does not make its raw neighbour safe. A local that
 * takes a raw `.ErrorMessage` is followed for ONE hop inside the block that declares it, so
 * `var m = result.ErrorMessage; return Error(m);` is caught; the local is forgotten when its block
 * closes, and a copy of a copy is not chased.
 */
export function findMcpFindings(source, path) {
  const findings = []
  const laundered = new Map()
  let blockDepth = 0

  const report = (statement, text, index, message) => {
    const lineText = lineTextAt(text, index)
    if (isAllowlisted(path, lineText)) return
    findings.push({
      rule: 'mcp-error-message',
      path,
      line: statement.startLine + lineNumberAt(text, index) - 1,
      message,
    })
  }

  for (const statement of splitStatements(source)) {
    const text = statement.text
    const logging = isLogging(text)

    // Parens inside plain string text would confuse the outward walk, so blank literals — but keep
    // interpolation holes, where a raw `.ErrorMessage` is a real leak.
    const scanText = maskLiterals(text, { preserveInterpolation: true })

    const outbound =
      !logging &&
      (/^\s*(?:return|throw)\b/m.test(text) ||
        /\bError\s*\(/.test(text) ||
        /JsonSerializer\.Serialize\s*\(/.test(text))

    const occurrences = logging ? [] : rawErrorMessageOccurrences(scanText)

    if (outbound) {
      for (const index of occurrences) {
        report(
          statement,
          text,
          index,
          'MCP payload returns or throws a raw ErrorMessage; wrap this occurrence in ' +
            'SensitiveDataRedactor.SanitizeLlmFailureMessage or a helper that applies it',
        )
      }

      for (const name of laundered.keys()) {
        const index = launderedUseIndex(scanText, name)
        if (index === -1) continue
        report(
          statement,
          text,
          index,
          `MCP payload returns or throws '${name}', a local that was assigned a raw ErrorMessage; ` +
            'wrap that value in SensitiveDataRedactor.SanitizeLlmFailureMessage or a helper that ' +
            'applies it',
        )
      }
    }

    const assignment = LOCAL_ASSIGNMENT.exec(scanText)
    if (assignment) {
      const name = assignment[1]
      if (!outbound && !logging && occurrences.length > 0) laundered.set(name, blockDepth)
      else laundered.delete(name)
    }

    blockDepth += braceDelta(maskLiterals(text))
    if (blockDepth < 0) blockDepth = 0
    for (const [name, depth] of laundered) {
      if (blockDepth < depth) laundered.delete(name)
    }
  }

  return findings
}

function isLogging(text) {
  return LOGGING_PATTERN.test(text)
}

/**
 * Rule 2 — unknown-exception text. Inside a `catch (Exception ex)` block, exception text may not be
 * carried into a persisted property, a DTO, or an MCP payload. Sanitization is judged per
 * occurrence with the same outward callee walk rule 1 uses, so a sanitizer applied to a different
 * value in the same statement does not excuse a raw `ex.Message` beside it. Known-domain catches
 * are never inspected, because the catch filter itself is what makes their message curated.
 */
export function findCatchBlockFindings(source, path, rule = 'unknown-exception-text') {
  const findings = []
  const lines = source.split(/\r?\n/)
  const masked = lines.map((line) => maskLiterals(line))
  const seen = new Set()

  for (let index = 0; index < lines.length; index += 1) {
    const match = /catch\s*\(\s*(?:System\.)?Exception\s+(\w+)\s*\)/.exec(masked[index])
    if (!match) continue
    const variable = match[1]

    // Walk the block by brace balance from the first `{` at or after the catch line.
    let depth = 0
    let started = false
    let end = index
    for (let cursor = index; cursor < lines.length; cursor += 1) {
      for (const char of masked[cursor]) {
        if (char === '{') {
          depth += 1
          started = true
        } else if (char === '}') {
          depth -= 1
        }
      }
      end = cursor
      if (started && depth <= 0) break
    }

    const blockSource = lines.slice(index, end + 1).join('\n')
    const escaped = escapeRegExp(variable)
    const leakPattern = new RegExp(
      `\\b${escaped}\\s*[?.]\\s*(?:Message|StackTrace|ToString\\s*\\()`,
      'g',
    )

    for (const statement of splitStatements(blockSource)) {
      const text = statement.text
      if (isLogging(text)) continue

      // Search the interpolation-preserving view so `$"...{ex.Message}"` is caught.
      const scanText = maskLiterals(text, { preserveInterpolation: true })

      leakPattern.lastIndex = 0
      let found
      while ((found = leakPattern.exec(scanText)) !== null) {
        if (wrappedByExceptionTextSanitizer(enclosingCallees(scanText, found.index))) continue

        const lineText = lineTextAt(text, found.index)
        if (isAllowlisted(path, lineText)) continue

        const absoluteLine = index + statement.startLine - 1 + lineNumberAt(text, found.index) - 1
        const key = `${path}:${absoluteLine}`
        if (seen.has(key)) continue
        seen.add(key)

        findings.push({
          rule,
          path,
          line: absoluteLine,
          message:
            `catch (Exception ${variable}) carries unknown-exception text outward without ` +
            'SensitiveDataRedactor.GenericUnexpectedFailureMessage or a SensitiveDataRedactor helper',
        })
      }
    }
  }

  return findings
}

// Retained name for the persisted-state region.
export const findPersistedStateFindings = (source, path) =>
  findCatchBlockFindings(source, path, 'persisted-unknown-failure')

async function listCsharpFiles(directory) {
  let entries
  try {
    entries = await readdir(directory, { withFileTypes: true })
  } catch {
    return []
  }
  const files = []
  for (const entry of entries) {
    const full = join(directory, entry.name)
    if (entry.isDirectory()) files.push(...(await listCsharpFiles(full)))
    else if (entry.name.endsWith('.cs')) files.push(full)
  }
  return files.sort()
}

export async function scanTree(root = repoRoot) {
  const findings = []

  for (const file of await listCsharpFiles(resolve(root, MCP_DIRECTORY))) {
    const relativePath = relative(root, file).split(sep).join('/')
    const source = await readFile(file, 'utf8')
    findings.push(...findMcpFindings(source, relativePath))
    findings.push(...findCatchBlockFindings(source, relativePath, 'mcp-unknown-exception-text'))
  }

  for (const relativePath of PERSISTED_STATE_FILES) {
    let source
    try {
      source = await readFile(resolve(root, relativePath), 'utf8')
    } catch {
      findings.push({
        rule: 'persisted-unknown-failure',
        path: relativePath,
        line: 0,
        message: 'guarded persisted-state file is missing; update PERSISTED_STATE_FILES if it moved',
      })
      continue
    }
    findings.push(...findPersistedStateFindings(source, relativePath))
  }

  return findings
}

async function main() {
  const findings = await scanTree()

  if (findings.length > 0) {
    console.error('Unknown-exception boundary check failed:')
    for (const finding of findings) {
      console.error(`- ${finding.path}:${finding.line} [${finding.rule}] ${finding.message}`)
    }
    console.error('See docs/security/UNKNOWN_EXCEPTION_SURFACE_INVENTORY.md for the reviewed surfaces.')
    process.exit(1)
  }

  console.log('Unknown-exception boundary check passed.')
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch((error) => {
    console.error('Unknown-exception boundary check crashed:', error)
    process.exit(1)
  })
}
