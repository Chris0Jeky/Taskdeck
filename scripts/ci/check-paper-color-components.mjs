import { readdir, readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { dirname, extname, join, relative, resolve } from 'node:path'

export const HEX_PATTERN = /#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6,8})\b/g

const SOURCE_EXTENSIONS = new Set(['.vue', '.ts', '.tsx'])

function blankComment(source, start, end) {
  return source.slice(start, end).replace(/[^\r\n]/g, ' ')
}

function isLineCommentStart(source, index) {
  if (source[index] !== '/' || source[index + 1] !== '/') return false

  // Preserve URL-like text outside quoted strings. A protocol separator is
  // not a comment delimiter, and the following slash is handled likewise.
  return source[index - 1] !== ':'
    && !(source[index - 1] === '/' && source[index - 2] === ':')
}

function stripCodeComments(source) {
  let output = ''
  let state = 'normal'

  for (let index = 0; index < source.length;) {
    const char = source[index]

    if (state === 'single' || state === 'double' || state === 'template') {
      output += char
      if (char === '\\' && index + 1 < source.length) {
        output += source[index + 1]
        index += 2
        continue
      }
      if (
        (state === 'single' && char === "'")
        || (state === 'double' && char === '"')
        || (state === 'template' && char === '`')
      ) {
        state = 'normal'
      }
      index += 1
      continue
    }

    if (char === "'") {
      state = 'single'
      output += char
      index += 1
      continue
    }
    if (char === '"') {
      state = 'double'
      output += char
      index += 1
      continue
    }
    if (char === '`') {
      state = 'template'
      output += char
      index += 1
      continue
    }

    if (source.startsWith('<!--', index)) {
      const end = source.indexOf('-->', index + 4)
      const commentEnd = end === -1 ? source.length : end + 3
      output += blankComment(source, index, commentEnd)
      index = commentEnd
      continue
    }

    if (source.startsWith('/*', index)) {
      const end = source.indexOf('*/', index + 2)
      const commentEnd = end === -1 ? source.length : end + 2
      output += blankComment(source, index, commentEnd)
      index = commentEnd
      continue
    }

    if (isLineCommentStart(source, index)) {
      const end = source.indexOf('\n', index + 2)
      const commentEnd = end === -1 ? source.length : end
      output += blankComment(source, index, commentEnd)
      index = commentEnd
      continue
    }

    output += char
    index += 1
  }

  return output
}

function findTagEnd(source, start) {
  let quote = ''
  for (let index = start + 1; index < source.length; index += 1) {
    const char = source[index]
    if (quote) {
      if (char === quote) quote = ''
    } else if (char === "'" || char === '"') {
      quote = char
    } else if (char === '>') {
      return index
    }
  }
  return source.length - 1
}

function parseTag(source, start, end) {
  const tag = source.slice(start, end + 1)
  const match = tag.match(/^<\s*(\/?)\s*([A-Za-z][\w:-]*)/)
  if (!match) return null
  return {
    closing: match[1] === '/',
    name: match[2].toLowerCase(),
    selfClosing: /\/\s*>$/.test(tag),
  }
}

function isClosingTag(source, index, name) {
  return new RegExp(`^<\\/\\s*${name}(?=[\\s>])`, 'i').test(source.slice(index))
}

function stripVueComments(source) {
  let output = ''
  let region = 'markup'
  let state = 'normal'

  for (let index = 0; index < source.length;) {
    const char = source[index]

    if (region === 'markup') {
      if (source.startsWith('<!--', index)) {
        const end = source.indexOf('-->', index + 4)
        const commentEnd = end === -1 ? source.length : end + 3
        output += blankComment(source, index, commentEnd)
        index = commentEnd
        continue
      }

      if (char === '<') {
        const tagEnd = findTagEnd(source, index)
        const tag = parseTag(source, index, tagEnd)
        output += source.slice(index, tagEnd + 1)
        if (tag && !tag.closing && !tag.selfClosing && (tag.name === 'script' || tag.name === 'style')) {
          region = tag.name
          state = 'normal'
        }
        index = tagEnd + 1
        continue
      }

      // Apostrophes in visible template prose are not JavaScript strings.
      output += char
      index += 1
      continue
    }

    if (state === 'single' || state === 'double' || state === 'template') {
      output += char
      if (char === '\\' && index + 1 < source.length) {
        output += source[index + 1]
        index += 2
        continue
      }
      if (
        (state === 'single' && char === "'")
        || (state === 'double' && char === '"')
        || (state === 'template' && char === '`')
      ) {
        state = 'normal'
      }
      index += 1
      continue
    }

    if (isClosingTag(source, index, region)) {
      const tagEnd = findTagEnd(source, index)
      output += source.slice(index, tagEnd + 1)
      region = 'markup'
      index = tagEnd + 1
      continue
    }

    if (char === "'") {
      state = 'single'
      output += char
      index += 1
      continue
    }
    if (char === '"') {
      state = 'double'
      output += char
      index += 1
      continue
    }
    if (char === '`') {
      state = 'template'
      output += char
      index += 1
      continue
    }

    if (source.startsWith('/*', index)) {
      const end = source.indexOf('*/', index + 2)
      const commentEnd = end === -1 ? source.length : end + 2
      output += blankComment(source, index, commentEnd)
      index = commentEnd
      continue
    }

    if (isLineCommentStart(source, index)) {
      const end = source.indexOf('\n', index + 2)
      const commentEnd = end === -1 ? source.length : end
      output += blankComment(source, index, commentEnd)
      index = commentEnd
      continue
    }

    output += char
    index += 1
  }

  return output
}

/**
 * Remove source comments without changing line positions or quoted values.
 * Vue templates need a markup-aware pass so visible prose such as "don't"
 * cannot accidentally hide a later color literal.
 */
export function stripComments(source, sourceType = 'code') {
  return sourceType === 'vue' ? stripVueComments(source) : stripCodeComments(source)
}

export function findHexLiterals(source, file = 'source') {
  const sourceType = file.endsWith('.vue') ? 'vue' : 'code'
  const uncommented = stripComments(source, sourceType)
  const findings = []

  for (const match of uncommented.matchAll(HEX_PATTERN)) {
    const lineStart = uncommented.lastIndexOf('\n', match.index - 1) + 1
    const lineEnd = uncommented.indexOf('\n', match.index)
    const line = lineEnd === -1
      ? source.slice(lineStart)
      : source.slice(lineStart, lineEnd)

    findings.push({
      file,
      line: uncommented.slice(0, match.index).split('\n').length,
      column: match.index - lineStart + 1,
      literal: match[0],
      sourceLine: line,
    })
  }

  return findings
}

async function collectSourceFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true })
  const files = []

  for (const entry of entries) {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) {
      files.push(...await collectSourceFiles(path))
    } else if (SOURCE_EXTENSIONS.has(extname(entry.name))) {
      files.push(path)
    }
  }

  return files
}

export async function findPaperComponentHexLiterals(repoRoot = process.cwd()) {
  const componentsRoot = resolve(repoRoot, 'frontend/taskdeck-web/src/components/paper')
  const files = await collectSourceFiles(componentsRoot)
  const findings = []

  for (const path of files) {
    const source = await readFile(path, 'utf8')
    findings.push(...findHexLiterals(source, relative(repoRoot, path)))
  }

  return findings
}

export async function main(repoRoot = process.cwd()) {
  const findings = await findPaperComponentHexLiterals(repoRoot)
  for (const finding of findings) {
    console.log(`${finding.file}:${finding.line}:${finding.column}: ${finding.literal} ${finding.sourceLine.trim()}`)
  }
  return findings.length === 0 ? 0 : 1
}

const thisFile = fileURLToPath(import.meta.url)
const invokedFile = process.argv[1] ? resolve(process.argv[1]) : ''
if (invokedFile === thisFile) {
  try {
    process.exitCode = await main(resolve(dirname(thisFile), '../..'))
  } catch (error) {
    console.error(error instanceof Error ? error.message : error)
    process.exitCode = 1
  }
}
