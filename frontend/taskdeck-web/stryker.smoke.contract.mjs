// Shared authority for the bounded frontend mutation activation probe.
//
// Keep the source range and the exact expression together. The Stryker config
// imports the range, while the receipt guard verifies that the report's source
// still contains this exact expression at that range before accepting any
// mutant result.
export const mutationSmokeContract = Object.freeze({
  schemaVersion: '1.0',
  file: 'src/store/board/boardCrudStore.ts',
  start: Object.freeze({ line: 599, column: 28 }),
  end: Object.freeze({ line: 599, column: 78 }),
  source: 'state.boards.value.filter((b) => b.id !== boardId)',
})

export const mutationSmokeRange =
  `${mutationSmokeContract.file}:` +
  `${mutationSmokeContract.start.line}:${mutationSmokeContract.start.column}-` +
  `${mutationSmokeContract.end.line}:${mutationSmokeContract.end.column}`
