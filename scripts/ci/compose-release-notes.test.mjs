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
  rewriteRelativeLinks,
  MalformedUpgradingSectionError,
  MAX_RELEASE_BODY_LENGTH,
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

// -----------------------------------------------------------------------------
// 4b. Fenced code blocks are content, never headings (#2250 item 5)
// -----------------------------------------------------------------------------

const FENCED = [
  '# Version notes',
  '',
  `## ${RC_TAG} — release candidate`,
  '',
  'Run the migration by hand:',
  '',
  '```bash',
  '# comment that is not a heading',
  '## also not a heading',
  'taskdeck migrate',
  '```',
  '',
  'Then restart the service.',
  '',
  '## v0.2.0 — 2026-08-29',
  '',
  'older notes',
  '',
].join('\n')

test('a fenced block inside the section is kept whole, hash lines and all', () => {
  const section = extractUpgradingSection(FENCED, RC_TAG)
  assert.ok(section.includes('# comment that is not a heading'), section)
  assert.ok(section.includes('## also not a heading'), section)
  assert.ok(section.includes('taskdeck migrate'), section)
  assert.ok(
    section.endsWith('Then restart the service.'),
    `the section must run to the next real heading, got: ${section}`,
  )
  assert.ok(!section.includes('older notes'), 'extraction still stops at the next version heading')
})

test('a fence closed before the next heading restores heading detection', () => {
  const doc = [
    `## ${RC_TAG}`,
    '',
    '~~~text',
    '## inside a tilde fence',
    '~~~',
    '',
    'after the fence',
    '',
    '## v0.2.0',
    '',
    'older notes',
  ].join('\n')
  const section = extractUpgradingSection(doc, RC_TAG)
  assert.ok(section.includes('## inside a tilde fence'), section)
  assert.ok(section.includes('after the fence'), section)
  assert.ok(!section.includes('older notes'), section)
})

test('a tag heading that only appears inside a fence is not a section start', () => {
  const doc = [
    '# Version notes',
    '',
    '```markdown',
    `## ${RC_TAG}`,
    'sample body',
    '```',
    '',
    '## v0.2.0',
    '',
    'older notes',
  ].join('\n')
  assert.equal(extractUpgradingSection(doc, RC_TAG), null)
})

// An unterminated fence would otherwise swallow every OLDER version's notes into
// the Breaking-changes heading, and the body-length guard cannot catch it because
// the whole of UPGRADING.md is far under the limit. The document is malformed, so
// the compose fails at tag time instead of publishing a wrong page.
const UNTERMINATED_FENCE = [
  '# Version notes',
  '',
  `## ${RC_TAG}`,
  '',
  '```bash',
  '## looks like a heading',
  '',
  '## v0.2.0',
  '',
  'older notes',
  '',
].join('\n')

test('an unterminated fence in the section is a malformed document, not a longer section', () => {
  assert.throws(
    () => extractUpgradingSection(UNTERMINATED_FENCE, RC_TAG),
    (error) => {
      assert.ok(error instanceof MalformedUpgradingSectionError, `wrong error type: ${error}`)
      assert.ok(error.message.includes(RC_TAG), error.message)
      assert.match(error.message, /unterminated fenced code block in the UPGRADING section/)
      return true
    },
  )
})

test('a malformed UPGRADING section is an ERROR for an RC as well as a stable tag', () => {
  for (const prerelease of [true, false]) {
    const tag = RC_TAG
    const { errors } = composeReleaseNotes({
      tag,
      prerelease,
      repo: REPO,
      assetName: assetFor(tag),
      checksumText: checksumFor(tag),
      upgradingText: UNTERMINATED_FENCE,
      notesText: NOTES,
      generatedNotes: GENERATED,
    })
    const malformed = errors.find((e) => e.includes('unterminated fenced code block in the UPGRADING section'))
    assert.ok(malformed, `prerelease=${prerelease}: expected a hard failure, got ${JSON.stringify(errors)}`)
    assert.ok(malformed.includes(tag), malformed)
  }
})

test('a closing fence must match the opening character and length', () => {
  const doc = [
    `## ${RC_TAG}`,
    '',
    '````text',
    '```',
    '## still inside the outer fence',
    '````',
    '',
    'after the fence',
    '',
    '## v0.2.0',
    '',
    'older notes',
  ].join('\n')
  const section = extractUpgradingSection(doc, RC_TAG)
  assert.ok(section.includes('## still inside the outer fence'), section)
  assert.ok(section.includes('after the fence'), section)
  assert.ok(!section.includes('older notes'), section)
})

