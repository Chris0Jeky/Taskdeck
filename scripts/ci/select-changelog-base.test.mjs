// =============================================================================
// select-changelog-base.test.mjs — changelog base selection (#2250 item 3)
// =============================================================================
//
// The defect this suite pins: `gh release list --limit 1` returned the globally
// newest stable release by RELEASE DATE. Re-running `v0.3.0-rc.1` after
// `v0.3.0` had shipped therefore sent `previous_tag_name=v0.3.0`, and the RC
// page rendered a changelog that ran backwards. The base must be the newest
// stable release that sorts STRICTLY BEFORE the tag being built, by semver
// precedence — which is also not string order (`v0.10.0` > `v0.4.0`).
//
// Run: node --test scripts/ci/select-changelog-base.test.mjs
// =============================================================================

import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import process from 'node:process'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

import {
  compareReleaseTags,
  parseReleaseTag,
  selectChangelogBase,
} from './select-changelog-base.mjs'

const scriptPath = fileURLToPath(new URL('./select-changelog-base.mjs', import.meta.url))
const ciRequiredPath = fileURLToPath(new URL('../../.github/workflows/ci-required.yml', import.meta.url))

function runCli(args, candidates) {
  const dir = mkdtempSync(join(tmpdir(), 'changelog-base-'))
  const file = join(dir, 'candidates.txt')
  writeFileSync(file, candidates.join('\n'), 'utf8')
  return spawnSync(process.execPath, [scriptPath, ...args, '--candidates', file], {
    encoding: 'utf8',
  })
}

// -----------------------------------------------------------------------------
// 1. Tag parsing — the same grammar the workflow's gate enforces
// -----------------------------------------------------------------------------

test('parses the release tags Taskdeck ships', () => {
  assert.deepEqual(parseReleaseTag('v0.3.0'), {
    major: 0,
    minor: 3,
    patch: 0,
    prerelease: [],
  })
  assert.deepEqual(parseReleaseTag('v1.2.3-rc.1'), {
    major: 1,
    minor: 2,
    patch: 3,
    prerelease: ['rc', 1],
  })
  assert.deepEqual(parseReleaseTag('v0.0.0-dryrun+abc1234'), {
    major: 0,
    minor: 0,
    patch: 0,
    prerelease: ['dryrun'],
  })
})

test('refuses anything outside the release-tag grammar', () => {
  for (const tag of ['', '0.3.0', 'v0.3', 'main', 'refs/tags/v0.3.0', 'v01.2.3', 'v0.3.0-ré', null]) {
    assert.equal(parseReleaseTag(tag), null, `expected ${JSON.stringify(tag)} to be refused`)
  }
})

// -----------------------------------------------------------------------------
// 2. Ordering — semver precedence, not string order and not release date
// -----------------------------------------------------------------------------

test('orders release numbers numerically, not lexicographically', () => {
  // The bug a plain string sort (or `sort` without -V) would introduce.
  assert.ok(compareReleaseTags('v0.10.0', 'v0.4.0') > 0, 'v0.10.0 is NEWER than v0.4.0')
  assert.ok(compareReleaseTags('v0.9.0', 'v0.10.0') < 0)
  assert.ok(compareReleaseTags('v1.0.0', 'v0.99.99') > 0)
  assert.equal(compareReleaseTags('v0.3.0', 'v0.3.0'), 0)
})

test('a prerelease sorts before its own stable release', () => {
  assert.ok(compareReleaseTags('v0.3.0-rc.1', 'v0.3.0') < 0, 'v0.3.0-rc.1 precedes v0.3.0')
  assert.ok(compareReleaseTags('v0.3.0', 'v0.3.0-rc.1') > 0)
  assert.ok(compareReleaseTags('v0.3.0-rc.1', 'v0.3.0-rc.2') < 0)
  assert.ok(compareReleaseTags('v0.3.0-rc.9', 'v0.3.0-rc.10') < 0, 'rc identifiers compare numerically')
  assert.ok(compareReleaseTags('v0.3.0-alpha', 'v0.3.0-alpha.1') < 0, 'more identifiers outrank fewer')
  assert.ok(compareReleaseTags('v0.3.0-1', 'v0.3.0-alpha') < 0, 'numeric identifiers rank below alphanumeric')
})

