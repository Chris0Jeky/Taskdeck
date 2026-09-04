#!/usr/bin/env node

// Focused guard for issue #2351: block NEW raw unknown-exception flows from reaching an
// LLM-facing MCP payload or a persisted worker/agent/command failure field.
//
// Deliberately narrow. It only inspects two reviewed regions and only rejects statements that
// carry unknown-exception text outward without a sanitizer. It never inspects log statements
// and never inspects known-domain catches (`catch (DomainException ex)`), because the shipped
// trust model deliberately passes curated domain messages through.
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

// A statement carrying any of these is already routed through the reviewed sanitizing helpers.
export const SANITIZER_TOKENS = [
  'SanitizeLlmFailureMessage',
  'GenericUnexpectedFailureMessage',
  'SummarizeException',
  'PublicFailureMessage',
  'SensitiveDataRedactor.',
]

// Logging is explicitly out of scope: structured logs are a trusted operator sink.
const LOGGING_PATTERN = /(?:_logger|\bLogger)\s*[?.]|\bLog(?:Error|Warning|Information|Debug|Critical|Trace)\s*\(/

// Known deliberate cases, each with the reason it is safe and where that was decided.
export const ALLOWLIST = [
  {
    path: 'backend/src/Taskdeck.Api/Mcp/CaptureResources.cs',
    pattern: 'errorMessage = c.ErrorMessage',
    issue: '#2443',
    reason:
      'Capture.ErrorMessage is sanitized on the write side before it is ever persisted ' +
      '(LlmQueueToProposalWorker.cs SanitizeLlmFailureMessage, TranscriptTriageWorker.cs ' +
      'SanitizeLlmFailureMessage), so the read projection replays an already-safe stored value.',
  },
]

function isAllowlisted(path, statementText) {
  return ALLOWLIST.some((entry) => path.endsWith(entry.path) && statementText.includes(entry.pattern))
}

// Blank out string literals and line comments so brace/paren depth counting is not thrown off
// by punctuation inside message text. Length is preserved so offsets stay aligned.
function maskLiterals(line) {
  let masked = ''
  let inString = false
  let inChar = false
  for (let index = 0; index < line.length; index += 1) {
    const char = line[index]
    if (!inString && !inChar && char === '/' && line[index + 1] === '/') {
      masked += ' '.repeat(line.length - index)
      return masked
    }
    if (!inChar && char === '"' && line[index - 1] !== '\\') {
      inString = !inString
      masked += '"'
      continue
    }
    if (!inString && char === "'" && line[index - 1] !== '\\') {
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

function isSanitized(text) {
  return SANITIZER_TOKENS.some((token) => text.includes(token))
}

function isLogging(text) {
  return LOGGING_PATTERN.test(text)
}

/**
 * Rule 1 — MCP payloads. An unknown failure message may not leave an MCP tool or resource
 * unless the same statement routes it through a sanitizing helper.
 */
export function findMcpFindings(source, path) {
  const findings = []
  for (const statement of splitStatements(source)) {
    const text = statement.text
    if (!/\.\s*ErrorMessage\b/.test(text)) continue
    if (isLogging(text)) continue
    if (isSanitized(text)) continue

    const outbound =
      /^\s*(?:return|throw)\b/m.test(text) ||
      /\bError\s*\(/.test(text) ||
      /JsonSerializer\.Serialize\s*\(/.test(text)
    if (!outbound) continue
    if (isAllowlisted(path, text)) continue

    findings.push({
      rule: 'mcp-error-message',
      path,
      line: statement.startLine,
      message:
        'MCP payload returns or throws ErrorMessage without SensitiveDataRedactor.SanitizeLlmFailureMessage ' +
        'or an Error(Result) helper that applies it',
    })
  }
  return findings
}

/**
 * Rule 2 — persisted failure state. Inside a `catch (Exception ex)` block, exception text may not
 * be carried into a persisted property or DTO. Known-domain catches are never inspected, because
 * the catch filter itself is what makes their message curated.
 */
export function findPersistedStateFindings(source, path) {
  const findings = []
  const lines = source.split(/\r?\n/)
  const masked = lines.map(maskLiterals)
  const findingsSeen = new Set()

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
      if (!leakPattern.test(text)) continue
      if (isLogging(text)) continue
      if (isSanitized(text)) continue
      const absoluteLine = index + statement.startLine - 1
      if (isAllowlisted(path, text)) continue
      const key = `${path}:${absoluteLine}`
      if (findingsSeen.has(key)) continue
      findingsSeen.add(key)

      findings.push({
        rule: 'persisted-unknown-failure',
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
    findings.push(...findMcpFindings(await readFile(file, 'utf8'), relativePath))
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
