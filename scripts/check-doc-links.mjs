#!/usr/bin/env node

/**
 * Repository-relative Markdown link check.
 *
 * The checker resolves local link and image targets without touching the
 * network. Anchors are intentionally checked only as far as the file half.
 */

import { readdirSync, readFileSync, existsSync } from 'node:fs'
import { join, resolve, dirname, relative, sep, isAbsolute } from 'node:path'
import { fileURLToPath } from 'node:url'

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')

/** Directories never worth walking: build output, dependencies, and parked checkouts. */
export const skippedDirectories = new Set([
  '.git',
  '.worktrees',
  'node_modules',
  'bin',
  'obj',
  'dist',
  'artifacts',
  'coverage',
  'TestResults',
  'playwright-report',
  'test-results',
])

function lineNumberAt(text, index) {
  let line = 1
  for (let cursor = 0; cursor < index; cursor += 1) {
    if (text[cursor] === '\n') line += 1
  }
  return line
}

function maskRange(buffer, source, start, end) {
  for (let index = start; index < end; index += 1) {
    if (source[index] !== '\n') buffer[index] = ' '
  }
}

function backtickRunLength(text, start) {
  let end = start
  while (text[end] === '`') end += 1
  return end - start
}

function isEscaped(text, index) {
  let backslashes = 0
  for (let cursor = index - 1; cursor >= 0 && text[cursor] === '\\'; cursor -= 1) {
    backslashes += 1
  }
  return backslashes % 2 === 1
}

function firstBlankLineAtOrAfter(text, start) {
  const pattern = /\n[ \t\r]*\n/g
  pattern.lastIndex = start
  return pattern.exec(text)?.index ?? -1
}

/**
 * Mask fenced blocks and balanced inline code while preserving every offset.
 *
 * A malformed inline span never masks across a blank line. Unterminated spans
 * and fences still produce diagnostics so a stale delimiter cannot silently
 * hide a large region from the checker. Diagnostics are warnings rather than
 * broken-link failures because historical prose may be malformed while every
 * local target remains valid.
 */
