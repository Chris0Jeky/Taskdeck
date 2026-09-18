// @ts-check

/**
 * Specs that intentionally inspect repository source text for static invariants.
 *
 * Keep this list next to `mutate`: a source-text guard that reads a mutated file
 * must be registered here, otherwise Stryker gives it the instrumented sandbox
 * copy rather than repository source and the dry run can fail before any mutant
 * executes. These specs remain mandatory in ordinary Vitest and required CI;
 * only their sandbox copies are omitted; repository sources remain untouched.
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
  // Stryker's testFiles negations did not exclude these specs in the hosted
  // dry run (#3009). Omit only their literal sandbox copies instead; a leading
  // slash anchors each pattern at the frontend project root.
  ignorePatterns: sourceTextGuardTests.map((file) => `/${file}`),
  tempDirName: 'stryker-tmp',
  cleanTempDir: 'always',
  timeoutMS: 60000,
  timeoutFactor: 2.5,
}

export default config
