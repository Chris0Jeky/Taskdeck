// =============================================================================
// compose-release-notes.test.mjs — release page layout contract (#2234)
// =============================================================================
//
// The composer decides what a person sees the moment they land on a release
// page, and it decides whether a stable tag is allowed to publish at all when
// its curated sources are missing. Both are exercised here against the pure
// function, plus a CLI round trip through the real filesystem so the exit codes
// the workflow depends on are executed rather than assumed.
//
// Run: node --test scripts/ci/compose-release-notes.test.mjs
// =============================================================================

import assert from 'node:assert/strict'
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { spawnSync } from 'node:child_process'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

import {
  composeReleaseNotes,
  escapeShieldsSegment,
  extractUpgradingSection,
  parseChecksum,
  parseArgs,
} from './compose-release-notes.mjs'

const composerPath = fileURLToPath(new URL('./compose-release-notes.mjs', import.meta.url))
const repoRoot = fileURLToPath(new URL('../../', import.meta.url))

const REPO = 'Chris0Jeky/Taskdeck'
const RC_TAG = 'v0.3.0-rc.1'
const STABLE_TAG = 'v0.3.0'
const DIGEST = 'a'.repeat(64)

function assetFor(tag) {
  return `taskdeck-${tag}-win-x64.zip`
}

function checksumFor(tag, digest = DIGEST) {
  return `${digest}  ${assetFor(tag)}\n`
}

const UPGRADING = [
  '# Upgrading Taskdeck',
  '',
  '## General upgrade procedure',
  '',
  'Stop Taskdeck, copy the database, start it again.',
  '',
  '# Version notes',
  '',
  `## ${RC_TAG} — release candidate (prerelease)`,
  '',
  '**BREAKING: API keys now require explicit scopes.**',
  '',
  '- The `AddApiKeyScopes` migration backfills existing keys to Full.',
  '',
  '## v0.2.0 — 2026-08-29',
  '',
  '**BREAKING: none for v0.1.1+ hosts.**',
  '',
].join('\n')

const NOTES = ['# Taskdeck v0.3.0-rc.1', '', '- Accountable agents: every proposal carries its evidence.', ''].join('\n')

const GENERATED = {
  name: RC_TAG,
  body: [
    "## What's Changed",
    '### Bug fixes',
    '* fix: stop the review queue double-fetching by @Chris0Jeky in https://github.com/Chris0Jeky/Taskdeck/pull/2208',
    '',
    '**Full Changelog**: https://github.com/Chris0Jeky/Taskdeck/compare/v0.2.0...v0.3.0-rc.1',
  ].join('\n'),
}

function compose(overrides = {}) {
  const tag = overrides.tag ?? RC_TAG
  return composeReleaseNotes({
    tag,
    prerelease: true,
    repo: REPO,
    assetName: assetFor(tag),
    checksumText: checksumFor(tag),
    upgradingText: UPGRADING,
    notesText: NOTES,
    generatedNotes: GENERATED,
    ...overrides,
  })
}

// -----------------------------------------------------------------------------
// 1. The download button is the first thing on the page
// -----------------------------------------------------------------------------

test('the download link is the first line of the body', () => {
  const { body, errors } = compose()
  assert.deepEqual(errors, [])
  const firstLine = body.split('\n')[0]
  assert.ok(firstLine.startsWith('[!['), `expected a linked badge image, got: ${firstLine}`)
  assert.ok(
    firstLine.includes(`https://github.com/${REPO}/releases/download/${RC_TAG}/${assetFor(RC_TAG)}`),
    'the button must point at the deterministic asset URL',
  )
  assert.ok(firstLine.includes('style=for-the-badge'), 'the issue asks for the for-the-badge style')
  assert.ok(firstLine.includes('img.shields.io/badge/'), 'the button image is a shields.io badge')
})

