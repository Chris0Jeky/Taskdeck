#!/usr/bin/env node

// Focused guard for issue #2351: block NEW raw unknown-exception flows from reaching an
// LLM-facing MCP payload or a persisted worker/agent/command failure field.
//
// Two rules, both deliberately narrow:
//   1. `mcp-error-message` — in MCP files, each raw `.ErrorMessage` member expression reached in a
//      return/throw/serialize position must itself be wrapped in a sanitizing call.
//   2. `unknown-exception-text` — in MCP files and in the listed persisted-state files, a
//      `catch (Exception ex)` block may not carry `ex.Message` / `ex.StackTrace` / `ex.ToString()`
//      outward.
//
// Neither rule inspects log statements, and neither inspects known-domain catches
// (`catch (DomainException ex)`), because the shipped trust model deliberately passes curated
// domain messages through. Everything outside these two regions is out of scope by design; the
// residuals that sit outside them are recorded in the inventory rather than suppressed here.
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

// Statement-level tokens that mark an already-generalized unknown-exception value. Used by rule 2,
// where the sanitized form replaces the exception text outright rather than wrapping it.
export const SANITIZER_TOKENS = [
  'SanitizeLlmFailureMessage',
  'GenericUnexpectedFailureMessage',
  'SummarizeException',
  'PublicFailureMessage',
  'SafeExceptionDescription',
  'SensitiveDataRedactor.',
  'LogSanitizer.',
]

// Logging is explicitly out of scope: structured logs are a trusted operator sink.
const LOGGING_PATTERN = /(?:_logger|\bLogger)\s*[?.]|\bLog(?:Error|Warning|Information|Debug|Critical|Trace)\s*\(/

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

/**
 * Blank out string literals and line comments so brace/paren depth counting is not thrown off by
 * punctuation inside message text. Length is preserved so offsets stay aligned with the source.
 * Interpolation holes (`{ ... }` inside a `$"..."`) are NOT blanked: an interpolated
 * `.ErrorMessage` is exactly the flow this guard exists to catch.
 */
export function maskLiterals(text, { preserveInterpolation = false } = {}) {
  let masked = ''
  let inString = false
  let inChar = false
  let holeDepth = 0

  for (let index = 0; index < text.length; index += 1) {
    const char = text[index]

    if (char === '\n') {
      masked += char
      inString = false
      inChar = false
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

    if (!inChar && char === '"' && text[index - 1] !== '\\') {
      inString = !inString
      masked += '"'
      continue
    }
    if (!inString && char === "'" && text[index - 1] !== '\\') {
      inChar = !inChar
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
 * Walk outward from `index`, collecting the callee name of every call expression that encloses it.
 * `Error(SensitiveDataRedactor.SanitizeLlmFailureMessage(code, result.ErrorMessage))` yields
 * ['SanitizeLlmFailureMessage', 'Error'].
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
    if (name) callees.push(name.split('.').pop())
  }

  return callees
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

/**
 * Rule 1 — MCP payloads. Evaluated per `.ErrorMessage` OCCURRENCE, not per statement: a sanitized
 * sibling member in the same anonymous object does not make its raw neighbour safe.
 */
export function findMcpFindings(source, path) {
  const findings = []

  for (const statement of splitStatements(source)) {
    const text = statement.text
    if (isLogging(text)) continue

    const outbound =
      /^\s*(?:return|throw)\b/m.test(text) ||
      /\bError\s*\(/.test(text) ||
      /JsonSerializer\.Serialize\s*\(/.test(text)
    if (!outbound) continue

    // Parens inside plain string text would confuse the outward walk, so blank literals — but keep
    // interpolation holes, where a raw `.ErrorMessage` is a real leak.
    const scanText = maskLiterals(text, { preserveInterpolation: true })

    // The reviewed ternary in ProposalTools swaps in the generic message for the UnexpectedError
    // code and keeps curated domain text otherwise. It is a named, reviewed shape, not a wrapper.
    const guardedTernary =
      text.includes('ErrorCodes.UnexpectedError') && text.includes('GenericUnexpectedFailureMessage')

    const pattern = /\.\s*ErrorMessage\b/g
    let match
    while ((match = pattern.exec(scanText)) !== null) {
      const occurrenceIndex = match.index
      const callees = enclosingCallees(scanText, occurrenceIndex)
      if (callees.some((callee) => SANITIZER_CALLS.has(callee))) continue

      const before = scanText.slice(0, occurrenceIndex).trimEnd()
      const isTernaryArm = before.endsWith(':') || before.endsWith('?')
      if (guardedTernary && isTernaryArm) continue

      const lineText = lineTextAt(text, occurrenceIndex)
      if (isAllowlisted(path, lineText)) continue

      findings.push({
        rule: 'mcp-error-message',
        path,
        line: statement.startLine + lineNumberAt(text, occurrenceIndex) - 1,
        message:
          'MCP payload returns or throws a raw ErrorMessage; wrap this occurrence in ' +
          'SensitiveDataRedactor.SanitizeLlmFailureMessage or a helper that applies it',
      })
    }
  }

  return findings
}

function isSanitized(text) {
  return SANITIZER_TOKENS.some((token) => text.includes(token))
}

function isLogging(text) {
  return LOGGING_PATTERN.test(text)
}

/**
 * Rule 2 — unknown-exception text. Inside a `catch (Exception ex)` block, exception text may not be
 * carried into a persisted property, a DTO, or an MCP payload. Known-domain catches are never
 * inspected, because the catch filter itself is what makes their message curated.
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
    const escaped = variable.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    const leakPattern = new RegExp(`\\b${escaped}\\s*[?.]\\s*(?:Message|StackTrace|ToString\\s*\\()`)

    for (const statement of splitStatements(blockSource)) {
      const text = statement.text
      if (isLogging(text)) continue
      if (isSanitized(text)) continue

      // Search the interpolation-preserving view so `$"...{ex.Message}"` is caught.
      const scanText = maskLiterals(text, { preserveInterpolation: true })
      const found = leakPattern.exec(scanText)
      if (!found) continue

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
