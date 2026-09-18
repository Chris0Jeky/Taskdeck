// @ts-check

/**
 * Specs that intentionally inspect repository source text for static invariants.
 *
 * Keep this list next to `mutate`: a source-text guard that reads a mutated file
 * must be registered here, otherwise Stryker gives it the instrumented sandbox
 * copy rather than repository source and the dry run can fail before any mutant
 * executes. These specs remain mandatory in ordinary Vitest and required CI;
 * the exclusion applies only to Stryker's test selection.
 */
export const sourceTextGuardTests = [
  'src/tests/views/paper/boardMutationCapabilityParity.spec.ts',
]

/** @type {import('@stryker-mutator/api/core').PartialStrykerOptions} */
const config = {
  packageManager: 'npm',
  reporters: ['html', 'json', 'progress', 'clear-text'],
  testRunner: 'vitest',
  vitest: {
    configFile: 'vitest.config.ts',
  },
  coverageAnalysis: 'perTest',
  thresholds: {
    high: 80,
    low: 60,
    break: 0,
  },
  mutate: [
    'src/store/captureStore.ts',
    'src/store/boardStore.ts',
    'src/store/board/*.ts',
  ],
  testFiles: [
    '**/*.{spec,test}.{js,mjs,cjs,ts,mts,cts,jsx,tsx}',
    ...sourceTextGuardTests.map((file) => `!${file}`),
  ],
  tempDirName: 'stryker-tmp',
  cleanTempDir: 'always',
  timeoutMS: 60000,
  timeoutFactor: 2.5,
}

export default config
