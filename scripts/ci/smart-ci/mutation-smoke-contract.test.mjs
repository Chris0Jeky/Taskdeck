import assert from 'node:assert/strict'
import { mkdtemp, rm, writeFile } from 'node:fs/promises'
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

function sourceWithContractSeam(source = mutationSmokeContract.source) {
  const lines = Array.from({ length: mutationSmokeContract.start.line }, () => '')
  lines[mutationSmokeContract.start.line - 1] =
    ' '.repeat(mutationSmokeContract.start.column) + source
  return lines.join('\n')
}

function report({
  mutants = [{ mutatorName: 'EqualityOperator', status: 'Killed' }],
  ...overrides
} = {}) {
  return {
    schemaVersion: mutationSmokeContract.schemaVersion,
    files: {
      [mutationSmokeContract.file]: {
        source: sourceWithContractSeam(),
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

test('shared mutation range selects the exact contracted source seam', () => {
  assert.equal(
    mutationSmokeRange,
    'src/store/board/boardCrudStore.ts:599:28-599:78',
  )
  assert.equal(mutationSmokeContract.source.length, 50)
})

test('guard accepts any non-zero mutator count when the seam matches and every mutant is killed', () => {
  assert.match(validateMutationSmokeReport(report()), /1 mutant killed/u)
  assert.match(
    validateMutationSmokeReport(
      report({
        mutants: Array.from({ length: 7 }, (_, index) => ({
          mutatorName: `Mutator${index}`,
          status: 'Killed',
        })),
      }),
    ),
    /7 mutants killed/u,
  )
})

test('guard rejects malformed schema, file inventory, source seam, empty mutants, and survivors', () => {
  assertGuardFailure(report({ schemaVersion: '2.0' }), /unsupported smoke report schema/u)
  assertGuardFailure({ schemaVersion: '1.0' }, /no valid "files" section/u)
  assertGuardFailure(
    report({ files: { 'src/other.ts': { source: '', mutants: [] } } }),
    /no entry for src\/store\/board\/boardCrudStore\.ts/u,
  )
  assertGuardFailure(
    report({
      files: {
        [mutationSmokeContract.file]: {
          source: sourceWithContractSeam('state.boards.value'),
          mutants: [{ mutatorName: 'x', status: 'Killed' }],
        },
      },
    }),
    /no longer selects the expected source seam/u,
  )
  assertGuardFailure(report({ mutants: [] }), /produced zero mutants/u)
  assertGuardFailure(
    report({ mutants: [{ mutatorName: 'EqualityOperator', status: 'Survived' }] }),
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
    await writeFile(valid, JSON.stringify(report()), 'utf8')
    await assert.doesNotReject(runMutationSmokeGuard(valid))
  } finally {
    await rm(fixtureRoot, { recursive: true, force: true })
  }
})

test('manual workflow always attempts and uploads the advisory report before enforcing smoke', async () => {
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
  assert.match(
    workflow.slice(advisoryIndex, uploadIndex),
    /if:\s*always\(\)/u,
    'advisory run must execute after smoke failure',
  )
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
})
