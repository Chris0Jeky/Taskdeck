// =============================================================================
// release-desktop-dispatch.test.mjs — dispatch-path regressions for #1795/#1806
// =============================================================================
//
// Two classes of check:
//
//   1. BEHAVIOURAL — `scripts/ci/validate-release-tag.sh` is executed for real
//      with injection-shaped and malformed tags. This is the gate that stands
//      between an attacker-influenced `workflow_dispatch` input and a job that
//      holds `contents: write`, so it is exercised, not merely inspected.
//
//   2. STRUCTURAL — assertions over `.github/workflows/release-desktop.yml`
//      text that pin the invariants a unit test cannot execute (a live dispatch
//      is the only thing that runs a workflow). They are deliberately phrased
//      against the exact strings that would have to change for the hardening to
//      regress.
//
// Run: node --test scripts/ci/release-desktop-dispatch.test.mjs
// =============================================================================

import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const repoRoot = fileURLToPath(new URL('../../', import.meta.url))
const workflowPath = fileURLToPath(new URL('../../.github/workflows/release-desktop.yml', import.meta.url))
// Normalised to LF: a Windows checkout with core.autocrlf=true would otherwise
// break every structural assertion for a reason that has nothing to do with the
// workflow's content.
const workflow = readFileSync(workflowPath, 'utf8').replace(/\r\n/g, '\n')

const bashBin = process.platform === 'win32' ? (process.env.BASH_BIN || 'bash') : 'bash'

function validateTag(...args) {
  return spawnSync(bashBin, ['scripts/ci/validate-release-tag.sh', ...args], {
    cwd: repoRoot,
    encoding: 'utf8',
  })
}

// -----------------------------------------------------------------------------
// 1. Tag grammar — accepted shapes
// -----------------------------------------------------------------------------

const acceptedTags = [
  'v0.0.0',
  'v0.1.0',
  'v1.2.3',
  'v10.20.30',
  'v1.2.3-rc.1',
  'v1.2.3-beta2',
  'v1.2.3+build.5',
  'v0.0.0-dryrun+abc1234', // the generated rehearsal version must stay valid
  'v1.2.3-rc.1+build.5',
]

for (const tag of acceptedTags) {
  test(`accepts the release tag ${tag}`, () => {
    const result = validateTag(tag)
    assert.equal(result.status, 0, `expected ${tag} to be accepted, stderr: ${result.stderr}`)
    assert.equal(result.stdout.trim(), tag, 'the validated tag is echoed for the caller to capture')
  })
}

// -----------------------------------------------------------------------------
// 2. Tag grammar — injection-shaped and invalid inputs
//
// Each of these, before #1795, would have been assigned via
// `TAG="${{ inputs.tag }}"` — i.e. spliced into Bash source — and then used in
// artifact paths, Git refs and `gh release` arguments.
// -----------------------------------------------------------------------------

const rejectedTags = [
  ['empty', ''],
  ['quote break-out', 'v1.0.0"; rm -rf /; #'],
  ['single-quote break-out', "v1.0.0'; id; '"],
  ['command substitution', '$(id)'],
  ['backtick substitution', '`id`'],
  ['variable expansion', 'v1.0.0$HOME'],
  ['brace expansion', 'v1.0.0${IFS}x'],
  ['command chaining', 'v1.0.0 && id'],
  ['pipe', 'v1.0.0 | id'],
  ['semicolon', 'v1.0.0;id'],
  ['newline smuggling', 'v1.0.0\nrm -rf /'],
  ['embedded space', 'v1.0.0 v2'],
  ['path traversal', '../../etc/passwd'],
  ['nested path', 'v1.0.0/../../x'],
  ['branch ref', 'refs/heads/main'],
  ['bare branch name', 'main'],
  ['bare commit sha', '0123456789abcdef0123456789abcdef01234567'],
  ['leading dash (option injection)', '-rf'],
  ['redirect', 'v1.0.0>owned'],
  ['glob', 'v1.0.*'],
  ['no v prefix', '1.2.3'],
  ['missing patch', 'v1.2'],
  ['leading zero major', 'v01.2.3'],
  ['trailing dot', 'v1.2.3.'],
  ['non-ascii', 'v1.2.3-ré'],
  ['too long', `v1.2.3-${'a'.repeat(80)}`],
]

for (const [label, tag] of rejectedTags) {
  test(`rejects ${label}`, () => {
    const result = validateTag(tag)
    assert.equal(result.status, 1, `expected rejection for ${JSON.stringify(tag)}, stdout: ${result.stdout}`)
    assert.equal(result.stdout, '', 'a rejected tag must never be echoed as a usable value')
    assert.match(result.stderr, /::error::/, 'rejection is annotated for the Actions log')
  })
}

