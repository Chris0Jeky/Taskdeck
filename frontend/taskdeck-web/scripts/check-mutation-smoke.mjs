#!/usr/bin/env node
// Fail-closed assertion for `npm run mutation:smoke`.
//
// Stryker's `thresholds.break` cannot catch the failure mode this guard exists
// to prevent. When `stryker.smoke.config.mjs` selects a source range that no
// longer holds an expression -- because the mutated seam moved -- Stryker
// instruments zero mutants, reports a mutation score of NaN, logs "NaN is
// greater than or equal to break threshold 100" and exits 0. The activation
// guard would then be green while proving nothing.
//
// This script reads the smoke run's JSON report and requires a known, non-zero
// set of killed mutants from the expected source file.

import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const EXPECTED_FILE = 'src/store/board/boardCrudStore.ts'
const EXPECTED_MUTANT_COUNT = 4

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const reportPath = path.join(projectRoot, 'reports', 'mutation', 'smoke.json')

class SmokeGuardError extends Error {}

function fail(message) {
  throw new SmokeGuardError(message)
}

async function check() {
  let report
  try {
    report = JSON.parse(await readFile(reportPath, 'utf8'))
  } catch (error) {
    fail(
      `could not read the smoke report at ${reportPath}. ` +
        'Stryker must run with the json reporter before this check. ' +
        `Underlying error: ${error.message}`,
    )
  }

  const files = report?.files
  if (!files || typeof files !== 'object') {
    fail(`the smoke report at ${reportPath} has no "files" section`)
  }

  // Pin the source file as well as the mutant count. Without this the guard
  // would accept any report that happens to carry four killed mutants --
  // including a stale report left behind by an earlier run, since
  // `reports/mutation/` is gitignored and survives branch switches.
  const reportedFiles = Object.keys(files)
  if (!Object.prototype.hasOwnProperty.call(files, EXPECTED_FILE)) {
    fail(
      `the smoke report has no entry for ${EXPECTED_FILE}. ` +
        `It reported: ${reportedFiles.length > 0 ? reportedFiles.join(', ') : '(no files)'}. ` +
        'Either the mutated range in stryker.smoke.config.mjs no longer points at the ' +
        'board-list deletion expression, or this report is stale -- delete ' +
        `${reportPath} and re-run the smoke.`,
    )
  }

  const mutants = Array.isArray(files[EXPECTED_FILE]?.mutants) ? files[EXPECTED_FILE].mutants : []

  if (mutants.length !== EXPECTED_MUTANT_COUNT) {
    fail(
      `expected ${EXPECTED_MUTANT_COUNT} mutants in ${EXPECTED_FILE}, found ${mutants.length}. ` +
        'Most likely the mutated range in stryker.smoke.config.mjs no longer points at the ' +
        'board-list deletion expression -- re-point its line/column coordinates. ' +
        'A Stryker upgrade that changes the mutator set for that expression produces the ' +
        `same symptom; in that case update EXPECTED_MUTANT_COUNT instead.`,
    )
  }

  const notKilled = mutants.filter((mutant) => mutant?.status !== 'Killed')
  if (notKilled.length > 0) {
    const detail = notKilled
      .map((mutant) => `${mutant?.mutatorName ?? 'unknown'}=${mutant?.status ?? 'unknown'}`)
      .join(', ')
    fail(`every smoke mutant must be Killed; ${notKilled.length} was not (${detail})`)
  }

  return `mutation:smoke guard OK: ${mutants.length}/${EXPECTED_MUTANT_COUNT} mutants killed in ${EXPECTED_FILE}.`
}

try {
  console.log(await check())
} catch (error) {
  if (!(error instanceof SmokeGuardError)) throw error
  // Set the exit code rather than calling process.exit(), which can discard a
  // pending stderr write when stderr is a pipe -- as it is on a CI runner.
  console.error(`mutation:smoke guard FAILED: ${error.message}`)
  process.exitCode = 1
}