test('the badge escapes shields.io separators so an RC tag renders in full', () => {
  assert.equal(escapeShieldsSegment('v0.3.0-rc.1'), 'v0.3.0--rc.1')
  assert.equal(escapeShieldsSegment('a_b'), 'a__b')
  assert.equal(escapeShieldsSegment('Windows x64'), 'Windows_x64')

  const { body } = compose()
  assert.ok(body.includes('v0.3.0--rc.1'), 'the literal dash in the tag is doubled for shields.io')
})

test('the top block carries the checksum and the in-zip quick start', () => {
  const { body } = compose()
  assert.ok(body.includes(`- **SHA-256:** \`${DIGEST}\``))
  assert.ok(body.includes(`- **File:** \`${assetFor(RC_TAG)}\``))
  assert.ok(
    body.includes(`https://github.com/${REPO}/blob/${RC_TAG}/docs/releases/WINDOWS_QUICK_START.md`),
    'the quick start link points at the reviewed source of the in-zip copy, pinned to the tag',
  )
  assert.ok(body.includes('the same file ships inside the ZIP'))
  assert.ok(body.includes('Get-FileHash'), 'the page shows how to check the digest it publishes')
})

// -----------------------------------------------------------------------------
// 2. Stable vs prerelease banner
// -----------------------------------------------------------------------------

test('a prerelease carries the one-line RC banner', () => {
  const { body } = compose({ prerelease: true })
  assert.ok(body.includes('> **Release candidate** — upgrade notes below; not the Latest release.'))
  // Immediately under the button, before the download details.
  assert.ok(body.indexOf('Release candidate') < body.indexOf('**SHA-256:**'))
})

test('a stable release carries no RC banner', () => {
  const { body, errors } = composeReleaseNotes({
    tag: STABLE_TAG,
    prerelease: false,
    repo: REPO,
    assetName: assetFor(STABLE_TAG),
    checksumText: checksumFor(STABLE_TAG),
    upgradingText: UPGRADING.replace(`## ${RC_TAG} — release candidate (prerelease)`, `## ${STABLE_TAG} — 2026-09-01`),
    notesText: NOTES,
    generatedNotes: GENERATED,
  })
  assert.deepEqual(errors, [])
  assert.ok(!body.includes('Release candidate'))
  assert.ok(!body.includes('not the Latest release'))
})

test("the string 'true' is accepted for prerelease, as a workflow output would supply it", () => {
  const { body } = compose({ prerelease: 'true' })
  assert.ok(body.includes('**Release candidate**'))
  const strict = compose({ prerelease: 'false', tag: RC_TAG })
  assert.ok(!strict.body.includes('**Release candidate**'))
})

// -----------------------------------------------------------------------------
// 3. Section order
// -----------------------------------------------------------------------------

test('sections run download, breaking changes, highlights, changelog', () => {
  const { body } = compose()
  const downloadAt = 0
  const breakingAt = body.indexOf('## Breaking changes')
  const highlightsAt = body.indexOf('## Highlights')
  const changelogAt = body.indexOf("## What's changed")
  assert.ok(breakingAt > downloadAt, 'breaking changes come after the download block')
  assert.ok(breakingAt < highlightsAt, 'breaking changes come before highlights')
  assert.ok(highlightsAt < changelogAt, 'highlights come before the generated changelog')
})

// -----------------------------------------------------------------------------
// 4. UPGRADING.md sourcing
// -----------------------------------------------------------------------------

test("the breaking-changes section is the tag's own UPGRADING section", () => {
  const { body } = compose()
  assert.ok(body.includes('**BREAKING: API keys now require explicit scopes.**'))
  assert.ok(
    !body.includes('**BREAKING: none for v0.1.1+ hosts.**'),
    'extraction must stop at the next version heading',
  )
  assert.ok(
    !body.includes('Stop Taskdeck, copy the database'),
    'an earlier, unrelated section must not leak in',
  )
})

test('a version heading matches on a tag boundary, never a bare prefix', () => {
  assert.equal(extractUpgradingSection(UPGRADING, 'v0.3.0'), null, 'v0.3.0 must not match the v0.3.0-rc.1 heading')
  assert.equal(extractUpgradingSection(UPGRADING, 'v9.9.9'), null)
  assert.match(extractUpgradingSection(UPGRADING, 'v0.2.0'), /BREAKING: none for v0\.1\.1\+ hosts/)
})