test('a fence indented up to three spaces still opens a block', () => {
  const doc = [
    `## ${RC_TAG}`,
    '',
    '- step one:',
    '',
    '   ```sql',
    '## not a heading',
    '   ```',
    '',
    'after the fence',
    '',
    '## v0.2.0',
    '',
    'older notes',
  ].join('\n')
  const section = extractUpgradingSection(doc, RC_TAG)
  assert.ok(section.includes('## not a heading'), section)
  assert.ok(section.includes('after the fence'), section)
  assert.ok(!section.includes('older notes'), section)
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

test('a body over the GitHub release-body limit fails, naming the overflow', () => {
  const huge = `# Taskdeck\n\n${'x'.repeat(MAX_RELEASE_BODY_LENGTH + 500)}\n`
  const { errors } = compose({ notesText: huge })
  const overflow = errors.find((e) => e.includes('over the'))
  assert.ok(overflow, `expected a length error, got: ${JSON.stringify(errors)}`)
  assert.match(overflow, new RegExp(`over the ${MAX_RELEASE_BODY_LENGTH} limit`))
  assert.match(overflow, /composed body is \d+ characters/)
  assert.ok(MAX_RELEASE_BODY_LENGTH < 125000, "the guard must sit under GitHub's own 125,000 cap")
})

test('a body just under the limit still renders', () => {
  const nearly = `# Taskdeck\n\n${'x'.repeat(MAX_RELEASE_BODY_LENGTH - 12000)}\n`
  const { body, errors } = compose({ notesText: nearly })
  assert.deepEqual(errors, [])
  assert.ok(body.length <= MAX_RELEASE_BODY_LENGTH)
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

test('the CLI fails closed on a malformed UPGRADING section, naming it on the ::error line', () => {
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
    { ...cliFiles, 'UPGRADING.md': UNTERMINATED_FENCE },
  )
  assert.notEqual(result.status, 0, result.stderr)
  assert.match(result.stderr, /::error::/)
  assert.match(result.stderr, /unterminated fenced code block in the UPGRADING section/)
  assert.ok(result.stderr.includes(RC_TAG), result.stderr)
  assert.equal(result.body, null, 'a wrong page must never reach --notes-file')
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

// -----------------------------------------------------------------------------
// 11. Relative links are absolute on the release page (#2250 item 5)
// -----------------------------------------------------------------------------

const LINK_BASE = `https://github.com/${REPO}/blob/${RC_TAG}`

function rewrite(markdown) {
  return rewriteRelativeLinks(markdown, { repo: REPO, tag: RC_TAG })
}

test('a bare anchor resolves against UPGRADING.md at the tag', () => {
  assert.equal(
    rewrite('restore the [snapshot](#automatic-pre-migration-backups) first'),
    `restore the [snapshot](${LINK_BASE}/UPGRADING.md#automatic-pre-migration-backups) first`,
  )
})

test('a relative path becomes a blob URL at the tag, with and without a ./ prefix', () => {
  assert.equal(
    rewrite('[guide](docs/platform/LLM_PROVIDER_SETUP_GUIDE.md)'),
    `[guide](${LINK_BASE}/docs/platform/LLM_PROVIDER_SETUP_GUIDE.md)`,
  )
  assert.equal(rewrite('[guide](./docs/platform/x.md)'), `[guide](${LINK_BASE}/docs/platform/x.md)`)
})

test('a fragment on a relative path is preserved', () => {
  assert.equal(rewrite('[x](docs/x.md#a-section)'), `[x](${LINK_BASE}/docs/x.md#a-section)`)
})

test('absolute, root-relative and mailto destinations are left untouched', () => {
  const untouched = [
    '[a](https://github.com/Chris0Jeky/Taskdeck/pull/2248)',
    '[b](http://example.com/x)',
    '[c](mailto:someone@example.com)',
    '[d](/already/root/relative.md)',
  ].join('\n')
  assert.equal(rewrite(untouched), untouched)
})

test('a link inside a fenced code block is left untouched', () => {
  const doc = ['```markdown', '[x](docs/x.md)', '```', '[y](docs/y.md)'].join('\n')
  assert.equal(
    rewrite(doc),
    ['```markdown', '[x](docs/x.md)', '```', `[y](${LINK_BASE}/docs/y.md)`].join('\n'),
  )
})

test('a link inside an inline code span is left untouched', () => {
  assert.equal(
    rewrite('write `[x](docs/x.md)` and link [y](docs/y.md)'),
    `write \`[x](docs/x.md)\` and link [y](${LINK_BASE}/docs/y.md)`,
  )
})

test('a reference-style link definition gets the same treatment', () => {
  assert.equal(rewrite('[guide]: docs/x.md'), `[guide]: ${LINK_BASE}/docs/x.md`)
  assert.equal(rewrite('[anchor]: #automatic-pre-migration-backups'), `[anchor]: ${LINK_BASE}/UPGRADING.md#automatic-pre-migration-backups`)
  assert.equal(rewrite('[keep]: https://example.com/x'), '[keep]: https://example.com/x')
})

test('an angle-bracketed destination is rewritten inside its brackets', () => {
  assert.equal(rewrite('[x](<docs/x.md>)'), `[x](<${LINK_BASE}/docs/x.md>)`)
  assert.equal(rewrite('[x](<./docs/x.md#a-section>)'), `[x](<${LINK_BASE}/docs/x.md#a-section>)`)
  assert.equal(rewrite('[x](<#anchor>)'), `[x](<${LINK_BASE}/UPGRADING.md#anchor>)`)
  assert.equal(rewrite('[x](<https://example.com/x>)'), '[x](<https://example.com/x>)')
  assert.equal(rewrite('[ref]: <docs/x.md>'), `[ref]: <${LINK_BASE}/docs/x.md>`)
})

test('prose shaped like a reference definition is left alone, but its inline links are not', () => {
  assert.equal(
    rewrite('[Note]: see [the guide](docs/x.md) before upgrading'),
    `[Note]: see [the guide](${LINK_BASE}/docs/x.md) before upgrading`,
  )
  assert.equal(rewrite('[Warning]: back up first, then run the migration'), '[Warning]: back up first, then run the migration')
  // A real definition, with and without each CommonMark title form, still rewrites.
  assert.equal(rewrite('[guide]: docs/x.md "The guide"'), `[guide]: ${LINK_BASE}/docs/x.md "The guide"`)
  assert.equal(rewrite("[guide]: docs/x.md 'The guide'"), `[guide]: ${LINK_BASE}/docs/x.md 'The guide'`)
  assert.equal(rewrite('[guide]: docs/x.md (The guide)'), `[guide]: ${LINK_BASE}/docs/x.md (The guide)`)
})

test('the tag is percent-encoded exactly as the UPGRADING fallback link encodes it', () => {
  const oddTag = 'v1.0.0+build/1'
  assert.equal(
    rewriteRelativeLinks('[x](docs/x.md)', { repo: REPO, tag: oddTag }),
    `[x](https://github.com/${REPO}/blob/${encodeURIComponent(oddTag)}/docs/x.md)`,
  )
})

const UPGRADING_WITH_LINKS = UPGRADING.replace(
  '- The `AddApiKeyScopes` migration backfills existing keys to Full.',
  '- Restore the [snapshot](#automatic-pre-migration-backups); see [the guide](docs/platform/LLM_PROVIDER_SETUP_GUIDE.md).',
)

test('the composed page carries absolute links for the UPGRADING section', () => {
  const { body, errors } = compose({ upgradingText: UPGRADING_WITH_LINKS })
  assert.deepEqual(errors, [])
  assert.ok(body.includes(`${LINK_BASE}/UPGRADING.md#automatic-pre-migration-backups`), body)
  assert.ok(body.includes(`${LINK_BASE}/docs/platform/LLM_PROVIDER_SETUP_GUIDE.md`), body)
  assert.ok(!body.includes('](#automatic-pre-migration-backups)'), 'no bare anchor may survive onto the page')
})

test('the release-body length guard still fires after links are rewritten', () => {
  const huge = [
    '# Version notes',
    '',
    `## ${RC_TAG} — release candidate (prerelease)`,
    '',
    '[x](docs/x.md)',
    '',
    'y'.repeat(MAX_RELEASE_BODY_LENGTH),
    '',
  ].join('\n')
  const { body, errors } = compose({ upgradingText: huge })
  assert.ok(body.includes(`${LINK_BASE}/docs/x.md`), 'the rewrite still ran on the oversized section')
  const overflow = errors.find((e) => e.includes('over the'))
  assert.ok(overflow, `expected a length error, got: ${JSON.stringify(errors)}`)
})