test('rejects a carriage return without ever emitting a usable tag', () => {
  // Host-dependent exit code by design: on Linux the value arrives intact and
  // fails the grammar (exit 1); under MSYS bash on Windows the CR splits the
  // argument list and the arity guard fires first (exit 2). Both are closed —
  // what matters is that nothing usable reaches stdout.
  const result = validateTag('v1.0.0\rid')
  assert.notEqual(result.status, 0, 'a carriage return must never be accepted')
  assert.equal(result.stdout, '')
})

test('rejects a wrong invocation instead of defaulting to an empty tag', () => {
  const noArgs = validateTag()
  assert.equal(noArgs.status, 2)
  assert.match(noArgs.stderr, /usage:/)

  const twoArgs = validateTag('v1.0.0', 'v2.0.0')
  assert.equal(twoArgs.status, 2)
})

test('accepts a tag exactly at the length ceiling and rejects one character more', () => {
  const atLimit = `v1.2.3-${'a'.repeat(64 - 'v1.2.3-'.length)}`
  assert.equal(atLimit.length, 64)
  assert.equal(validateTag(atLimit).status, 0)
  assert.equal(validateTag(`${atLimit}a`).status, 1)
})

// -----------------------------------------------------------------------------
// 3. Workflow structure — the untrusted input never becomes shell source
// -----------------------------------------------------------------------------

test('the dispatch tag input reaches Bash only through a step env var', () => {
  assert.match(
    workflow,
    /^\s+RAW_TAG: \$\{\{ inputs\.tag \}\}$/m,
    'inputs.tag must be bound to an env var, not spliced into a run block',
  )

  const inputsTagUses = workflow.match(/\$\{\{ *inputs\.tag[^}]*\}\}/g) ?? []
  assert.deepEqual(
    inputsTagUses,
    ['${{ inputs.tag }}'],
    'inputs.tag may be referenced exactly once, by the RAW_TAG env binding',
  )

  assert.doesNotMatch(
    workflow,
    /TAG="\$\{\{/,
    'the pre-#1795 `TAG="${{ inputs.tag || \'\' }}"` assignment must not come back',
  )
})

test('the tag is validated by the shared grammar gate before any other use', () => {
  const resolveJob = jobBlock('resolve-source')
  const validatorCalls = resolveJob.match(/bash scripts\/ci\/validate-release-tag\.sh/g) ?? []
  assert.ok(
    validatorCalls.length >= 3,
    'the dispatch input, the pushed tag ref and the final tag are each validated',
  )
  // The validator must run before the tag is resolved against the GitHub API.
  assert.ok(
    resolveJob.indexOf('validate-release-tag.sh') < resolveJob.indexOf('git/ref/tags/'),
    'grammar validation precedes ref resolution',
  )
})

// -----------------------------------------------------------------------------
// 4. Workflow structure — one resolved commit, verified by every build
// -----------------------------------------------------------------------------

function jobBlock(name) {
  const start = workflow.indexOf(`\n  ${name}:\n`)
  assert.notEqual(start, -1, `job ${name} must exist`)
  const rest = workflow.slice(start + 1)
  const next = rest.search(/\n {2}[a-z][a-z0-9-]*:\n/)
  return next === -1 ? rest : rest.slice(0, next)
}

test('resolve-source publishes the tag, commit and publish decision as job outputs', () => {
  const job = jobBlock('resolve-source')
  assert.match(job, /tag: \$\{\{ steps\.resolve\.outputs\.tag \}\}/)
  assert.match(job, /sha: \$\{\{ steps\.resolve\.outputs\.sha \}\}/)
  assert.match(job, /publish: \$\{\{ steps\.resolve\.outputs\.publish \}\}/)
})

test('resolve-source dereferences annotated tags and refuses anything else', () => {
  const job = jobBlock('resolve-source')
  assert.match(job, /git\/ref\/tags\/\$\{tag\}/, 'exact tag ref lookup, so a branch cannot pose as a tag')
  assert.match(job, /git\/tags\/\$\{object_sha\}/, 'annotated tag objects are dereferenced to their commit')
  assert.match(job, /unsupported object type/, 'any other object type fails closed')
  assert.match(job, /\^\[0-9a-f\]\{40\}\$/, 'the resolved commit is checked to be a clean 40-hex SHA')
})

for (const job of ['build-frontend', 'build-backend', 'create-release']) {
  test(`${job} builds from the resolved commit and fails closed on a mismatch`, () => {
    const block = jobBlock(job)
    assert.match(
      block,
      /ref: \$\{\{ needs\.resolve-source\.outputs\.sha \}\}/,
      'the checkout is pinned to the resolved release commit',
    )
    assert.match(
      block,
      /Verify checkout matches the resolved release commit/,
      'a post-checkout verification step is present',
    )
    assert.match(
      block,
      /actual_sha="\$\(git rev-parse HEAD\)"/,
      'the verification reads the real post-checkout HEAD',
    )
    assert.match(
      block,
      /does not match the resolved release commit[\s\S]{0,200}?exit 1/,
      'a mismatch exits non-zero rather than warning',
    )
  })
}

