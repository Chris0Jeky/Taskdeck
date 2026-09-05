#!/usr/bin/env node
// =============================================================================
// select-changelog-base.mjs — pick the changelog base for a release (#2250)
// =============================================================================
//
// `releases/generate-notes` renders the "What's changed" section between a base
// tag and the tag being published. Release Desktop used to name that base with
//
//   gh release list --exclude-pre-releases --exclude-drafts --limit 1
//
// which returns the newest stable release by RELEASE DATE. Re-running
// `v0.3.0-rc.1` after `v0.3.0` had already shipped therefore sent
// `previous_tag_name=v0.3.0`, and the RC page rendered a changelog running
// backwards from a release that came after it.
//
// The base is instead the newest stable release that sorts STRICTLY BEFORE the
// tag being built, by semver precedence. Precedence is not string order either:
// `v0.10.0` is newer than `v0.4.0`, which a lexicographic sort reverses.
//
// The accepted tag shapes are exactly the ones `scripts/ci/validate-release-tag.sh`
// admits, so this script and the workflow's grammar gate cannot drift apart:
//
//   v<major>.<minor>.<patch>[-<prerelease>][+<build>]
//
// Anything else in the candidate list is skipped with a warning — an unrelated
// tag in the repository must not fail a publish — but an unparseable TARGET is
// refused, because the caller has already grammar-checked it and a mismatch
// there means something upstream is wrong.
//
// Dependency-free and importable: `select-changelog-base.test.mjs` exercises the
// ordering and the selection directly, plus the CLI shape the workflow calls.
//
// Usage:  node scripts/ci/select-changelog-base.mjs --tag <tag> --candidates <file>
// Output: the selected base tag on stdout, or nothing at all when the target is
//         the first release. Nothing on stdout is the caller's "no base" signal.
// Exit:   0 selected (or legitimately empty) · 1 refused target · 2 wrong usage.
// =============================================================================

import { readFileSync } from 'node:fs'
import process from 'node:process'
import { pathToFileURL } from 'node:url'

