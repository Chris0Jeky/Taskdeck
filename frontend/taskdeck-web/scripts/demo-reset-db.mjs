/**
 * Taskdeck Demo DB Reset
 *
 * Deletes the canonical dev SQLite database used by `dotnet run` and the demo seeder.
 * The backend will recreate the DB (via EF Core migration) on next startup.
 *
 * Usage:
 *   npm run demo:reset-db
 *   npm run demo:reset-db -- --all   # also delete e2e/demo/ci DB files
 */

import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const REPO_ROOT = path.resolve(__dirname, '..', '..', '..')

const CANONICAL_DB = path.join(REPO_ROOT, 'backend', 'src', 'Taskdeck.Api', 'taskdeck.db')

let failures = 0

function findExtraDbFiles() {
  const found = []
  const candidates = [
    path.join(REPO_ROOT, 'taskdeck.db'),
    ...findGlobMatches(path.join(REPO_ROOT, 'backend', 'src', 'Taskdeck.Api'), 'taskdeck.e2e'),
    ...findGlobMatches(REPO_ROOT, 'taskdeck.e2e'),
    ...findGlobMatches(path.join(REPO_ROOT, 'frontend', 'taskdeck-web'), 'taskdeck.e2e'),
    ...findGlobMatches(path.join(REPO_ROOT, 'frontend', 'taskdeck-web'), 'taskdeck.demo'),
  ]
  for (const f of candidates) {
    if (f !== CANONICAL_DB && fs.existsSync(f)) {
      found.push(f)
    }
  }
  return found
}

function findGlobMatches(dir, prefix) {
  const results = []
  try {
    for (const entry of fs.readdirSync(dir)) {
      if (entry.startsWith(prefix) && entry.endsWith('.db')) {
        results.push(path.join(dir, entry))
      }
    }
  } catch {
    // Directory doesn't exist
  }
  return results
}

function deleteFile(filePath) {
  const rel = path.relative(REPO_ROOT, filePath)
  for (const suffix of ['', '-wal', '-shm']) {
    const target = filePath + suffix
    try {
      fs.unlinkSync(target)
      console.log(`  deleted: ${rel}${suffix}`)
    } catch (err) {
      if (err && err.code === 'ENOENT') {
        continue
      }
      console.error(`  FAILED: ${rel}${suffix} — ${err.message}`)
      failures++
    }
  }
}

const args = process.argv.slice(2)
const deleteAll = args.includes('--all')
const showHelp = args.includes('--help') || args.includes('-h')

if (showHelp) {
  console.log(`
Usage: npm run demo:reset-db [-- [options]]

Options:
  --all      Also delete e2e, demo, CI, and repo-root database files
  --help, -h Print this usage information

Canonical dev DB: backend/src/Taskdeck.Api/taskdeck.db
`.trim())
  process.exit(0)
}

console.log('\nTaskdeck demo DB reset')
console.log('----------------------------------------')

console.log('Deleting canonical dev DB (if present):')
deleteFile(CANONICAL_DB)

if (deleteAll) {
  const extras = findExtraDbFiles()
  if (extras.length) {
    console.log(`\nDeleting ${extras.length} extra DB file(s):`)
    for (const f of extras) {
      deleteFile(f)
    }
  } else {
    console.log('\nNo extra DB files found.')
  }
}

if (failures > 0) {
  console.error(`\n${failures} file(s) could not be deleted. Is the backend still running?`)
  process.exitCode = 1
} else {
  console.log('\nDone. Restart the backend to trigger EF Core migration on a fresh DB.')
}
