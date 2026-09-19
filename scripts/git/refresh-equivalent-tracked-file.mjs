#!/usr/bin/env node
/**
 * Safely refresh Git's stat metadata for one tracked Vitest snapshot whose
 * worktree bytes and staged/ordinary diff prove it is content-identical.
 *
 * Windows fast path:
 *   node scripts/git/refresh-equivalent-tracked-file.mjs \
 *     --repo . \
 *     --path frontend/taskdeck-web/src/tests/components/paper/__snapshots__/PaperConfidenceDial.spec.ts.snap
 *
 * A clean path is a no-op. The command refuses real content changes, staged
 * changes, untracked files, symlinks, directories, non-.snap files, and paths
 * outside the repository. It emits a bounded JSON before/after receipt.
 */
import { spawnSync } from 'node:child_process'
import { lstatSync, statSync } from 'node:fs'
import { isAbsolute, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

function runGit(repository, args, { allowFailure = false } = {}) {
  const result = spawnSync('git', ['-C', repository, ...args], {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  if (result.error) throw new Error(`Git could not start: ${result.error.message}`)

  const status = result.status ?? 1
  if (!allowFailure && status !== 0) {
    const detail = (result.stderr || result.stdout || '').trim()
    throw new Error(`Git ${args[0]} failed${detail ? `: ${detail}` : ''}`)
  }

  return {
    status,
    stdout: (result.stdout || '').trim(),
    stderr: (result.stderr || '').trim(),
  }
}

function requireQuiet(repository, args, refusalMessage) {
  const result = runGit(repository, args, { allowFailure: true })
  if (result.status === 0) return
  if (result.status === 1) throw new Error(refusalMessage)
  throw new Error(
    `Git ${args[0]} failed: ${result.stderr || result.stdout || `exit ${result.status}`}`,
  )
}

function validateRelativeSnapshotPath(root, candidate) {
  if (typeof candidate !== 'string' || candidate.trim() === '') {
    throw new Error('An exact repository-relative .snap path is required.')
  }

  const normalized = candidate.replaceAll('\\', '/')
  if (
    normalized.startsWith('/') ||
    /^[A-Za-z]:\//.test(normalized) ||
    normalized.split('/').some((segment) => segment === '..' || segment === '')
  ) {
    throw new Error('The snapshot path must be one exact repository-relative path.')
  }
  if (!normalized.endsWith('.snap')) {
    throw new Error('The repair is restricted to tracked .snap files.')
  }

  const absolute = resolve(root, ...normalized.split('/'))
  const fromRoot = relative(root, absolute)
  if (fromRoot.startsWith('..') || isAbsolute(fromRoot)) {
    throw new Error('The snapshot path must stay inside the repository.')
  }

  return { absolute, normalized }
}

function objectEvidence(repository, relativePath) {
  const indexObject = runGit(repository, [
    'rev-parse',
    '--verify',
    `:${relativePath}`,
  ]).stdout
  const rawWorkingObject = runGit(repository, [
    'hash-object',
    '--no-filters',
    '--',
    relativePath,
  ]).stdout
  const filteredWorkingObject = runGit(repository, [
    'hash-object',
    `--path=${relativePath}`,
    '--',
    relativePath,
  ]).stdout

  return { indexObject, rawWorkingObject, filteredWorkingObject }
}

function exactStatus(repository, relativePath) {
  return runGit(repository, [
    'status',
    '--porcelain=v2',
    '--untracked-files=all',
    '--',
    relativePath,
  ]).stdout
}

export function refreshEquivalentTrackedFile({ repository = process.cwd(), relativePath }) {
  const root = runGit(repository, ['rev-parse', '--show-toplevel']).stdout
  const path = validateRelativeSnapshotPath(root, relativePath)
  const file = lstatSync(path.absolute)
  if (!file.isFile() || file.isSymbolicLink()) {
    throw new Error('The exact snapshot path must be a regular non-symlink file.')
  }

  const tracked = runGit(
    root,
    ['ls-files', '--error-unmatch', '--', path.normalized],
    { allowFailure: true },
  )
  if (tracked.status !== 0) {
    throw new Error('The exact snapshot path must already be tracked by Git.')
  }

  const statusBefore = exactStatus(root, path.normalized)
  const before = {
    status: statusBefore,
    bytes: statSync(path.absolute).size,
    ...objectEvidence(root, path.normalized),
  }

  if (statusBefore === '') {
    return {
      action: 'clean',
      path: path.normalized,
      before,
      after: { ...before },
    }
  }

  const statusLines = statusBefore.split(/\r?\n/).filter(Boolean)
  const fields = statusLines.length === 1 ? statusLines[0].split(' ') : []
  if (fields[0] !== '1' || fields[1] !== '.M') {
    throw new Error(
      `Refusing staged or unsupported status for the exact snapshot path: ${statusBefore}`,
    )
  }

  requireQuiet(
    root,
    ['diff', '--cached', '--quiet', '--', path.normalized],
    'Refusing because the staged diff is not empty.',
  )
  requireQuiet(
    root,
    ['diff', '--quiet', '--', path.normalized],
    'Refusing because the ordinary diff is not empty.',
  )

  if (
    before.indexObject !== before.rawWorkingObject ||
    before.indexObject !== before.filteredWorkingObject
  ) {
    throw new Error(
      'Refusing because the index, raw worktree, and filtered worktree objects differ.',
    )
  }

  // All content evidence is identical and the path is exact. `git add` only
  // refreshes the index stat cache for this file; postconditions below prove it
  // did not create staged or working-tree content changes.
  runGit(root, ['add', '--', path.normalized])

  requireQuiet(
    root,
    ['diff', '--cached', '--quiet', '--', path.normalized],
    'The exact-path refresh unexpectedly created a staged diff.',
  )
  requireQuiet(
    root,
    ['diff', '--quiet', '--', path.normalized],
    'The exact-path refresh unexpectedly left an ordinary diff.',
  )

  const statusAfter = exactStatus(root, path.normalized)
  const after = {
    status: statusAfter,
    bytes: statSync(path.absolute).size,
    ...objectEvidence(root, path.normalized),
  }

  if (statusAfter !== '') {
    throw new Error(`The exact snapshot path remained dirty after refresh: ${statusAfter}`)
  }
  if (
    after.bytes !== before.bytes ||
    after.indexObject !== before.indexObject ||
    after.rawWorkingObject !== before.rawWorkingObject ||
    after.filteredWorkingObject !== before.filteredWorkingObject
  ) {
    throw new Error('The exact-path refresh changed file bytes or Git object identity.')
  }

  return {
    action: 'refreshed',
    path: path.normalized,
    before,
    after,
  }
}

function parseArguments(argv) {
  const options = { repository: process.cwd(), relativePath: null, help: false }

  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index]
    if (argument === '--repo') {
      options.repository = argv[++index]
    } else if (argument === '--path') {
      options.relativePath = argv[++index]
    } else if (argument === '--help' || argument === '-h') {
      options.help = true
    } else {
      throw new Error(`Unknown argument: ${argument}`)
    }
  }

  return options
}

function printUsage() {
  process.stdout.write(
    'Usage: node scripts/git/refresh-equivalent-tracked-file.mjs --repo <repository> --path <tracked.snap>\n',
  )
}

const isDirectEntry = process.argv[1]
  ? resolve(process.argv[1]) === fileURLToPath(import.meta.url)
  : false

if (isDirectEntry) {
  try {
    const options = parseArguments(process.argv.slice(2))
    if (options.help) {
      printUsage()
    } else {
      const receipt = refreshEquivalentTrackedFile(options)
      process.stdout.write(`${JSON.stringify(receipt)}\n`)
    }
  } catch (error) {
    process.stderr.write(`Equivalent tracked-file refresh refused: ${error.message}\n`)
    process.exitCode = 1
  }
}
