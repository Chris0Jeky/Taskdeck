#!/usr/bin/env node

/**
 * Repository-relative Markdown link check.
 *
 * Scope, deliberately narrow: it resolves link and image targets that point at a
 * path inside this repository and reports the ones that do not exist. It does
 * not touch the network, so it never fails on someone else's outage, rate limit
 * or login wall, and it is safe to run in any lane.
 *
 * Anchors (`#section`) are checked only as far as the file half — this does not
 * verify that a heading exists, because heading-slug rules differ between GitHub
 * and every other renderer and a wrong answer here would be worse than none.
 *
 * Seeded by the #2235 v0.3 spring-cleaning link sweep, which #1138 asks to make
 * repeatable rather than one-shot.
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

/**
 * Blank out fenced blocks and inline code spans, replacing each character with a
 * space so that every surviving offset still matches the original string.
 *
 * This is what stops the checker reporting illustrative Markdown as broken. A
 * doc that writes `![…](../path/to/diagram.svg)` inside backticks is showing the
 * reader a shape, not linking anywhere, and an earlier ad-hoc sweep flagged
 * exactly that as a false positive.
 */
export function maskCode(markdown) {
  let masked = markdown.replace(/^([ \t]*)(```+|~~~+)[^\n]*\n[\s\S]*?^[ \t]*\2[^\n]*$/gm, (block) =>
    block.replace(/[^\n]/g, ' '),
  )
  masked = masked.replace(/(`+)(?:(?!\1)[\s\S])*?\1/g, (span) => span.replace(/[^\n]/g, ' '))
  return masked
}

/** Targets that name something other than a path in this repository. */
export function isExternalTarget(target) {
  return (
    target === '' ||
    /^[a-z][a-z0-9+.-]*:/i.test(target) || // http:, https:, mailto:, tel:, data:, ...
    target.startsWith('//') ||
    target.startsWith('#')
  )
}

/** Extract every repository-relative link target, with the line it sits on. */
export function extractLocalTargets(markdown) {
  const masked = maskCode(markdown)
  const found = []
  const pattern = /!?\[(?:[^\][]|\[[^\][]*\])*\]\(\s*(<[^>]*>|[^)\s]+)(?:\s+(?:"[^"]*"|'[^']*'))?\s*\)/g
  let match
  while ((match = pattern.exec(masked)) !== null) {
    let target = match[1]
    if (target.startsWith('<') && target.endsWith('>')) target = target.slice(1, -1)
    if (isExternalTarget(target)) continue
    const pathPart = target.split('#')[0].split('?')[0]
    if (pathPart === '') continue
    const line = masked.slice(0, match.index).split('\n').length
    found.push({ target, pathPart, line })
  }
  return found
}

/** Every tracked Markdown file, repo-relative, in stable order. */
export function collectMarkdownFiles(root = repoRoot) {
  const files = []
  const walk = (directory) => {
    let entries
    try {
      entries = readdirSync(directory, { withFileTypes: true })
    } catch {
      return // an unreadable directory is not a link defect
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
export function resolveTarget(sourceFile, pathPart, root = repoRoot) {
  let decoded = pathPart
  try {
    decoded = decodeURIComponent(pathPart)
  } catch {
    // A target that is not valid percent-encoding is used as written.
  }
  // A leading "/" in a repository document means repo-root-relative, not filesystem-absolute.
  const base = decoded.startsWith('/') ? join(root, decoded.slice(1)) : join(dirname(sourceFile), decoded)
  const target = resolve(base)
  if (!existsSync(target)) return { reason: 'missing' }
  // Escaping the repository resolves on the author's machine and nowhere else.
  const inside = relative(root, target)
  if (inside.startsWith('..') || isAbsolute(inside)) {
    return { reason: 'outside the repository' }
  }
  return null
}

export function findBrokenLinks(root = repoRoot) {
  const broken = []
  for (const file of collectMarkdownFiles(root)) {
    let contents
    try {
      contents = readFileSync(file, 'utf8')
    } catch {
      continue
    }
    for (const { target, pathPart, line } of extractLocalTargets(contents)) {
      const failure = resolveTarget(file, pathPart, root)
      if (failure) {
        broken.push({
          file: relative(root, file).split(sep).join('/'),
          line,
          target,
          reason: failure.reason,
        })
      }
    }
  }
  return broken
}

function main() {
  const scanned = collectMarkdownFiles().length
  const broken = findBrokenLinks()

  if (broken.length > 0) {
    console.error('Doc link check failed:')
    for (const { file, line, target, reason } of broken) {
      console.error(`- ${file}:${line} -> ${target} (${reason})`)
    }
    console.error(
      `\n${broken.length} broken repository-relative link(s) across ${scanned} Markdown files.`,
    )
    process.exit(1)
  }

  console.log(`Doc link check passed (${scanned} Markdown files, 0 broken relative links).`)
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