test('build metadata is ignored for precedence', () => {
  assert.equal(compareReleaseTags('v0.2.0+build.5', 'v0.2.0'), 0)
  assert.equal(compareReleaseTags('v0.2.0', 'v0.2.0+build.5'), 0)
})

// -----------------------------------------------------------------------------
// 3. Selection
// -----------------------------------------------------------------------------

test('re-running an RC after its stable release shipped does NOT pick that stable release', () => {
  // The exact #2250 item 3 regression: gh returned v0.3.0 (newest by date).
  assert.equal(selectChangelogBase('v0.3.0-rc.1', ['v0.3.0', 'v0.2.0', 'v0.1.1']), 'v0.2.0')
})

test('a stable release is never its own changelog base', () => {
  assert.equal(selectChangelogBase('v0.3.0', ['v0.3.0', 'v0.2.0']), 'v0.2.0')
})

test('the newest candidate strictly before the target wins, by semver', () => {
  assert.equal(selectChangelogBase('v0.4.0', ['v0.10.0', 'v0.3.0', 'v0.2.0']), 'v0.3.0')
  assert.equal(selectChangelogBase('v1.0.0', ['v0.10.0', 'v0.9.0']), 'v0.10.0')
  assert.equal(selectChangelogBase('v0.3.1', ['v0.3.0', 'v0.3.0-rc.1']), 'v0.3.0')
})

test('the first-release path returns nothing rather than guessing', () => {
  assert.equal(selectChangelogBase('v0.1.0', []), null)
  assert.equal(selectChangelogBase('v0.1.0', ['v0.2.0', 'v1.0.0']), null, 'only later releases exist')
})

test('candidates outside the grammar are skipped, not fatal', () => {
  assert.equal(selectChangelogBase('v0.3.0', ['not-a-tag', 'nightly', 'v0.2.0']), 'v0.2.0')
})

test('the selected tag is returned exactly as it was listed', () => {
  assert.equal(
    selectChangelogBase('v0.3.0', ['v0.2.0+build.5']),
    'v0.2.0+build.5',
    'the tag handed to generate-notes must be the one GitHub actually knows',
  )
})

test('an unparseable target tag is refused instead of silently selecting nothing', () => {
  assert.throws(() => selectChangelogBase('main', ['v0.2.0']), /release-tag grammar/)
})

// -----------------------------------------------------------------------------
// 4. CLI — the shape the workflow calls
// -----------------------------------------------------------------------------

test('the CLI prints the selected base and exits 0', () => {
  const result = runCli(['--tag', 'v0.3.0-rc.1'], ['v0.3.0', 'v0.2.0', 'v0.1.1'])
  assert.equal(result.status, 0, result.stderr)
  assert.equal(result.stdout.trim(), 'v0.2.0')
})

test('the CLI prints nothing when there is no earlier release', () => {
  const result = runCli(['--tag', 'v0.1.0'], [])
  assert.equal(result.status, 0, result.stderr)
  assert.equal(result.stdout.trim(), '', 'an empty stdout is what the workflow reads as "no base"')
})

test('the CLI fails closed on a target tag outside the grammar', () => {
  const result = runCli(['--tag', 'refs/heads/main'], ['v0.2.0'])
  assert.equal(result.status, 1)
  assert.equal(result.stdout, '', 'nothing usable may reach stdout on a refusal')
  assert.match(result.stderr, /::error::/)
})

test('the CLI refuses a missing --tag rather than defaulting', () => {
  const result = runCli([], ['v0.2.0'])
  assert.equal(result.status, 2)
  assert.match(result.stderr, /usage/)
})

// -----------------------------------------------------------------------------
// 5. The suite is actually run by the required gate
// -----------------------------------------------------------------------------

test('ci-required runs this suite beside the release workflow contract', () => {
  const ciRequired = readFileSync(ciRequiredPath, 'utf8').replace(/\r\n/g, '\n')
  assert.match(
    ciRequired,
    /node --test scripts\/ci\/release-desktop-dispatch\.test\.mjs/,
    'the dispatch contract is the block this suite belongs to',
  )
  assert.match(
    ciRequired,
    /node --test scripts\/ci\/select-changelog-base\.test\.mjs/,
    'an unrun contract suite protects nothing',
  )
})
