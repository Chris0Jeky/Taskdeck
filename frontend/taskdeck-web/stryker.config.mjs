// @ts-check
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
  tempDirName: 'stryker-tmp',
  cleanTempDir: 'always',
  concurrency: 4,
  timeoutMS: 60000,
  timeoutFactor: 2.5,
}

export default config