test('an UPGRADING heading with no body is treated as missing', () => {
  const empty = `# Version notes\n\n## ${RC_TAG}\n\n## v0.2.0 — 2026-08-29\n\nreal content\n`
  assert.equal(extractUpgradingSection(empty, RC_TAG), null)
})

test('a missing UPGRADING section is a WARNING for an RC and a fallback pointer', () => {
  const { body, warnings, errors } = compose({ upgradingText: '# Upgrading Taskdeck\n' })
  assert.deepEqual(errors, [], 'an RC still publishes')
  assert.ok(warnings.some((w) => w.includes(`no "## ${RC_TAG}" section`)), warnings.join('; '))
  assert.ok(body.includes('## Breaking changes'), 'the heading is never dropped')
  assert.ok(body.includes(`https://github.com/${REPO}/blob/${RC_TAG}/UPGRADING.md`), 'the fallback links UPGRADING.md')
})

test('a missing UPGRADING section is an ERROR for a stable tag', () => {
  const { errors } = composeReleaseNotes({
    tag: STABLE_TAG,
    prerelease: false,
    repo: REPO,
    assetName: assetFor(STABLE_TAG),
    checksumText: checksumFor(STABLE_TAG),
    upgradingText: UPGRADING,
    notesText: NOTES,
    generatedNotes: GENERATED,
  })
  assert.ok(
    errors.some((e) => e.includes(`no "## ${STABLE_TAG}" section`)),
    `expected a hard failure, got: ${JSON.stringify(errors)}`,
  )
})

// -----------------------------------------------------------------------------
// 5. Highlights sourcing
// -----------------------------------------------------------------------------

test('highlights come from the curated notes file, without its own title line', () => {
  const { body } = compose()
  assert.ok(body.includes('## Highlights\n\n- Accountable agents: every proposal carries its evidence.'))
  assert.ok(!body.includes('# Taskdeck v0.3.0-rc.1\n'), 'the notes file title is replaced by the page heading')
})

test('a missing notes file is a WARNING for an RC and the section is omitted', () => {
  const { body, warnings, errors } = compose({ notesText: null })
  assert.deepEqual(errors, [])
  assert.ok(warnings.some((w) => w.includes(`docs/releases/notes/${RC_TAG}.md is missing`)), warnings.join('; '))
  assert.ok(!body.includes('## Highlights'), 'an empty highlights heading is worse than none')
  assert.ok(body.includes('## Breaking changes') && body.includes("## What's changed"), 'the rest still renders')
})

test('an empty notes file counts as missing', () => {
  const { warnings } = compose({ notesText: '   \n\n' })
  assert.ok(warnings.some((w) => w.includes('missing or empty')))
})

test('a missing notes file is an ERROR for a stable tag', () => {
  const stableUpgrading = UPGRADING.replace(
    `## ${RC_TAG} — release candidate (prerelease)`,
    `## ${STABLE_TAG} — 2026-09-01`,
  )
  const { errors } = composeReleaseNotes({
    tag: STABLE_TAG,
    prerelease: false,
    repo: REPO,
    assetName: assetFor(STABLE_TAG),
    checksumText: checksumFor(STABLE_TAG),
    upgradingText: stableUpgrading,
    notesText: null,
    generatedNotes: GENERATED,
  })
  assert.ok(
    errors.some((e) => e.includes(`docs/releases/notes/${STABLE_TAG}.md is missing`)),
    `expected a hard failure, got: ${JSON.stringify(errors)}`,
  )
})

// -----------------------------------------------------------------------------
// 6. Checksum parsing
// -----------------------------------------------------------------------------

