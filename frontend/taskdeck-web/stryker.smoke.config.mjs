import baseConfig from './stryker.config.mjs'
import { mutationSmokeRange } from './stryker.smoke.contract.mjs'

/** @type {import('@stryker-mutator/api/core').PartialStrykerOptions} */
const config = {
  ...baseConfig,
  // Keep this probe cheap enough for local use and early CI failure. The
  // shared contract binds these coordinates to the exact board-list deletion
  // expression, and the receipt guard verifies the report's source text before
  // accepting any non-empty all-Killed mutant set.
  mutate: [mutationSmokeRange],
  // The smoke deliberately drives Vitest through Stryker's command runner
  // rather than `@stryker-mutator/vitest-runner`. With Stryker 10 and the
  // repository's Vitest 5 line the vitest-runner reports "0.00 tests per
  // mutant" and every mutant survives; shelling out to the ordinary Vitest
  // CLI keeps the probe independent of that runner/Vitest pairing.
  testRunner: 'command',
  commandRunner: {
    command: 'npx vitest --run --maxWorkers=2 src/tests/store/board/boardCrudStore.spec.ts',
  },
  // The command runner reports a single synthetic test, so per-test coverage
  // analysis has nothing to key on.
  coverageAnalysis: 'off',
  reporters: ['clear-text', 'json'],
  jsonReporter: {
    fileName: 'reports/mutation/smoke.json',
  },
  concurrency: 1,
  tempDirName: 'stryker-smoke-tmp',
  cleanTempDir: 'always',
  thresholds: {
    high: 100,
    low: 100,
    break: 100,
  },
}

export default config