test('every checkout refuses to persist Git credentials', () => {
  const checkouts = workflow.match(/uses: actions\/checkout@[^\n]*\n(?: +[^\n]*\n)*/g) ?? []
  assert.equal(checkouts.length, 4, 'resolve-source, build-frontend, build-backend, create-release')
  for (const block of checkouts) {
    assert.match(block, /persist-credentials: false/, `checkout without persist-credentials: false:\n${block}`)
  }
})

test('the publish decision comes from resolve-source, not a re-read of the raw input', () => {
  assert.match(
    workflow,
    /if: needs\.resolve-source\.outputs\.publish == 'true'/,
    'create-release is gated on the validated publish decision',
  )
  assert.doesNotMatch(
    workflow,
    /if: startsWith\(github\.ref, 'refs\/tags\/'\) \|\| inputs\.tag != ''/,
    'the pre-#1795 guard, which never saw the grammar check, must not come back',
  )
})

// -----------------------------------------------------------------------------
// 5. Workflow structure — resumable publish (#1806)
// -----------------------------------------------------------------------------

test('the release is created as a draft, or an existing one is adopted', () => {
  const job = jobBlock('create-release')
  assert.match(job, /gh release create "\$\{RELEASE_TAG\}" \\\n\s+--draft\b/, 'creation is a draft')
  assert.match(job, /--verify-tag/, 'the tag must already exist')
})

test('adoption detects DRAFT releases, which is the whole resumability case', () => {
  const job = jobBlock('create-release')
  assert.match(
    job,
    /gh api "repos\/\$\{GITHUB_REPOSITORY\}\/releases" --paginate/,
    'detection reads the release listing, which includes drafts',
  )
  assert.match(job, /\.draft \| tostring/, 'the draft flag is read for the log line')
  assert.doesNotMatch(
    job,
    /gh release view "\$\{RELEASE_TAG\}"/,
    'the releases/tags/{tag}-backed lookup, which does not return drafts, must not come back',
  )
  const detectAt = job.indexOf('gh api "repos/${GITHUB_REPOSITORY}/releases" --paginate')
  const createAt = job.indexOf('gh release create "${RELEASE_TAG}"')
  assert.ok(detectAt !== -1 && createAt !== -1)
  assert.ok(detectAt < createAt, 'an existing release is detected before create is attempted')
})

test('assets are uploaded per file with --clobber and bounded retries', () => {
  const job = jobBlock('create-release')
  assert.match(job, /gh release upload "\$\{RELEASE_TAG\}" "\$\{asset\}" --clobber/)
  assert.match(job, /for attempt in 1 2 3; do/, 'uploads retry a bounded number of times')
  assert.match(job, /Failed to upload[\s\S]{0,200}?exit 1/, 'exhausted retries fail the job')
  assert.doesNotMatch(
    job,
    /gh release create[\s\S]{0,120}release-assets\/\*/,
    'the all-or-nothing create-with-assets form must not come back',
  )
})

test('a non-regular asset is refused before any upload starts', () => {
  const job = jobBlock('create-release')
  assert.match(job, /if \[ ! -f "\$\{asset\}" \]; then/)
  assert.match(job, /not a regular file/)
})

test('the draft is published only after every asset is uploaded', () => {
  const job = jobBlock('create-release')
  const uploadAt = job.indexOf('gh release upload')
  const publishAt = job.indexOf('gh release edit "${RELEASE_TAG}" --draft=false')
  assert.ok(uploadAt !== -1 && publishAt !== -1)
  assert.ok(uploadAt < publishAt, 'upload precedes the publish flip')
})

test('release assets download to release-assets/, never the repo-tracked artifacts/', () => {
  const job = jobBlock('create-release')
  assert.match(job, /path: release-assets\/$/m)
  assert.doesNotMatch(job, /^\s+path: artifacts\/?$/m, 'artifacts/ is a tracked repo directory (artifacts/openapi)')
})

// -----------------------------------------------------------------------------
// 6. Workflow structure — provenance evidence
// -----------------------------------------------------------------------------

test('the resolved commit ships as release evidence', () => {
  const job = jobBlock('create-release')
  assert.match(job, /taskdeck-\$\{RELEASE_TAG\}-provenance\.txt/)
  assert.match(job, /printf 'commit: %s\\n' "\$\{RELEASE_SHA\}"/)
  assert.match(
    job,
    /gh api "repos\/\$\{GITHUB_REPOSITORY\}\/commits\/\$\{RELEASE_TAG\}"/,
    'the tag is re-checked against the built commit immediately before publishing',
  )
  assert.match(job, /Refusing to publish/, 'a tag moved mid-run fails closed')
})

test('packaging steps consume the resolved tag through the environment', () => {
  const job = jobBlock('build-backend')
  assert.match(job, /RELEASE_TAG: \$\{\{ needs\.resolve-source\.outputs\.tag \}\}/)
  assert.doesNotMatch(job, /steps\.version\.outputs\.tag/, 'the removed per-job tag step must not come back')
})
