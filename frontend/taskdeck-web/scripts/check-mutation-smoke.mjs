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
// set of killed mutants.

import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const EXPECTED_MUTANT_COUNT = 4

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const reportPath = path.join(projectRoot, 'reports', 'mutation', 'smoke.json')

function fail(message) {
  console.error(`mutation:smoke guard FAILED: ${message}`)
  process.exit(1)
}

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

const mutants = Object.values(files).flatMap((file) =>
  Array.isArray(file?.mutants) ? file.mutants : [],
)

if (mutants.length !== EXPECTED_MUTANT_COUNT) {
  fail(
    `expected ${EXPECTED_MUTANT_COUNT} mutants, found ${mutants.length}. ` +
      'The mutated range in stryker.smoke.config.mjs most likely no longer points at the ' +
      'board-list deletion expression in src/store/board/boardCrudStore.ts. ' +
      'Re-point the line/column coordinates at that expression.',
  )
}

const notKilled = mutants.filter((mutant) => mutant?.status !== 'Killed')
if (notKilled.length > 0) {
  const detail = notKilled
    .map((mutant) => `${mutant?.mutatorName ?? 'unknown'}=${mutant?.status ?? 'unknown'}`)
    .join(', ')
  fail(`every smoke mutant must be Killed; ${notKilled.length} was not (${detail})`)
}

console.log(
  `mutation:smoke guard OK: ${mutants.length}/${EXPECTED_MUTANT_COUNT} mutants killed.`,
)
