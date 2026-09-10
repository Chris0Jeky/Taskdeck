import baseConfig from './stryker.config.mjs'

/** @type {import('@stryker-mutator/api/core').PartialStrykerOptions} */
const config = {
  ...baseConfig,
  // Keep this probe cheap enough for local use and early CI failure. The
  // selected line is the board-list deletion branch covered by the focused
  // board CRUD suite; update the coordinates with that seam if it moves.
  mutate: ['src/store/board/boardCrudStore.ts:587:28-587:78'],
  testFiles: ['src/tests/store/board/boardCrudStore.spec.ts'],
  reporters: ['clear-text'],
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
