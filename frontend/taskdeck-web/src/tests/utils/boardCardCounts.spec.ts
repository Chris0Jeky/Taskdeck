import { describe, expect, it } from 'vitest'
import { applyBoardCardCounts } from '../../utils/boardCardCounts'

function boardWithCounts(...columns: Array<[string, number]>) {
  return {
    columns: columns.map(([id, cardCount]) => ({ id, cardCount })),
  }
}

describe('applyBoardCardCounts', () => {
  it('counts cards per matching column', () => {
    const board = boardWithCounts(['todo', 0], ['done', 0])

    applyBoardCardCounts(board, [
      { columnId: 'todo' },
      { columnId: 'todo' },
      { columnId: 'done' },
    ])

    expect(board.columns).toEqual([
      { id: 'todo', cardCount: 2 },
      { id: 'done', cardCount: 1 },
    ])
  })

  it('resets columns with no matching cards to zero', () => {
    const board = boardWithCounts(['todo', 7], ['done', 3])

    applyBoardCardCounts(board, [])

    expect(board.columns).toEqual([
      { id: 'todo', cardCount: 0 },
      { id: 'done', cardCount: 0 },
    ])
  })

  it('does not add columns for cards with an unknown column id', () => {
    const board = boardWithCounts(['todo', 0])

    applyBoardCardCounts(board, [{ columnId: 'missing' }])

    expect(board.columns).toEqual([{ id: 'todo', cardCount: 0 }])
  })
})
