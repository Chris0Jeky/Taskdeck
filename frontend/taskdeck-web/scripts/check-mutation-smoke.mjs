#!/usr/bin/env node
// Fail-closed assertion for `npm run mutation:smoke`.
//
// Stryker's `thresholds.break` cannot catch an empty mutation probe: a stale
// source range can yield zero mutants, a NaN score, and exit code 0. This guard
// therefore validates the report schema, exact source seam, a non-empty mutant
// set, and an all-Killed result. It deliberately does not pin the number of
// mutants, because that count belongs to the installed Stryker mutator set
// rather than to Taskdeck's source contract.

import { readFile } from 'node:fs/promises'
import { fileURLToPath, pathToFileURL } from 'node:url'
import path from 'node:path'
import { mutationSmokeContract } from '../stryker.smoke.contract.mjs'

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
export const defaultMutationSmokeReportPath = path.join(
  projectRoot,
  'reports',
  'mutation',
  'smoke.json',
)

export class SmokeGuardError extends Error {}

function fail(message) {
  throw new SmokeGuardError(message)
}

function sourceSlice(source, contract) {
  if (
    contract.start.line !== contract.end.line ||
    contract.start.line < 1 ||
    contract.start.column < 1 ||
    contract.end.column <= contract.start.column
  ) {
    fail('the mutation smoke source contract has an unsupported or invalid range')
  }

  const line = source.split(/\r?\n/u)[contract.start.line - 1]
  if (line === undefined) {
    return null
  }

  // Contract columns are Stryker's 1-based, end-exclusive coordinates; `slice`
  // takes 0-based offsets, so both ends shift by one. Slicing with the raw
  // Stryker columns silently selects a window one character to the right, which
  // matches a fabricated fixture but never the real file.
  return line.slice(contract.start.column - 1, contract.end.column - 1)
}

export function validateMutationSmokeReport(report, contract = mutationSmokeContract) {
  if (report?.schemaVersion !== contract.schemaVersion) {
    fail(
      `unsupported smoke report schema ${JSON.stringify(report?.schemaVersion)}; ` +
        `expected ${JSON.stringify(contract.schemaVersion)}`,
    )
  }

  const files = report?.files
  if (!files || typeof files !== 'object' || Array.isArray(files)) {
    fail('the smoke report has no valid "files" section')
  }

  const reportedFiles = Object.keys(files)
  if (!Object.prototype.hasOwnProperty.call(files, contract.file)) {
    fail(
      `the smoke report has no entry for ${contract.file}. ` +
        `It reported: ${reportedFiles.length > 0 ? reportedFiles.join(', ') : '(no files)'}.`,
    )
  }

  const fileReport = files[contract.file]
  if (!fileReport || typeof fileReport.source !== 'string') {
    fail(`the smoke report entry for ${contract.file} has no source text`)
  }

  const selectedSource = sourceSlice(fileReport.source, contract)
  if (selectedSource !== contract.source) {
    fail(
      `the configured mutation range no longer selects the expected source seam in ${contract.file}. ` +
        `Expected ${JSON.stringify(contract.source)}, found ${JSON.stringify(selectedSource)}. ` +
        'Move the shared range in stryker.smoke.contract.mjs with the expression.',
    )
  }

  if (!Array.isArray(fileReport.mutants)) {
    fail(`the smoke report entry for ${contract.file} has no mutant list`)
  }

  const mutants = fileReport.mutants
  if (mutants.length === 0) {
    fail(
      `the exact source seam in ${contract.file} produced zero mutants. ` +
        'Stryker must instrument at least one mutant for the activation proof to be meaningful.',
    )
  }

  const notKilled = mutants.filter((mutant) => mutant?.status !== 'Killed')
  if (notKilled.length > 0) {
    const detail = notKilled
      .map((mutant) => `${mutant?.mutatorName ?? 'unknown'}=${mutant?.status ?? 'unknown'}`)
      .join(', ')
    fail(`every smoke mutant must be Killed; ${notKilled.length} was not (${detail})`)
  }

  return (
    `mutation:smoke guard OK: ${mutants.length} mutant` +
    `${mutants.length === 1 ? '' : 's'} killed in ${contract.file}; source range matched.`
  )
}

export async function runMutationSmokeGuard(reportPath = defaultMutationSmokeReportPath) {
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

  return validateMutationSmokeReport(report)
}

function isDirectExecution() {
  if (!process.argv[1]) return false
  return import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
}

if (isDirectExecution()) {
  try {
    console.log(await runMutationSmokeGuard())
  } catch (error) {
    if (!(error instanceof SmokeGuardError)) throw error
    // Set the exit code rather than calling process.exit(), which can discard a
    // pending stderr write when stderr is a pipe, as it is on a CI runner.
    console.error(`mutation:smoke guard FAILED: ${error.message}`)
    process.exitCode = 1
  }
}