export function maskCodeWithDiagnostics(markdown) {
  const buffer = markdown.split('')
  const diagnostics = []
  let fence = null
  let lineStart = 0
  let line = 1

  while (lineStart < markdown.length) {
    const newline = markdown.indexOf('\n', lineStart)
    const lineEnd = newline === -1 ? markdown.length : newline
    const rawLine = markdown.slice(lineStart, lineEnd)
    const comparableLine = rawLine.endsWith('\r') ? rawLine.slice(0, -1) : rawLine
    const leftTrimmed = comparableLine.replace(/^[ \t]*/, '')

    if (fence) {
      maskRange(buffer, markdown, lineStart, lineEnd)
      const trimmed = comparableLine.trim()
      if (
        trimmed.length >= fence.length &&
        [...trimmed].every((character) => character === fence.character)
      ) {
        fence = null
      }
    } else {
      const opening = /^(`{3,}|~{3,})/.exec(leftTrimmed)
      if (opening) {
        fence = {
          character: opening[1][0],
          length: opening[1].length,
          line,
          target: opening[1],
        }
        maskRange(buffer, markdown, lineStart, lineEnd)
      }
    }

    if (newline === -1) break
    lineStart = newline + 1
    line += 1
  }

  if (fence) {
    diagnostics.push({
      line: fence.line,
      target: fence.target,
      reason: 'unterminated fenced code block',
    })
  }

  const fenceMasked = buffer.join('')
  let cursor = 0
  while (cursor < fenceMasked.length) {
    if (fenceMasked[cursor] !== '`' || isEscaped(fenceMasked, cursor)) {
      cursor += 1
      continue
    }

    const runLength = backtickRunLength(fenceMasked, cursor)
    const marker = '`'.repeat(runLength)
    const contentStart = cursor + runLength
    const blankLine = firstBlankLineAtOrAfter(fenceMasked, contentStart)
    const searchLimit = blankLine === -1 ? fenceMasked.length : blankLine
    let search = contentStart
    let closing = -1

    while (search < searchLimit) {
      const candidate = fenceMasked.indexOf('`', search)
      if (candidate === -1 || candidate >= searchLimit) break
      const candidateLength = backtickRunLength(fenceMasked, candidate)
      // Backslashes are literal inside a code span, so they do not escape the
      // matching closing delimiter.
      if (candidateLength === runLength) {
        closing = candidate
        break
      }
      search = candidate + candidateLength
    }

    if (closing === -1) {
      diagnostics.push({
        line: lineNumberAt(markdown, cursor),
        target: marker,
        reason: 'unbalanced inline code span',
      })
      cursor = blankLine === -1 ? fenceMasked.length : blankLine + 1
      continue
    }

    const end = closing + runLength
    maskRange(buffer, markdown, cursor, end)
    cursor = end
  }

  return { masked: buffer.join(''), diagnostics }
}

/** Backwards-compatible convenience for callers that need only masked text. */
export function maskCode(markdown) {
  return maskCodeWithDiagnostics(markdown).masked
}

/** Targets that name something other than a path in this repository. */
export function isExternalTarget(target) {
  return (
    target === '' ||
    /^[a-z][a-z0-9+.-]*:/i.test(target) ||
    target.startsWith('//') ||
    target.startsWith('#')
  )
}

function findLabelEnd(markdown, start) {
  let depth = 1
  for (let index = start + 1; index < markdown.length; index += 1) {
    if (markdown[index] === '\\') {
      index += 1
      continue
    }
    if (markdown[index] === '[') depth += 1
    else if (markdown[index] === ']') {
      depth -= 1
      if (depth === 0) return index
    }
  }
  return -1
}

function skipWhitespace(text, start) {
  let cursor = start
  while (cursor < text.length && /\s/.test(text[cursor])) cursor += 1
  return cursor
}

function findInlineClosingParen(text, start) {
  let cursor = skipWhitespace(text, start)
  if (text[cursor] === ')') return cursor

  if (text[cursor] === '"' || text[cursor] === "'") {
    const quote = text[cursor]
    cursor += 1
    while (cursor < text.length) {
      if (text[cursor] === '\\') cursor += 2
      else if (text[cursor] === quote) {
        cursor = skipWhitespace(text, cursor + 1)
        return text[cursor] === ')' ? cursor : -1
      } else cursor += 1
    }
    return -1
  }

  if (text[cursor] === '(') {
    let depth = 1
    cursor += 1
    while (cursor < text.length) {
      if (text[cursor] === '\\') {
        cursor += 2
        continue
      }
      if (text[cursor] === '(') depth += 1
      else if (text[cursor] === ')') {
        depth -= 1
        if (depth === 0) {
          cursor = skipWhitespace(text, cursor + 1)
          return text[cursor] === ')' ? cursor : -1
        }
      }
      cursor += 1
    }
  }

  return -1
}

function parseInlineDestination(text, openParen) {
  let cursor = skipWhitespace(text, openParen + 1)

  if (text[cursor] === '<') {
    const targetStart = cursor + 1
    cursor = targetStart
    while (cursor < text.length && text[cursor] !== '>' && text[cursor] !== '\n') {
      if (text[cursor] === '\\') cursor += 2
      else cursor += 1
    }
    if (text[cursor] !== '>') return null
    const closing = findInlineClosingParen(text, cursor + 1)
    if (closing === -1) return null
    return { target: text.slice(targetStart, cursor), closing }
  }

  const targetStart = cursor
  let depth = 0
  while (cursor < text.length) {
    if (text[cursor] === '\\') {
      cursor += 2
      continue
    }
    if (text[cursor] === '(') {
      depth += 1
      cursor += 1
      continue
    }
    if (text[cursor] === ')') {
      if (depth === 0) {
        return { target: text.slice(targetStart, cursor), closing: cursor }
      }
      depth -= 1
      cursor += 1
      continue
    }
    if (/\s/.test(text[cursor]) && depth === 0) {
      const closing = findInlineClosingParen(text, cursor)
      if (closing === -1) return null
      return { target: text.slice(targetStart, cursor), closing }
    }
    cursor += 1
  }
  return null
}

function parseReferenceDestination(text) {
  let cursor = skipWhitespace(text, 0)
  if (text[cursor] === '<') {
    const end = text.indexOf('>', cursor + 1)
    return end === -1 ? null : text.slice(cursor + 1, end)
  }

  const start = cursor
  let depth = 0
  while (cursor < text.length) {
    if (text[cursor] === '\\') {
      cursor += 2
      continue
    }
    if (text[cursor] === '(') depth += 1
    else if (text[cursor] === ')' && depth > 0) depth -= 1
    else if (/\s/.test(text[cursor]) && depth === 0) break
    cursor += 1
  }
  return text.slice(start, cursor)
}

function extractLocalTargetsFromMasked(masked) {
  const found = []
  let sequence = 0

  const push = (rawTarget, index) => {
    let target = rawTarget.trim()
    target = target.replace(/\\([\\()[\]<> ])/g, '$1')
    if (isExternalTarget(target)) return
    const pathPart = target.split('#')[0].split('?')[0]
    if (pathPart === '') return
    found.push({ target, pathPart, line: lineNumberAt(masked, index), index, sequence })
    sequence += 1
  }

  // Parse inline links instead of using a single regular expression so nested
  // image labels and balanced parentheses in destinations remain visible.
  for (let index = 0; index < masked.length; index += 1) {
    let bracketStart = -1
    if (masked[index] === '!' && masked[index + 1] === '[') bracketStart = index + 1
    else if (masked[index] === '[' && masked[index - 1] !== '!') bracketStart = index
    if (bracketStart === -1) continue

    const labelEnd = findLabelEnd(masked, bracketStart)
    if (labelEnd === -1 || masked[labelEnd + 1] !== '(') continue
    const destination = parseInlineDestination(masked, labelEnd + 1)
    if (destination) push(destination.target, index)
  }

  // A reference-style link's path lives in its definition, so validating every
  // local definition covers both links and images without resolving labels.
  const definitionPattern = /^[ \t]{0,3}\[[^\]\n]+\]:[ \t]*(.*)$/gm
  let definition
  while ((definition = definitionPattern.exec(masked)) !== null) {
    const target = parseReferenceDestination(definition[1])
    if (target !== null) push(target, definition.index)
  }

  const tagPattern = /<(a|img)\b[^>]*>/gi
  let tag
  while ((tag = tagPattern.exec(masked)) !== null) {
    const attributeName = tag[1].toLowerCase() === 'a' ? 'href' : 'src'
    const attributePattern = new RegExp(
      `\\s${attributeName}\\s*=\\s*(?:"([^"]*)"|'([^']*)'|([^\\s>]+))`,
      'i',
    )
    const attribute = attributePattern.exec(tag[0])
    if (!attribute) continue
    const target = attribute[1] ?? attribute[2] ?? attribute[3] ?? ''
    const valueOffset = attribute[0].indexOf(target)
    push(target, tag.index + attribute.index + Math.max(0, valueOffset))
  }

  return found
    .sort((left, right) => left.index - right.index || left.sequence - right.sequence)
    .map(({ target, pathPart, line }) => ({ target, pathPart, line }))
}