test('the checksum is parsed from both sha256sum output shapes', () => {
  assert.deepEqual(parseChecksum(`${DIGEST}  ${assetFor(RC_TAG)}\n`, assetFor(RC_TAG)), {
    sha256: DIGEST,
    error: null,
  })
  assert.deepEqual(parseChecksum(`${DIGEST} *${assetFor(RC_TAG)}\n`, assetFor(RC_TAG)), {
    sha256: DIGEST,
    error: null,
  })
  assert.deepEqual(parseChecksum(`${DIGEST}  ./release-assets/${assetFor(RC_TAG)}\n`, assetFor(RC_TAG)), {
    sha256: DIGEST,
    error: null,
  })
})

test('an uppercase digest is normalised to lower case', () => {
  const upper = 'A'.repeat(64)
  assert.equal(parseChecksum(`${upper}  ${assetFor(RC_TAG)}`, assetFor(RC_TAG)).sha256, 'a'.repeat(64))
})

test('a checksum naming a different file is refused, never published as this asset digest', () => {
  const result = parseChecksum(`${DIGEST}  taskdeck-v0.2.0-win-x64.zip\n`, assetFor(RC_TAG))
  assert.equal(result.sha256, null)
  assert.match(result.error, /but the release asset is/)
})

test('an empty or malformed checksum file is refused', () => {
  assert.match(parseChecksum('', assetFor(RC_TAG)).error, /empty or missing/)
  assert.match(parseChecksum(null, assetFor(RC_TAG)).error, /empty or missing/)
  assert.match(parseChecksum('not a checksum\n', assetFor(RC_TAG)).error, /unparseable/)
  assert.match(parseChecksum(`${'a'.repeat(63)}  x.zip\n`, assetFor(RC_TAG)).error, /unparseable/)
})

test('a bad checksum fails the render even for a release candidate', () => {
  const { errors } = compose({ checksumText: null })
  assert.ok(
    errors.some((e) => e.includes('checksum file is empty or missing')),
    `expected the digest to be non-negotiable, got: ${JSON.stringify(errors)}`,
  )
})

// -----------------------------------------------------------------------------
// 7. Generated-notes passthrough
// -----------------------------------------------------------------------------

test('the generated changelog is passed through with its compare link', () => {
  const { body } = compose()
  assert.ok(body.includes('### Bug fixes'), 'the .github/release.yml grouping survives')
  assert.ok(body.includes('fix: stop the review queue double-fetching'))
  assert.ok(
    body.includes('**Full Changelog**: https://github.com/Chris0Jeky/Taskdeck/compare/v0.2.0...v0.3.0-rc.1'),
    'the full-changelog compare link is kept',
  )
})

