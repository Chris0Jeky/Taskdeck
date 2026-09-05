#!/usr/bin/env node
// =============================================================================
// compose-release-notes.mjs — render the GitHub Release page body (#2234)
// =============================================================================
//
// `gh release create --generate-notes` opens the page with GitHub's flat
// "What's Changed" PR list and leaves the download at the bottom, behind the
// asset table. This composer renders the body the release workflow passes to
// `--notes-file`, in the order a person landing on the page needs it:
//
//   1. the download button (a shields.io `for-the-badge` image linked to the
//      DETERMINISTIC asset URL, which is known before the upload happens),
//      the SHA-256, the quick start link, and an RC banner for a prerelease;
//   2. `## Breaking changes`, lifted from the tag's own `UPGRADING.md` section
//      so the section can never be forgotten at tag time (#1308 AC5);
//   3. `## Highlights`, the curated `docs/releases/notes/<tag>.md` written by
//      the pre-tag docs PR;
//   4. `## What's changed`, the `releases/generate-notes` body grouped through
//      `.github/release.yml`, carrying its full-changelog compare link.
//
// Missing-source policy differs by tag class, because a stable tag is the one
// users are pointed at by /releases/latest:
//
//   * STABLE  — a missing highlights file or a missing UPGRADING section is an
//               ERROR and fails the run before anything is published.
//   * RC      — both degrade to a warning: highlights are omitted, breaking
//               changes fall back to "see UPGRADING.md".
//
// `composeReleaseNotes` is pure (strings in, string out) so the whole policy is
// unit-testable without a runner: see `compose-release-notes.test.mjs`.
//
// Tracked by: #2234 (page layout), #1806 (idempotent adopt/publish), #2217 (RC)
// =============================================================================

import { readFileSync, writeFileSync } from 'node:fs'
import process from 'node:process'
import { pathToFileURL } from 'node:url'

/** The in-zip quick start's reviewed source, byte-checked by the workflow. */
export const QUICK_START_SOURCE_PATH = 'docs/releases/WINDOWS_QUICK_START.md'

/**
 * GitHub rejects a release body over 125,000 characters. Failing here names the
 * overflow and the file to trim, instead of letting `gh release create` reject
 * the publish with an opaque 422 after the assets are already built.
 */
export const MAX_RELEASE_BODY_LENGTH = 120000

/**
 * shields.io encodes its own separators inside a path segment: a literal dash
 * is `--`, a literal underscore `__`, and a space `_`. Skipping this turns
 * `v0.3.0-rc.1` into a badge that reads `v0.3.0` with `rc.1` as its colour.
 */
export function escapeShieldsSegment(value) {
  return encodeURIComponent(String(value).replace(/-/g, '--').replace(/_/g, '__').replace(/ /g, '_'))
}

/** Markdown link text: `]` would close the label early and break the button. */
function escapeLinkText(value) {
  return String(value).replace(/([[\]])/g, '\\$1')
}

/**
 * Read the SHA-256 out of a `sha256sum`-format line. The file name recorded in
 * the checksum is compared with the asset name the page links to: a mismatch
 * means the page would publish a digest for a different file, which is worse
 * than publishing none.
 */