/** Extract every repository-relative link target, with the line it sits on. */
export function extractLocalTargets(markdown) {
  return extractLocalTargetsFromMasked(maskCode(markdown))
}

/**
 * Directory listings, cached for one check, used for the case-exact existence
 * check. An unreadable directory caches as null so the caller can fall back to
 * existsSync instead of retrying it for every link.
 */
function readDirectoryCached(directory, directoryCache) {
  if (!directoryCache.has(directory)) {
    try {
      directoryCache.set(directory, new Set(readdirSync(directory)))
    } catch {
      directoryCache.set(directory, null)
    }
  }
  return directoryCache.get(directory)
}

/** Case-exact existence, because Windows and macOS existsSync can be lenient. */
export function existsCaseExact(target, root, directoryCache = new Map()) {
  if (!existsSync(target)) return false
  const relativePath = relative(root, target)
  if (relativePath === '') return true
  let current = root
  for (const segment of relativePath.split(sep)) {
    const entries = readDirectoryCached(current, directoryCache)
    if (entries === null) return true
    if (!entries.has(segment)) return false
    current = join(current, segment)
  }
  return true
}

/** Every Markdown file in the working tree, repo-relative, in stable order. */
export function collectMarkdownFiles(root = repoRoot) {
  const files = []
  const walk = (directory) => {
    let entries
    try {
      entries = readdirSync(directory, { withFileTypes: true })
    } catch {
      return
    }
    for (const entry of entries.sort((a, b) => a.name.localeCompare(b.name))) {
      if (skippedDirectories.has(entry.name)) continue
      const full = join(directory, entry.name)
      if (entry.isDirectory()) walk(full)
      else if (entry.name.toLowerCase().endsWith('.md')) files.push(full)
    }
  }
  walk(root)
  return files
}

