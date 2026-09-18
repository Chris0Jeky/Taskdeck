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

function createLineNumberResolver(text) {
  const lineStarts = [0]
  for (let index = 0; index < text.length; index += 1) {
    if (text[index] === '\n') lineStarts.push(index + 1)
  }

  return (index) => {
    let low = 0
    let high = lineStarts.length
    while (low < high) {
      const middle = Math.floor((low + high) / 2)
      if (lineStarts[middle] <= index) low = middle + 1
      else high = middle
    }
    return low
  }
}

function maskRange(buffer, source, start, end) {
  for (let index = start; index < end; index += 1) {
    if (source[index] !== '\n') buffer[index] = ' '
  }
}

function boundedDelimiter(character, length) {
  if (length <= 32) return character.repeat(length)
  return `${character.repeat(32)}… (${length} characters)`
}

function firstBlankLine(text, start) {
  const pattern = /\n[ \t\r]*\n/g
  pattern.lastIndex = start
  return pattern.exec(text)
}

function maskHtmlComments(buffer, source, searchable, diagnostics, lineNumberAt) {
  let cursor = 0
  while (cursor < searchable.length) {
    const start = searchable.indexOf('<!--', cursor)
    if (start === -1) break

    const close = searchable.indexOf('-->', start + 4)
    if (close !== -1) {
      const end = close + 3
      maskRange(buffer, source, start, end)
      cursor = end
      continue
    }

    // A malformed comment must not silently hide the rest of the document.
    // Bound its masking at the next blank line, report it once, then resume.
    const blankLine = firstBlankLine(searchable, start + 4)
    const end = blankLine ? blankLine.index + 1 : source.length
    maskRange(buffer, source, start, end)
    diagnostics.push({
      line: lineNumberAt(start),
      target: '<!--',
      reason: 'unterminated HTML comment',
    })
    cursor = Math.max(end, start + 4)
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

function collectBacktickRuns(text, start, end) {
  const runs = []
  let cursor = start
  let openLength = null

  while (cursor < end) {
    const candidate = text.indexOf('`', cursor)
    if (candidate === -1 || candidate >= end) break
    if (isEscaped(text, candidate) && openLength === null) {
      cursor = candidate + 1
      continue
    }

    const length = backtickRunLength(text, candidate)
    runs.push({ start: candidate, length })
    if (openLength === null) openLength = length
    else if (openLength === length) openLength = null
    cursor = candidate + length
  }

  return runs
}

function maskInlineCodeBlock(buffer, source, searchable, start, end, diagnostics, lineNumberAt) {
  const runs = collectBacktickRuns(searchable, start, end)
  const nextSameLength = new Int32Array(runs.length)
  nextSameLength.fill(-1)
  const lastRunByLength = new Map()

  for (let index = runs.length - 1; index >= 0; index -= 1) {
    const run = runs[index]
    const next = lastRunByLength.get(run.length)
    if (next !== undefined) nextSameLength[index] = next
    lastRunByLength.set(run.length, index)
  }

  let index = 0
  while (index < runs.length) {
    const opening = runs[index]
    const closingIndex = nextSameLength[index]
    if (closingIndex === -1) {
      diagnostics.push({
        line: lineNumberAt(opening.start),
        target: boundedDelimiter('`', opening.length),
        reason: 'unbalanced inline code span',
      })
      index += 1
      continue
    }

    const closing = runs[closingIndex]
    maskRange(buffer, source, opening.start, closing.start + closing.length)
    index = closingIndex + 1
  }
}

function maskInlineCode(buffer, source, searchable, diagnostics, lineNumberAt) {
  const blankLinePattern = /\n[ \t\r]*\n/g
  let blockStart = 0
  let blankLine

  while ((blankLine = blankLinePattern.exec(searchable)) !== null) {
    maskInlineCodeBlock(
      buffer,
      source,
      searchable,
      blockStart,
      blankLine.index,
      diagnostics,
      lineNumberAt,
    )
    blockStart = blankLine.index + blankLine[0].length
  }

  maskInlineCodeBlock(
    buffer,
    source,
    searchable,
    blockStart,
    searchable.length,
    diagnostics,
    lineNumberAt,
  )
}

/**
 * Mask fenced blocks and balanced inline code while preserving every offset.
 *
 * A malformed inline span never masks across a blank line. Unterminated spans,
 * fences, and HTML comments produce bounded diagnostics so stale delimiters
 * cannot silently hide a large region from the checker. Diagnostics are
 * warnings rather than broken-link failures because historical prose may be
 * malformed while every local target remains valid.
 */
export function maskCodeWithDiagnostics(markdown) {
  const buffer = markdown.split('')
  const diagnostics = []
  const lineNumberAt = createLineNumberResolver(markdown)
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
          target: boundedDelimiter(opening[1][0], opening[1].length),
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
  maskHtmlComments(buffer, markdown, fenceMasked, diagnostics, lineNumberAt)
  const fenceAndCommentMasked = buffer.join('')
  maskInlineCode(buffer, markdown, fenceAndCommentMasked, diagnostics, lineNumberAt)

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

function buildLabelEndIndex(markdown) {
  const stack = []
  const ends = new Map()

  for (let index = 0; index < markdown.length; index += 1) {
    if (markdown[index] === '\\') {
      index += 1
      continue
    }
    if (markdown[index] === '[') stack.push(index)
    else if (markdown[index] === ']' && stack.length > 0) {
      ends.set(stack.pop(), index)
    }
  }

  return ends
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

function markdownContainerContentStart(line) {
  let cursor = 0

  while (cursor < line.length) {
    let indentation = 0
    while (indentation < 3 && (line[cursor] === ' ' || line[cursor] === '\t')) {
      cursor += 1
      indentation += 1
    }

    if (line[cursor] === '>') {
      cursor += 1
      if (line[cursor] === ' ' || line[cursor] === '\t') cursor += 1
      continue
    }

    const listMarker = /^(?:[-+*]|\d{1,9}[.)])[ \t]+/.exec(line.slice(cursor))
    if (listMarker) {
      cursor += listMarker[0].length
      continue
    }

    return cursor
  }

  return cursor
}

function extractReferenceDefinitions(masked, push) {
  let lineStart = 0

  while (lineStart <= masked.length) {
    const newline = masked.indexOf('\n', lineStart)
    const lineEnd = newline === -1 ? masked.length : newline
    const line = masked.slice(lineStart, lineEnd)
    const contentStart = markdownContainerContentStart(line)
    const content = line.slice(contentStart)
    const definition = /^\[(?!\^)[^\]\n]+\]:/.exec(content)

    if (definition) {
      const separatorEnd = definition[0].length
      const destinationStart = skipWhitespace(content, separatorEnd)
      let destinationText = content.slice(destinationStart)
      let destinationIndex = lineStart + contentStart + destinationStart

      if (destinationText.trim() === '' && newline !== -1) {
        const nextLineStart = newline + 1
        const nextLineEnd = masked.indexOf('\n', nextLineStart)
        const nextLine = masked.slice(
          nextLineStart,
          nextLineEnd === -1 ? masked.length : nextLineEnd,
        )
        const nextContentStart = markdownContainerContentStart(nextLine)
        const continuation = /^[ \t]*(\S.*)$/.exec(nextLine.slice(nextContentStart))
        if (continuation) {
          destinationText = continuation[1]
          destinationIndex =
            nextLineStart + nextContentStart + continuation[0].indexOf(destinationText)
        }
      }

      const target = parseReferenceDestination(destinationText)
      if (target !== null) push(target, destinationIndex)
    }

    if (newline === -1) break
    lineStart = newline + 1
  }
}

function parseHtmlTagStart(text, start) {
  let cursor = start + 1
  let opening = true

  if (text[cursor] === '/') {
    opening = false
    cursor += 1
  }

  if (text[cursor] === '!' || text[cursor] === '?') {
    return { name: null, opening: false, nameEnd: cursor + 1 }
  }

  if (!/[A-Za-z]/.test(text[cursor] ?? '')) return null
  const nameStart = cursor
  cursor += 1
  while (cursor < text.length && /[A-Za-z0-9:-]/.test(text[cursor])) cursor += 1

  return {
    name: text.slice(nameStart, cursor).toLowerCase(),
    opening,
    nameEnd: cursor,
  }
}

function findHtmlTagEnd(text, start) {
  let quote = null
  for (let cursor = start + 1; cursor < text.length; cursor += 1) {
    const character = text[cursor]
    if (quote) {
      if (character === quote) quote = null
    } else if (character === '"' || character === "'") {
      quote = character
    } else if (character === '>') {
      return cursor
    }
  }
  return -1
}

function findHtmlAttribute(tag, name, start) {
  const limit = tag.length - 1
  let cursor = start

  while (cursor < limit) {
    while (cursor < limit && /\s/.test(tag[cursor])) cursor += 1
    if (cursor >= limit || tag[cursor] === '/' || tag[cursor] === '>') break

    const nameStart = cursor
    while (cursor < limit && !/[\s=/>]/.test(tag[cursor])) cursor += 1
    if (cursor === nameStart) {
      cursor += 1
      continue
    }

    const attributeName = tag.slice(nameStart, cursor).toLowerCase()
    while (cursor < limit && /\s/.test(tag[cursor])) cursor += 1

    let value = null
    let valueStart = -1
    if (tag[cursor] === '=') {
      cursor += 1
      while (cursor < limit && /\s/.test(tag[cursor])) cursor += 1

      if (tag[cursor] === '"' || tag[cursor] === "'") {
        const quote = tag[cursor]
        cursor += 1
        valueStart = cursor
        while (cursor < limit && tag[cursor] !== quote) cursor += 1
        value = tag.slice(valueStart, cursor)
        if (tag[cursor] === quote) cursor += 1
      } else {
        valueStart = cursor
        while (cursor < limit && !/[\s>]/.test(tag[cursor])) cursor += 1
        value = tag.slice(valueStart, cursor)
      }
    }

    if (attributeName === name && value !== null) {
      return { value, valueStart }
    }
  }

  return null
}

function extractHtmlTargets(masked, push) {
  let cursor = 0

  while (cursor < masked.length) {
    const start = masked.indexOf('<', cursor)
    if (start === -1) break

    const tagStart = parseHtmlTagStart(masked, start)
    if (!tagStart) {
      cursor = start + 1
      continue
    }

    const end = findHtmlTagEnd(masked, start)
    if (end === -1) {
      cursor = start + 1
      continue
    }

    if (tagStart.opening && (tagStart.name === 'a' || tagStart.name === 'img')) {
      const tag = masked.slice(start, end + 1)
      const attributeName = tagStart.name === 'a' ? 'href' : 'src'
      const attribute = findHtmlAttribute(tag, attributeName, tagStart.nameEnd - start)
      if (attribute) push(attribute.value, start + attribute.valueStart)
    }

    cursor = end + 1
  }
}

function extractLocalTargetsFromMasked(masked) {
  const found = []
  const lineNumberAt = createLineNumberResolver(masked)
  const labelEnds = buildLabelEndIndex(masked)
  let sequence = 0

  const push = (rawTarget, index) => {
    let target = rawTarget.trim()
    target = target.replace(/\\([\\()[\]<> ])/g, '$1')
    if (isExternalTarget(target)) return
    const pathPart = target.split('#')[0].split('?')[0]
    if (pathPart === '') return
    found.push({ target, pathPart, line: lineNumberAt(index), index, sequence })
    sequence += 1
  }

  // Parse inline links instead of using a single regular expression so nested
  // image labels and balanced parentheses in destinations remain visible.
  for (let index = 0; index < masked.length; index += 1) {
    let bracketStart = -1
    if (masked[index] === '!' && masked[index + 1] === '[') bracketStart = index + 1
    else if (masked[index] === '[' && masked[index - 1] !== '!') bracketStart = index
    if (bracketStart === -1) continue

    const labelEnd = labelEnds.get(bracketStart)
    if (labelEnd === undefined || masked[labelEnd + 1] !== '(') continue
    const destination = parseInlineDestination(masked, labelEnd + 1)
    if (destination) push(destination.target, index)
  }

  // A reference-style link's path lives in its definition, so validating every
  // local definition covers both links and images without resolving labels.
  extractReferenceDefinitions(masked, push)
  extractHtmlTargets(masked, push)

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

/** Diagnose case-only mismatches on case-sensitive filesystems; never accept them. */
function existsCaseFolded(target, root, directoryCache) {
  let current = root
  for (const segment of relative(root, target).split(sep)) {
    const entries = readDirectoryCached(current, directoryCache)
    if (entries === null) return false
    let name = segment
    if (!entries.has(name)) {
      const matches = [...entries].filter((entry) => entry.toLowerCase() === segment.toLowerCase())
      // Do not guess between ambiguous names or mistake a missing suffix for a case error.
      if (matches.length !== 1) return false
      name = matches[0]
    }
    current = join(current, name)
  }
  return existsSync(current)
}

/** Every Markdown file in the working tree, repo-relative, in stable order.
 *
 * This walks the working tree, not `git ls-files`, so an untracked or
 * gitignored `.md` sitting in the checkout is scanned too. That is deliberate —
 * the check needs no git process and works in a bare export — but it means a
 * local scratch document can produce a finding that CI will not reproduce.
 */
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
  if (inside === '..' || inside.startsWith(`..${sep}`) || isAbsolute(inside)) {
    return { reason: 'outside the repository' }
  }
  if (!existsSync(target)) {
    return { reason: existsCaseFolded(target, root, directoryCache) ? 'wrong case' : 'missing' }
  }
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
      `\n${diagnostics.length} malformed masking warning(s); links outside bounded spans were still checked.`,
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