test("GitHub's own What's Changed heading is not rendered twice", () => {
  const { body } = compose()
  const headings = body.match(/^#{2,3} +What's [Cc]hanged$/gm) ?? []
  assert.deepEqual(headings, ["## What's changed"])
})

test('absent generated notes degrade to a placeholder with a warning, never a hard failure', () => {
  const { body, warnings, errors } = compose({ generatedNotes: null })
  assert.deepEqual(errors, [], 'a rehearsal render has no tag to generate notes from')
  assert.ok(warnings.some((w) => w.includes('no generated release notes')), warnings.join('; '))
  assert.ok(body.includes("## What's changed"))
  assert.ok(body.includes('not available in this render'))
})

// -----------------------------------------------------------------------------
// 8. Required inputs
// -----------------------------------------------------------------------------

test('a missing tag, repo or asset name fails before anything is rendered', () => {
  for (const missing of ['tag', 'repo', 'assetName']) {
    const { body, errors } = compose({ [missing]: '' })
    assert.equal(body, '')
    assert.ok(errors.some((e) => e.includes(`${missing} is required`)), `${missing}: ${JSON.stringify(errors)}`)
  }
})

test('parseArgs accepts both --name value and --name=value', () => {
  assert.deepEqual(parseArgs(['--tag', 'v1.0.0', '--repo=o/r']), { tag: 'v1.0.0', repo: 'o/r' })
  assert.throws(() => parseArgs(['--tag']), /--tag requires a value/)
  assert.throws(() => parseArgs(['--tag', '--repo']), /--tag requires a value/)
  assert.throws(() => parseArgs(['positional']), /unexpected argument/)
})

// -----------------------------------------------------------------------------
// 9. CLI round trip — the exit codes the workflow relies on
// -----------------------------------------------------------------------------

function runCli(args, files = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'compose-release-notes-'))
  try {
    const paths = {}
    for (const [name, contents] of Object.entries(files)) {
      paths[name] = join(dir, name)
      writeFileSync(paths[name], contents, 'utf8')
    }
    const out = join(dir, 'release-notes.md')
    const result = spawnSync(process.execPath, [composerPath, ...args(paths), '--out', out], {
      cwd: repoRoot,
      encoding: 'utf8',
    })
    let body = null
    try {
      body = readFileSync(out, 'utf8')
    } catch {
      body = null
    }
    return { ...result, body }
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

const cliFiles = {
  'checksum.sha256': checksumFor(RC_TAG),
  'UPGRADING.md': UPGRADING,
  'notes.md': NOTES,
  'generated.json': JSON.stringify(GENERATED),
}

test('the CLI writes the body and exits 0 for a complete release candidate', () => {
  const result = runCli(
    (paths) => [
      '--tag', RC_TAG,
      '--prerelease', 'true',
      '--repo', REPO,
      '--asset', assetFor(RC_TAG),
      '--checksum-file', paths['checksum.sha256'],
      '--upgrading', paths['UPGRADING.md'],
      '--notes', paths['notes.md'],
      '--generated-notes', paths['generated.json'],
    ],
    cliFiles,
  )
  assert.equal(result.status, 0, result.stderr)
  assert.ok(result.body.startsWith('[!['), 'the written file opens with the download button')
  assert.ok(result.body.includes('## Highlights'))
})

test('the CLI warns but still writes the body when an RC has no notes file', () => {
  const result = runCli(
    (paths) => [
      '--tag', RC_TAG,
      '--prerelease', 'true',
      '--repo', REPO,
      '--asset', assetFor(RC_TAG),
      '--checksum-file', paths['checksum.sha256'],
      '--upgrading', paths['UPGRADING.md'],
      '--notes', join(paths['UPGRADING.md'], '..', 'does-not-exist.md'),
      '--generated-notes', paths['generated.json'],
    ],
    cliFiles,
  )
  assert.equal(result.status, 0, result.stderr)
  assert.match(result.stderr, /::warning::/)
  assert.ok(result.body.startsWith('[!['))
})

test('the CLI exits non-zero and writes NOTHING when a stable tag is missing its sources', () => {
  const result = runCli(
    (paths) => [
      '--tag', STABLE_TAG,
      '--prerelease', 'false',
      '--repo', REPO,
      '--asset', assetFor(STABLE_TAG),
      '--checksum-file', paths['checksum.sha256'],
      '--upgrading', paths['UPGRADING.md'],
      '--generated-notes', paths['generated.json'],
    ],
    { ...cliFiles, 'checksum.sha256': checksumFor(STABLE_TAG) },
  )
  assert.equal(result.status, 1)
  assert.match(result.stderr, /::error::/)
  assert.equal(result.body, null, 'no half-rendered page may reach --notes-file')
})

test('the CLI refuses a missing required option with exit 2', () => {
  const result = runCli(() => ['--tag', RC_TAG], {})
  assert.equal(result.status, 2)
  assert.match(result.stderr, /usage:/)
})

// -----------------------------------------------------------------------------
// 10. The repo's real UPGRADING.md is readable by the extractor
// -----------------------------------------------------------------------------

test('the shipped UPGRADING.md yields a section for the last stable tag', () => {
  const upgrading = readFileSync(join(repoRoot, 'UPGRADING.md'), 'utf8')
  const section = extractUpgradingSection(upgrading, 'v0.2.0')
  assert.ok(section && section.includes('BREAKING'), 'the real document must match the extractor the workflow uses')
})
