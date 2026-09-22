import assert from 'node:assert/strict'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'
import {
  SmokeGuardError,
  runMutationSmokeGuard,
  validateMutationSmokeReport,
} from '../../../frontend/taskdeck-web/scripts/check-mutation-smoke.mjs'
import {
  mutationSmokeContract,
  mutationSmokeRange,
} from '../../../frontend/taskdeck-web/stryker.smoke.contract.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..')
const trackedSourcePath = path.join(
  repositoryRoot,
  'frontend/taskdeck-web',
  mutationSmokeContract.file,
)

/**
 * The real tracked file, exactly as Stryker reports it. The positive fixtures
 * use this rather than a synthetic line so a stale contract line or an
 * off-by-one column cannot pass by construction: a hand-built line indented to
 * the contract's own column matches any column convention and proves nothing.
 */
async function trackedSource() {
  return readFile(trackedSourcePath, 'utf8')
}

/**
 * Synthetic source for the NEGATIVE controls only. Columns are Stryker's
 * 1-based coordinates, so the seam starts after `column - 1` padding.
 */
function sourceWithContractSeam(source = mutationSmokeContract.source, columnShift = 0) {
  const lines = Array.from({ length: mutationSmokeContract.start.line }, () => '')
  lines[mutationSmokeContract.start.line - 1] =
    ' '.repeat(mutationSmokeContract.start.column - 1 + columnShift) + source
  return lines.join('\n')
}

async function report({
  mutants = [{ mutatorName: 'EqualityOperator', status: 'Killed' }],
  ...overrides
} = {}) {
  return {
    schemaVersion: mutationSmokeContract.schemaVersion,
    files: {
      [mutationSmokeContract.file]: {
        source: await trackedSource(),
        mutants,
      },
    },
    ...overrides,
  }
}

function assertGuardFailure(candidate, pattern) {
  assert.throws(
    () => validateMutationSmokeReport(candidate),
    (error) => error instanceof SmokeGuardError && pattern.test(error.message),
  )
}

test('the shared range addresses the seam in the real tracked source', async () => {
  // Literal, not derived from the same constants: Stryker's `mutate` columns
  // are 0-based while the contract's are 1-based, so a derived assertion would
  // pass whichever base `mutationSmokeRange` happened to use. Passing column 28
  // starts the range inside the expression and drops its outermost mutant.
  assert.equal(mutationSmokeRange, 'src/store/board/boardCrudStore.ts:656:27-656:77')
  assert.equal(mutationSmokeContract.source.length, 50)

  // Real payload, not a fabricated line: the contract must address the seam in
  // the file Stryker actually mutates, or `npm run mutation:smoke` fails on
  // every run. Base drift moved this expression once already (#2931).
  const line = (await trackedSource()).split(/\r?\n/u)[mutationSmokeContract.start.line - 1]
  assert.equal(
    line?.slice(mutationSmokeContract.start.column - 1, mutationSmokeContract.end.column - 1),
    mutationSmokeContract.source,
    'move the shared range in stryker.smoke.contract.mjs with the expression',
  )
})

test('guard accepts any non-zero mutator count when the seam matches and every mutant is killed', async () => {
  assert.match(validateMutationSmokeReport(await report()), /1 mutant killed/u)
  assert.match(
    validateMutationSmokeReport(
      await report({
        mutants: Array.from({ length: 7 }, (_, index) => ({
          mutatorName: `Mutator${index}`,
          status: 'Killed',
        })),
      }),
    ),
    /7 mutants killed/u,
  )
})

