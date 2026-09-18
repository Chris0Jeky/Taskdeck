import assert from 'node:assert/strict'
import { execFileSync, spawnSync } from 'node:child_process'
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

import { refreshEquivalentTrackedFile } from './refresh-equivalent-tracked-file.mjs'

const cliPath = fileURLToPath(new URL('./refresh-equivalent-tracked-file.mjs', import.meta.url))
const snapshotPath = 'src/tests/__snapshots__/Equivalent.spec.ts.snap'
const snapshotContent = 'exports[`equivalent snapshot 1`] = `\nvalue\n`;\n'

function git(repository, ...args) {
  return execFileSync('git', ['-C', repository, ...args], {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
  }).trim()
}

function gitExit(repository, ...args) {
  return spawnSync('git', ['-C', repository, ...args], {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
  }).status
}

function createRepository() {
  const root = mkdtempSync(join(tmpdir(), 'taskdeck-equivalent-index-'))
  const repository = join(root, 'repo')
  mkdirSync(repository)
  git(repository, 'init', '--quiet')
  git(repository, 'config', 'user.email', 'taskdeck-tests@example.invalid')
  git(repository, 'config', 'user.name', 'Taskdeck tests')
  git(repository, 'config', 'core.autocrlf', 'true')
  git(repository, 'config', 'core.safecrlf', 'false')

  writeFileSync(join(repository, '.gitattributes'), '* text=auto\n', 'utf8')
  const absoluteSnapshot = join(repository, snapshotPath)
  mkdirSync(dirname(absoluteSnapshot), { recursive: true })
  writeFileSync(absoluteSnapshot, snapshotContent, 'utf8')
  writeFileSync(join(repository, 'neighbor.txt'), 'untouched\n', 'utf8')
  git(repository, 'add', '--', '.gitattributes', snapshotPath, 'neighbor.txt')
  git(repository, 'commit', '--quiet', '-m', 'fixture')

  return {
    root,
    repository,
    absoluteSnapshot,
    dispose() {
      rmSync(root, { recursive: true, force: true })
    },
  }
}

function reproduceFalseDirty(fixture) {
  rmSync(fixture.absoluteSnapshot)
  git(fixture.repository, 'checkout', '--', snapshotPath)
  const checkoutBytes = readFileSync(fixture.absoluteSnapshot)
  assert.ok(checkoutBytes.includes(Buffer.from('\r\n')), 'autocrlf checkout must materialize CRLF bytes')

  // Record the checkout representation in the index stat cache, then model the
  // unchanged Vitest snapshot serializer writing canonical LF bytes.
  git(fixture.repository, 'update-index', '--refresh')
  writeFileSync(fixture.absoluteSnapshot, snapshotContent, 'utf8')

  const status = git(fixture.repository, 'status', '--porcelain=v2', '--', snapshotPath)
  const indexObject = git(fixture.repository, 'rev-parse', `:${snapshotPath}`)
  const rawWorkingObject = git(
    fixture.repository,
    'hash-object',
    '--no-filters',
    '--',
    snapshotPath,
  )

  assert.match(status, /^1 \.M /, 'fixture must reproduce the false dirty worktree status')
  assert.equal(gitExit(fixture.repository, 'diff', '--quiet', '--', snapshotPath), 0)
  assert.equal(gitExit(fixture.repository, 'diff', '--cached', '--quiet', '--', snapshotPath), 0)
  assert.equal(rawWorkingObject, indexObject)

  return { indexObject, rawWorkingObject, status }
}

test('refreshes only equivalent snapshot stat metadata and records before/after evidence', () => {
  const fixture = createRepository()
  try {
    const before = reproduceFalseDirty(fixture)
    const bytesBefore = readFileSync(fixture.absoluteSnapshot)
    const neighborBefore = readFileSync(join(fixture.repository, 'neighbor.txt'))

    const result = refreshEquivalentTrackedFile({
      repository: fixture.repository,
      relativePath: snapshotPath,
    })

    assert.equal(result.action, 'refreshed')
    assert.equal(result.path, snapshotPath)
    assert.equal(result.before.status, before.status)
    assert.equal(result.before.indexObject, before.indexObject)
    assert.equal(result.before.rawWorkingObject, before.rawWorkingObject)
    assert.equal(result.after.status, '')
    assert.equal(result.after.indexObject, before.indexObject)
    assert.deepEqual(readFileSync(fixture.absoluteSnapshot), bytesBefore)
    assert.deepEqual(readFileSync(join(fixture.repository, 'neighbor.txt')), neighborBefore)
    assert.equal(gitExit(fixture.repository, 'diff', '--cached', '--quiet', '--', snapshotPath), 0)
    assert.equal(gitExit(fixture.repository, 'diff', '--quiet', '--', snapshotPath), 0)
  } finally {
    fixture.dispose()
  }
})

test('the documented CLI emits the same bounded receipt', () => {
  const fixture = createRepository()
  try {
    reproduceFalseDirty(fixture)
    const command = spawnSync(
      process.execPath,
      [cliPath, '--repo', fixture.repository, '--path', snapshotPath],
      { encoding: 'utf8' },
    )

    assert.equal(command.status, 0, command.stderr)
    const receipt = JSON.parse(command.stdout)
    assert.equal(receipt.action, 'refreshed')
    assert.equal(receipt.path, snapshotPath)
    assert.equal(receipt.after.status, '')
  } finally {
    fixture.dispose()
  }
})

test('a clean tracked snapshot is a no-op', () => {
  const fixture = createRepository()
  try {
    const result = refreshEquivalentTrackedFile({
      repository: fixture.repository,
      relativePath: snapshotPath,
    })

    assert.equal(result.action, 'clean')
    assert.equal(result.before.status, '')
    assert.equal(result.after.status, '')
  } finally {
    fixture.dispose()
  }
})

test('refuses a real worktree change and does not stage it', () => {
  const fixture = createRepository()
  try {
    writeFileSync(fixture.absoluteSnapshot, `${snapshotContent}changed\n`, 'utf8')

    assert.throws(
      () => refreshEquivalentTrackedFile({
        repository: fixture.repository,
        relativePath: snapshotPath,
      }),
      /ordinary diff is not empty/i,
    )
    assert.equal(gitExit(fixture.repository, 'diff', '--cached', '--quiet', '--', snapshotPath), 0)
    assert.notEqual(gitExit(fixture.repository, 'diff', '--quiet', '--', snapshotPath), 0)
  } finally {
    fixture.dispose()
  }
})

test('refuses staged, untracked, non-snapshot, and escaping paths', () => {
  const fixture = createRepository()
  try {
    writeFileSync(fixture.absoluteSnapshot, `${snapshotContent}staged\n`, 'utf8')
    git(fixture.repository, 'add', '--', snapshotPath)
    assert.throws(
      () => refreshEquivalentTrackedFile({ repository: fixture.repository, relativePath: snapshotPath }),
      /staged or unsupported status/i,
    )

    writeFileSync(join(fixture.repository, 'untracked.snap'), 'value\n', 'utf8')
    assert.throws(
      () => refreshEquivalentTrackedFile({ repository: fixture.repository, relativePath: 'untracked.snap' }),
      /tracked/i,
    )
    assert.throws(
      () => refreshEquivalentTrackedFile({ repository: fixture.repository, relativePath: 'neighbor.txt' }),
      /\.snap/i,
    )
    assert.throws(
      () => refreshEquivalentTrackedFile({ repository: fixture.repository, relativePath: '../escape.snap' }),
      /repository-relative/i,
    )
  } finally {
    fixture.dispose()
  }
})
