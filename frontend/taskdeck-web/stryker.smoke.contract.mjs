// Shared authority for the bounded frontend mutation activation probe.
//
// Keep the source range and the exact expression together. The Stryker config
// imports the range, while the receipt guard verifies that the report's source
// still contains this exact expression at that range before accepting any
// mutant result.
//
// Coordinates are Stryker `mutate` coordinates: 1-based line, 1-based start
// column, exclusive end column. The guard converts them to 0-based string
// offsets; do not re-number them for JavaScript's `String.prototype.slice`.
export const mutationSmokeContract = Object.freeze({
  schemaVersion: '1.0',
  file: 'src/store/board/boardCrudStore.ts',
  start: Object.freeze({ line: 653, column: 28 }),
  end: Object.freeze({ line: 653, column: 78 }),
  source: 'state.boards.value.filter((b) => b.id !== boardId)',
})

export const mutationSmokeRange =
  `${mutationSmokeContract.file}:` +
  `${mutationSmokeContract.start.line}:${mutationSmokeContract.start.column}-` +
  `${mutationSmokeContract.end.line}:${mutationSmokeContract.end.column}`