test('guard rejects malformed schema, file inventory, source seam, empty mutants, and survivors', async () => {
  assertGuardFailure(await report({ schemaVersion: '2.0' }), /unsupported smoke report schema/u)
  assertGuardFailure({ schemaVersion: '1.0' }, /no valid "files" section/u)
  assertGuardFailure(
    await report({ files: { 'src/other.ts': { source: '', mutants: [] } } }),
    /no entry for src\/store\/board\/boardCrudStore\.ts/u,
  )
  assertGuardFailure(
    await report({
      files: {
        [mutationSmokeContract.file]: {
          source: sourceWithContractSeam('state.boards.value'),
          mutants: [{ mutatorName: 'x', status: 'Killed' }],
        },
      },
    }),
    /no longer selects the expected source seam/u,
  )
  // Negative control for the column convention: the same expression one column
  // to the right must be rejected. Slicing with Stryker's raw 1-based columns
  // would accept this shifted line and reject the real file (#2931).
  assertGuardFailure(
    await report({
      files: {
        [mutationSmokeContract.file]: {
          source: sourceWithContractSeam(mutationSmokeContract.source, 1),
          mutants: [{ mutatorName: 'x', status: 'Killed' }],
        },
      },
    }),
    /no longer selects the expected source seam/u,
  )
  assertGuardFailure(await report({ mutants: [] }), /produced zero mutants/u)
  assertGuardFailure(
    await report({ mutants: [{ mutatorName: 'EqualityOperator', status: 'Survived' }] }),
    /EqualityOperator=Survived/u,
  )
})

test('file-backed guard distinguishes unreadable and malformed receipts', async () => {
  const fixtureRoot = await mkdtemp(path.join(tmpdir(), 'taskdeck-mutation-smoke-'))
  try {
    const missing = path.join(fixtureRoot, 'missing.json')
    await assert.rejects(
      runMutationSmokeGuard(missing),
      (error) =>
        error instanceof SmokeGuardError &&
        /could not read the smoke report/u.test(error.message),
    )

    const malformed = path.join(fixtureRoot, 'malformed.json')
    await writeFile(malformed, '{not-json', 'utf8')
    await assert.rejects(
      runMutationSmokeGuard(malformed),
      (error) =>
        error instanceof SmokeGuardError &&
        /could not read the smoke report/u.test(error.message) &&
        /Underlying error:/u.test(error.message),
    )

    const valid = path.join(fixtureRoot, 'valid.json')
    await writeFile(valid, JSON.stringify(await report()), 'utf8')
    await assert.doesNotReject(runMutationSmokeGuard(valid))
  } finally {
    await rm(fixtureRoot, { recursive: true, force: true })
  }
})

test('manual workflow preserves advisory diagnostics only after an uncancelled smoke attempt', async () => {
  const workflow = await import('node:fs/promises').then(({ readFile }) =>
    readFile(path.join(repositoryRoot, '.github/workflows/mutation-testing.yml'), 'utf8'),
  )

  const smokeIndex = workflow.indexOf('id: activation_smoke')
  const advisoryIndex = workflow.indexOf('name: Run Stryker advisory report')
  const uploadIndex = workflow.indexOf('name: Upload Stryker report', advisoryIndex)
  const enforcementIndex = workflow.indexOf('name: Enforce mutation activation smoke verdict')

  assert.ok(smokeIndex >= 0, 'workflow must expose the smoke step outcome')
  assert.match(
    workflow.slice(smokeIndex, advisoryIndex),
    /continue-on-error:\s*true/u,
    'smoke failure must not suppress the advisory run',
  )
  const attemptedSmokeCondition =
    "${{ !cancelled() && (steps.activation_smoke.outcome == 'success' || " +
    "steps.activation_smoke.outcome == 'failure') }}"
  for (const [label, block] of [
    ['advisory run', workflow.slice(advisoryIndex, uploadIndex)],
    ['smoke verdict', workflow.slice(enforcementIndex)],
  ]) {
    assert.equal(
      block.match(/^\s*if:\s*(.+)$/mu)?.[1],
      attemptedSmokeCondition,
      `${label} must accept smoke success/failure but reject skipped setup and cancellation`,
    )
  }
  assert.match(
    workflow.slice(advisoryIndex, uploadIndex),
    /continue-on-error:\s*true/u,
    'advisory mutation score and runner failures remain non-blocking',
  )
  assert.ok(
    smokeIndex < advisoryIndex && advisoryIndex < uploadIndex && uploadIndex < enforcementIndex,
    'smoke verdict must be enforced only after advisory execution and artifact upload',
  )
  assert.match(
    workflow.slice(enforcementIndex),
    /steps\.activation_smoke\.outcome/u,
    'final verdict must bind the actual smoke outcome',
  )
  assert.match(
    workflow.slice(enforcementIndex),
    /exit 1/u,
    'a failed smoke must actually fail the job, not merely be reported',
  )
})