/** Resolve one target against the file that declared it; null when it resolves. */
export function resolveTarget(
  sourceFile,
  pathPart,
  root = repoRoot,
  directoryCache = new Map(),
) {
  let decoded = pathPart
  try {
    decoded = decodeURIComponent(pathPart)
  } catch {
    // A target that is not valid percent-encoding is used as written.
  }
  const base = decoded.startsWith('/') ? join(root, decoded.slice(1)) : join(dirname(sourceFile), decoded)
  const target = resolve(base)
  const inside = relative(root, target)
  if (inside.startsWith('..') || isAbsolute(inside)) {
    return { reason: 'outside the repository' }
  }
  if (!existsSync(target)) return { reason: 'missing' }
  if (!existsCaseExact(target, root, directoryCache)) return { reason: 'wrong case' }
  return null
}

/** Scan once so the CLI can report link failures and masking warnings together. */
export function scanDocumentation(root = repoRoot) {
  const files = collectMarkdownFiles(root)
  const broken = []
  const diagnostics = []
  const directoryCache = new Map()

  for (const file of files) {
    let contents
    try {
      contents = readFileSync(file, 'utf8')
    } catch {
      continue
    }

    const result = maskCodeWithDiagnostics(contents)
    const fileName = relative(root, file).split(sep).join('/')
    diagnostics.push(...result.diagnostics.map((diagnostic) => ({ file: fileName, ...diagnostic })))

    const fileFindings = []
    for (const { target, pathPart, line } of extractLocalTargetsFromMasked(result.masked)) {
      const failure = resolveTarget(file, pathPart, root, directoryCache)
      if (failure) {
        fileFindings.push({ file: fileName, line, target, reason: failure.reason })
      }
    }
    fileFindings.sort((left, right) => left.line - right.line || left.target.localeCompare(right.target))
    broken.push(...fileFindings)
  }

  return { files, broken, diagnostics }
}

export function findBrokenLinks(root = repoRoot) {
  return scanDocumentation(root).broken
}

export function findMaskingDiagnostics(root = repoRoot) {
  return scanDocumentation(root).diagnostics
}

/** Render findings the way both the CLI and the test suite should report them. */
export function formatBrokenLinks(findings) {
  return findings.map(
    ({ file, line, target, reason }) => `${file}:${line} -> ${target} (${reason})`,
  )
}

function main() {
  const { files, broken, diagnostics } = scanDocumentation()

  if (diagnostics.length > 0) {
    console.warn('Doc link masking warnings:')
    for (const line of formatBrokenLinks(diagnostics)) {
      console.warn(`- ${line}`)
    }
    console.warn(
      `\n${diagnostics.length} malformed code delimiter warning(s); links outside masked spans were still checked.`,
    )
  }

  if (broken.length > 0) {
    console.error('Doc link check failed:')
    for (const line of formatBrokenLinks(broken)) {
      console.error(`- ${line}`)
    }
    console.error(
      `\n${broken.length} broken repository-relative link(s) across ${files.length} Markdown files.`,
    )
    process.exit(1)
  }

  console.log(
    `Doc link check passed (${files.length} Markdown files, 0 broken relative links${
      diagnostics.length > 0 ? `, ${diagnostics.length} masking warning(s)` : ''
    }).`,
  )
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    main()
  } catch (error) {
    console.error('Doc link check crashed:', error)
    process.exit(1)
  }
}

export { repoRoot }