/** Mirrors TAG_GRAMMAR in scripts/ci/validate-release-tag.sh. */
export const RELEASE_TAG_GRAMMAR =
  /^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*))?(?:\+([0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*))?$/

/** The same 64-character ceiling the shell gate enforces. */
export const MAX_TAG_LENGTH = 64

/**
 * Parse a release tag into its precedence-bearing parts.
 *
 * Build metadata is deliberately dropped: semver excludes it from precedence,
 * and two releases differing only in build metadata are the same release for
 * changelog purposes.
 *
 * @param {unknown} tag
 * @returns {{major: number, minor: number, patch: number, prerelease: Array<string|number>}|null}
 */
export function parseReleaseTag(tag) {
  if (typeof tag !== 'string' || tag.length === 0 || tag.length > MAX_TAG_LENGTH) return null
  const match = RELEASE_TAG_GRAMMAR.exec(tag)
  if (!match) return null
  const [, major, minor, patch, prerelease] = match
  return {
    major: Number(major),
    minor: Number(minor),
    patch: Number(patch),
    // A wholly numeric identifier compares numerically and ranks below any
    // alphanumeric one, so its type is decided here rather than at compare time.
    prerelease:
      prerelease === undefined
        ? []
        : prerelease.split('.').map((part) => (/^[0-9]+$/.test(part) ? Number(part) : part)),
  }
}

/**
 * Semver precedence over the prerelease identifier lists.
 *
 * An EMPTY list means "no prerelease", which outranks any prerelease of the same
 * version — v0.3.0 is newer than v0.3.0-rc.1.
 */
function comparePrerelease(left, right) {
  if (left.length === 0 && right.length === 0) return 0
  if (left.length === 0) return 1
  if (right.length === 0) return -1
  const shared = Math.min(left.length, right.length)
  for (let index = 0; index < shared; index += 1) {
    const a = left[index]
    const b = right[index]
    const aNumeric = typeof a === 'number'
    const bNumeric = typeof b === 'number'
    if (aNumeric && bNumeric) {
      if (a !== b) return a < b ? -1 : 1
      continue
    }
    if (aNumeric !== bNumeric) return aNumeric ? -1 : 1
    if (a !== b) return a < b ? -1 : 1
  }
  // All shared identifiers are equal: the longer list wins (rc.1 < rc.1.1).
  if (left.length === right.length) return 0
  return left.length < right.length ? -1 : 1
}

/**
 * Compare two release tags by semver precedence.
 *
 * @returns {number} negative if `left` is older, 0 if equal, positive if newer.
 * @throws {Error} when either tag is outside the release-tag grammar.
 */
export function compareReleaseTags(left, right) {
  const a = parseReleaseTag(left)
  const b = parseReleaseTag(right)
  if (a === null) throw new Error(`${JSON.stringify(left)} does not match the release-tag grammar`)
  if (b === null) throw new Error(`${JSON.stringify(right)} does not match the release-tag grammar`)
  if (a.major !== b.major) return a.major < b.major ? -1 : 1
  if (a.minor !== b.minor) return a.minor < b.minor ? -1 : 1
  if (a.patch !== b.patch) return a.patch < b.patch ? -1 : 1
  return comparePrerelease(a.prerelease, b.prerelease)
}

/**
 * Choose the newest candidate that sorts STRICTLY BEFORE `targetTag`.
 *
 * Strictness is what keeps a re-run of an already-published tag from becoming
 * its own changelog base, and what keeps a later release out of an earlier
 * one's page.
 *
 * @param {string} targetTag the tag being published
 * @param {string[]} candidateTags stable release tags, in any order
 * @param {(message: string) => void} [warn] receives one line per skipped candidate
 * @returns {string|null} the base tag exactly as it was listed, or null
 */
export function selectChangelogBase(targetTag, candidateTags, warn = () => {}) {
  const target = parseReleaseTag(targetTag)
  if (target === null) {
    throw new Error(`target tag ${JSON.stringify(targetTag)} does not match the release-tag grammar`)
  }
  let best = null
  for (const candidate of candidateTags) {
    const trimmed = typeof candidate === 'string' ? candidate.trim() : ''
    if (trimmed === '') continue
    if (parseReleaseTag(trimmed) === null) {
      warn(`ignoring ${JSON.stringify(trimmed)}: not a release tag`)
      continue
    }
    if (compareReleaseTags(trimmed, targetTag) >= 0) continue
    if (best === null || compareReleaseTags(trimmed, best) > 0) {
      best = trimmed
    }
  }
  return best
}

export function parseArgs(argv) {
  const options = {}
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index]
    const match = /^--([a-z0-9-]+)$/.exec(token)
    if (!match) throw new Error(`unexpected argument: ${token}`)
    const value = argv[index + 1]
    if (value === undefined || /^--[a-z0-9-]+$/.test(value)) {
      throw new Error(`--${match[1]} requires a value`)
    }
    options[match[1]] = value
    index += 1
  }
  return options
}

export function main(argv) {
  let options
  try {
    options = parseArgs(argv)
  } catch (error) {
    process.stderr.write(`::error::select-changelog-base: ${error.message}\n`)
    return 2
  }
  if (!options.tag || !options.candidates) {
    process.stderr.write(
      '::error::select-changelog-base: usage: --tag <tag> --candidates <file of tags, one per line>\n',
    )
    return 2
  }

  let listed
  try {
    listed = readFileSync(options.candidates, 'utf8')
  } catch (error) {
    process.stderr.write(`::error::select-changelog-base: ${error.message}\n`)
    return 1
  }

  let base
  try {
    base = selectChangelogBase(options.tag, listed.replace(/\r\n/g, '\n').split('\n'), (message) => {
      process.stderr.write(`::warning::select-changelog-base: ${message}\n`)
    })
  } catch (error) {
    process.stderr.write(`::error::select-changelog-base: ${error.message}\n`)
    return 1
  }

  if (base === null) {
    process.stderr.write(`No release sorts before ${options.tag}; the caller decides the fallback.\n`)
    return 0
  }
  process.stdout.write(`${base}\n`)
  return 0
}

// Importable by the test suite; executed only when invoked as the CLI entry point.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = main(process.argv.slice(2))
}
