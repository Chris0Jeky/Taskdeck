// Shared authority for the bounded frontend mutation activation probe.
//
// Keep the source range and the exact expression together. The Stryker config
// imports the range, while the receipt guard verifies that the report's source
// still contains this exact expression at that range before accepting any
// mutant result.
//
// Coordinates below are 1-based line, 1-based start column, exclusive end
// column, so `start.column - 1` and `end.column - 1` are the 0-based
// `String.prototype.slice` offsets the guard uses.
//
// Stryker's `mutate` range is 1-based in the LINE and 0-based in the COLUMN
// (`@stryker-mutator/core` project-reader subtracts 1 from the line only, and
// the Babel transformer compares the column against Babel's 0-based
// `node.loc.column`), and its end column is inclusive. `mutationSmokeRange`
// therefore subtracts 1 from both columns. Passing the 1-based column verbatim
// starts the range one character inside the expression, which silently drops
// the mutant for the expression's own outermost node.
export const mutationSmokeContract = Object.freeze({
  schemaVersion: '1.0',
  file: 'src/store/board/boardCrudStore.ts',
  start: Object.freeze({ line: 710, column: 28 }),
  end: Object.freeze({ line: 710, column: 78 }),
  source: 'state.boards.value.filter((b) => b.id !== boardId)',
})

export const mutationSmokeRange =
  `${mutationSmokeContract.file}:` +
  `${mutationSmokeContract.start.line}:${mutationSmokeContract.start.column - 1}-` +
  `${mutationSmokeContract.end.line}:${mutationSmokeContract.end.column - 1}`