export function parseChecksum(checksumText, assetName) {
  if (typeof checksumText !== 'string' || checksumText.trim() === '') {
    return { sha256: null, error: 'checksum file is empty or missing' }
  }
  for (const rawLine of checksumText.split('\n')) {
    const line = rawLine.trim()
    if (line === '') continue
    // `sha256sum` writes "<hex>  <name>"; the binary mode of `sha256sum` and
    // `shasum -a 256` write "<hex> *<name>".
    const match = /^([0-9a-fA-F]{64})[ \t]+[* ]?(.+)$/.exec(line)
    if (!match) {
      return { sha256: null, error: `unparseable checksum line: ${line}` }
    }
    const [, hex, recordedName] = match
    const name = recordedName.trim().replace(/^\.\//, '').split(/[\\/]/).pop()
    if (name !== assetName) {
      return {
        sha256: null,
        error: `checksum names ${name}, but the release asset is ${assetName}`,
      }
    }
    return { sha256: hex.toLowerCase(), error: null }
  }
  return { sha256: null, error: 'checksum file contains no checksum line' }
}

/**
 * A CommonMark fenced-code-block delimiter: up to three leading spaces, then a
 * run of at least three backticks or tildes, then the info string (opening) or
 * trailing whitespace only (closing).
 */
const FENCE_DELIMITER = /^ {0,3}(`{3,}|~{3,})(.*)$/

/**
 * Advance the fenced-code-block state by one line. `state` is `null` outside a
 * fence and `{char, length}` inside one. UPGRADING.md carries `sql` and
 * `powershell` samples whose contents start with `#`, so every scan over the
 * document has to know whether the line it is looking at is prose or sample
 * text — a `# comment` inside a fence is neither a heading nor a link context.
 */
function advanceFenceState(state, line) {
  const match = FENCE_DELIMITER.exec(line)
  if (!match) return state
  const [, marker, info] = match
  const char = marker[0]
  if (state === null) {
    // An opening backtick fence may not carry a backtick in its info string.
    if (char === '`' && info.includes('`')) return null
    return { char, length: marker.length }
  }
  // A fence closes only on its own character, at least as long as the opener,
  // with nothing but whitespace after the run.
  if (char === state.char && marker.length >= state.length && info.trim() === '') {
    return null
  }
  return state
}

/**
 * Lift the `## <tag> …` section out of UPGRADING.md. Headings carry a date or a
 * label after the tag (`## v0.2.0 — 2026-08-29`), so the match is on the tag
 * followed by a boundary — never a bare prefix, which would let `v0.1.0` match
 * a `v0.1.0-rc.1` heading.
 *
 * The scan is fence-aware in both directions (#2250): a `#`/`##` line inside a
 * fenced block neither starts a section nor ends one, so a shell comment or a
 * Markdown sample can no longer truncate the section or be mistaken for its
 * heading. An UNTERMINATED fence runs to the end of the document, which is what
 * a Markdown renderer does with the same input — the section is then over-long
 * rather than silently cut at a sample line.
 */
export function extractUpgradingSection(markdown, tag) {
  if (typeof markdown !== 'string') return null
  const lines = markdown.replace(/\r\n/g, '\n').split('\n')
  let fence = null
  let start = -1
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index]
    const wasInFence = fence !== null
    fence = advanceFenceState(fence, line)
    // Skip the delimiter lines themselves and everything between them.
    if (wasInFence || fence !== null) continue
    const heading = /^## +(.*)$/.exec(line)
    if (!heading) continue
    const text = heading[1].trim()
    if (text === tag || text.startsWith(`${tag} `)) {
      start = index + 1
      break
    }
  }
  if (start === -1) return null
  const body = []
  fence = null
  for (let index = start; index < lines.length; index += 1) {
    const line = lines[index]
    const wasInFence = fence !== null
    fence = advanceFenceState(fence, line)
    if (!wasInFence && fence === null && /^#{1,2} +/.test(line)) break
    body.push(line)
  }
  const section = body.join('\n').trim()
  return section === '' ? null : section
}

/**
 * Rewrite one Markdown link destination for the release page. A release body is
 * rendered outside any file, so a destination that resolves against UPGRADING.md
 * in the repository resolves against nothing here.
 *
 * Left exactly as written: any destination carrying a scheme (`https:`,
 * `mailto:`, and every other), and any root-relative `/path` — GitHub already
 * resolves those against the repository host.
 */
function rewriteDestination(destination, base) {
  if (destination === '') return destination
  if (destination.startsWith('#')) return `${base}UPGRADING.md${destination}`
  if (/^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(destination)) return destination
  if (destination.startsWith('/')) return destination
  const hashAt = destination.indexOf('#')
  const path = hashAt === -1 ? destination : destination.slice(0, hashAt)
  const fragment = hashAt === -1 ? '' : destination.slice(hashAt)
  const cleaned = path.replace(/^\.\//, '')
  if (cleaned === '') return destination
  // Path segments are kept verbatim: they are already repository paths, and
  // re-encoding them would break the `/` separators the blob URL needs.
  return `${base}${cleaned}${fragment}`
}

/** `](dest)` or `](dest "title")`, the shape UPGRADING.md actually writes. */
const INLINE_LINK = /(\]\()([^()\s]+)((?:[ \t]+(?:"[^"]*"|'[^']*'|\([^()]*\)))?[ \t]*\))/g

/** `[id]: dest` at the start of a line, optionally followed by a title. */
const REFERENCE_DEFINITION = /^( {0,3}\[[^\]]+\]:[ \t]*)(\S+)([ \t]*.*)$/

function rewriteLinksInText(text, base) {
  return text.replace(INLINE_LINK, (whole, open, destination, close) => {
    return `${open}${rewriteDestination(destination, base)}${close}`
  })
}

/**
 * Apply `rewriteText` to everything on the line EXCEPT inline code spans. A
 * span opens on a run of N backticks and closes on the next run of exactly N;
 * an unclosed run is literal text and is rewritten with the rest of the line.
 */
function rewriteOutsideInlineCode(line, rewriteText) {
  let out = ''
  let plainStart = 0
  let index = 0
  while (index < line.length) {
    if (line[index] !== '`') {
      index += 1
      continue
    }
    const runStart = index
    while (index < line.length && line[index] === '`') index += 1
    const runLength = index - runStart
    let search = index
    let closeEnd = -1
    while (search < line.length) {
      if (line[search] !== '`') {
        search += 1
        continue
      }
      const closeStart = search
      while (search < line.length && line[search] === '`') search += 1
      if (search - closeStart === runLength) {
        closeEnd = search
        break
      }
    }
    if (closeEnd === -1) continue
    out += rewriteText(line.slice(plainStart, runStart))
    out += line.slice(runStart, closeEnd)
    index = closeEnd
    plainStart = closeEnd
  }
  return out + rewriteText(line.slice(plainStart))
}

/**
 * Make every relative link in a lifted Markdown section absolute against the
 * tag being published (#2250). Bare anchors resolve against UPGRADING.md, the
 * only document whose headings they can name; relative paths resolve against
 * the repository root at the tag, matching `upgradingUrl` above (including its
 * `encodeURIComponent(tag)` convention).
 *
 * Untouched: fenced code blocks, inline code spans, absolute and scheme-bearing
 * destinations, and root-relative paths. Image destinations (`![alt](path)`)
 * share the `](` shape and get the same rewrite; a relative image is broken on
 * a release page either way (a working one would need a `raw.githubusercontent`
 * URL), and UPGRADING.md has no images today.
 */
export function rewriteRelativeLinks(markdown, { repo, tag } = {}) {
  if (typeof markdown !== 'string') return markdown
  if (typeof repo !== 'string' || repo === '' || typeof tag !== 'string' || tag === '') return markdown
  const base = `https://github.com/${repo}/blob/${encodeURIComponent(tag)}/`
  let fence = null
  return markdown
    .split('\n')
    .map((line) => {
      const wasInFence = fence !== null
      fence = advanceFenceState(fence, line)
      if (wasInFence || fence !== null) return line
      const definition = REFERENCE_DEFINITION.exec(line)
      if (definition) {
        const [, label, destination, rest] = definition
        return `${label}${rewriteDestination(destination, base)}${rest}`
      }
      return rewriteOutsideInlineCode(line, (text) => rewriteLinksInText(text, base))
    })
    .join('\n')
}

/** Drop a leading `# Title` line: the release page supplies its own heading. */
function stripLeadingTitle(markdown) {
  const text = markdown.replace(/\r\n/g, '\n').trim()
  return text.replace(/^# +[^\n]*\n+/, '').trim()
}

/**
 * GitHub's generated body opens with its own `## What's Changed` heading. This
 * page supplies that heading itself, so the duplicate is removed rather than
 * rendered twice.
 */
function stripGeneratedHeading(markdown) {
  return markdown
    .replace(/\r\n/g, '\n')
    .trim()
    .replace(/^#{2,3} +What's Changed\n+/i, '')
    .trim()
}

/**
 * Render the release body.
 *
 * @param {object} input
 * @param {string} input.tag              validated release tag, e.g. `v0.3.0-rc.1`
 * @param {boolean|string} input.prerelease resolve-source's prerelease decision
 * @param {string} input.repo             `owner/name`
 * @param {string} input.assetName        `taskdeck-<tag>-win-x64.zip`
 * @param {string|null} input.checksumText contents of `<asset>.sha256`
 * @param {string|null} input.upgradingText contents of UPGRADING.md
 * @param {string|null} input.notesText   contents of docs/releases/notes/<tag>.md
 * @param {object|null} input.generatedNotes parsed `releases/generate-notes` JSON
 * @returns {{body: string, warnings: string[], errors: string[]}}
 */
export function composeReleaseNotes({
  tag,
  prerelease,
  repo,
  assetName,
  checksumText = null,
  upgradingText = null,
  notesText = null,
  generatedNotes = null,
}) {
  const warnings = []
  const errors = []

  for (const [name, value] of [['tag', tag], ['repo', repo], ['assetName', assetName]]) {
    if (typeof value !== 'string' || value.trim() === '') {
      errors.push(`compose-release-notes: ${name} is required`)
    }
  }
  if (errors.length > 0) {
    return { body: '', warnings, errors }
  }

  const isPrerelease = prerelease === true || prerelease === 'true'
  // Curated sources are REQUIRED for a stable tag and advisory for an RC.
  const requireCuratedSources = !isPrerelease
  const record = (message) => {
    if (requireCuratedSources) errors.push(message)
    else warnings.push(message)
  }

  const assetUrl =
    `https://github.com/${repo}/releases/download/${encodeURIComponent(tag)}/${encodeURIComponent(assetName)}`
  const quickStartUrl = `https://github.com/${repo}/blob/${encodeURIComponent(tag)}/${QUICK_START_SOURCE_PATH}`
  const upgradingUrl = `https://github.com/${repo}/blob/${encodeURIComponent(tag)}/UPGRADING.md`

  const badgeLabel = escapeShieldsSegment('Download')
  const badgeMessage = escapeShieldsSegment(`${tag} · Windows x64 ZIP`)
  const badgeUrl =
    `https://img.shields.io/badge/${badgeLabel}-${badgeMessage}-2f81f7` +
    '?style=for-the-badge&logo=windows&logoColor=white'

  const sections = []

  // --- 1. Download button, first line of the page --------------------------
  sections.push(
    `[![${escapeLinkText(`Download Taskdeck ${tag} for Windows x64`)}](${badgeUrl})](${assetUrl})`,
  )

  if (isPrerelease) {
    sections.push('> **Release candidate** — upgrade notes below; not the Latest release.')
  }

  const { sha256, error: checksumError } = parseChecksum(checksumText, assetName)
  if (checksumError) {
    errors.push(`compose-release-notes: ${checksumError}`)
  }

  sections.push(
    [
      '**Windows 10/11 x64 · portable ZIP · no installer.** Unzip anywhere and run `Taskdeck.Api.exe`.',
      '',
      `- **File:** \`${assetName}\``,
      `- **SHA-256:** \`${sha256 ?? 'UNAVAILABLE'}\``,
      `- **First run:** [QUICK_START.md](${quickStartUrl}) — the same file ships inside the ZIP.`,
      '',
      'Verify the download before running it:',
      '',
      '```powershell',
      `Get-FileHash .\\${assetName} -Algorithm SHA256`,
      '```',
    ].join('\n'),
  )

  // --- 2. Breaking changes, from the tag's UPGRADING section ---------------
  // A release body is not a file in the tree, so relative links and bare
  // anchors lifted out of UPGRADING.md are dead here (#2250). Only this
  // section is rewritten: the curated notes file carries no relative-link
  // shapes today, and the generated changelog is already absolute.
  const upgradingSection = extractUpgradingSection(upgradingText, tag)
  if (upgradingSection) {
    sections.push(`## Breaking changes\n\n${rewriteRelativeLinks(upgradingSection, { repo, tag })}`)
  } else {
    record(`UPGRADING.md has no "## ${tag}" section`)
    sections.push(
      `## Breaking changes\n\nNo \`UPGRADING.md\` section was written for \`${tag}\` at tag time — ` +
        `read [UPGRADING.md](${upgradingUrl}) before upgrading.`,
    )
  }

  // --- 3. Curated highlights ----------------------------------------------
  const highlights = typeof notesText === 'string' ? stripLeadingTitle(notesText) : ''
  if (highlights !== '') {
    sections.push(`## Highlights\n\n${highlights}`)
  } else {
    record(`docs/releases/notes/${tag}.md is missing or empty`)
  }

  // --- 4. Generated, grouped changelog ------------------------------------
  const generatedBody =
    generatedNotes && typeof generatedNotes.body === 'string' ? stripGeneratedHeading(generatedNotes.body) : ''
  if (generatedBody !== '') {
    sections.push(`## What's changed\n\n${generatedBody}`)
  } else {
    warnings.push('no generated release notes were supplied; the changelog section is a placeholder')
    sections.push(
      "## What's changed\n\nThe automatic changelog is generated at tag time and is not available in this render.",
    )
  }

  const body = `${sections.join('\n\n')}\n`
  if (body.length > MAX_RELEASE_BODY_LENGTH) {
    errors.push(
      `compose-release-notes: composed body is ${body.length} characters, ` +
        `${body.length - MAX_RELEASE_BODY_LENGTH} over the ${MAX_RELEASE_BODY_LENGTH} limit — ` +
        'trim docs/releases/notes/<tag>.md or the UPGRADING section',
    )
  }

  return { body, warnings, errors }
}

// -----------------------------------------------------------------------------
// CLI
// -----------------------------------------------------------------------------

function readOptionalFile(path) {
  if (!path) return null
  try {
    return readFileSync(path, 'utf8')
  } catch (error) {
    if (error && error.code === 'ENOENT') return null
    throw error
  }
}

export function parseArgs(argv) {
  const options = {}
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index]
    const match = /^--([a-z0-9-]+)(?:=([\s\S]*))?$/.exec(token)
    if (!match) {
      throw new Error(`unexpected argument: ${token}`)
    }
    const [, name, inlineValue] = match
    let value = inlineValue
    if (value === undefined) {
      value = argv[index + 1]
      if (value === undefined || /^--[a-z0-9-]+$/.test(value)) {
        throw new Error(`--${name} requires a value`)
      }
      index += 1
    }
    options[name] = value
  }
  return options
}

export function main(argv) {
  let options
  try {
    options = parseArgs(argv)
  } catch (error) {
    process.stderr.write(`::error::compose-release-notes: ${error.message}\n`)
    return 2
  }
  for (const required of ['tag', 'repo', 'asset', 'out']) {
    if (!options[required]) {
      process.stderr.write(
        '::error::compose-release-notes: usage: --tag <tag> --prerelease <true|false> --repo <owner/name> ' +
          '--asset <name> --out <path> [--checksum-file <path>] [--upgrading <path>] [--notes <path>] ' +
          '[--generated-notes <path>]\n',
      )
      return 2
    }
  }

  const generatedRaw = readOptionalFile(options['generated-notes'])
  let generatedNotes = null
  if (generatedRaw !== null && generatedRaw.trim() !== '') {
    try {
      generatedNotes = JSON.parse(generatedRaw)
    } catch (error) {
      process.stderr.write(
        `::warning::compose-release-notes: generated notes are not valid JSON (${error.message}).\n`,
      )
    }
  }

  const { body, warnings, errors } = composeReleaseNotes({
    tag: options.tag,
    prerelease: options.prerelease === 'true',
    repo: options.repo,
    assetName: options.asset,
    checksumText: readOptionalFile(options['checksum-file']),
    upgradingText: readOptionalFile(options.upgrading),
    notesText: readOptionalFile(options.notes),
    generatedNotes,
  })

  for (const warning of warnings) {
    process.stderr.write(`::warning::compose-release-notes: ${warning}\n`)
  }
  for (const error of errors) {
    process.stderr.write(`::error::${error}\n`)
  }
  if (errors.length > 0) {
    return 1
  }

  writeFileSync(options.out, body, 'utf8')
  process.stdout.write(`Rendered release notes for ${options.tag} to ${options.out} (${body.length} bytes).\n`)
  return 0
}

// Importable by the test suite; executed only when invoked as the CLI entry point.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = main(process.argv.slice(2))
}
