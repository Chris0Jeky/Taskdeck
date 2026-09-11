import { describe, expect, it, vi } from 'vitest'
import {
  BOARD_CARD_DETAIL_KEY,
  BOARD_COLLAPSED_COLUMNS_KEY,
  BOARD_COLUMN_WIDTH_KEY,
  BOARD_DENSITY_KEY,
  collapsedColumnsStorageKey,
  isBoardDensity,
  isBoardCardDetail,
  isBoardColumnWidth,
  parseCollapsedColumnIds,
  readCollapsedColumnIds,
  readStoredPreference,
  writeCollapsedColumnIds,
  writeStoredPreference,
  type BoardCardDetail,
  type BoardColumnWidth,
  type BoardDensity,
} from '../../utils/paperBoardPreferences'

function makeStorage(
  getItem: Storage['getItem'] = vi.fn(),
  setItem: Storage['setItem'] = vi.fn(),
): Storage {
  return {
    getItem,
    setItem,
    removeItem: vi.fn(),
    clear: vi.fn(),
    key: vi.fn(),
    length: 0,
  }
}

describe('paperBoardPreferences', () => {
  it('reads valid values and falls back for malformed stored preferences', () => {
    const storage = makeStorage(vi.fn((key: string) => {
      if (key === BOARD_DENSITY_KEY) return 'compact'
      if (key === BOARD_COLUMN_WIDTH_KEY) return 'stretch-to-fit'
      return 'headlines'
    }))

    expect(readStoredPreference<BoardDensity>(storage, BOARD_DENSITY_KEY, 'comfortable',
      isBoardDensity)).toBe('compact')
    expect(readStoredPreference<BoardColumnWidth>(storage, BOARD_COLUMN_WIDTH_KEY, 'standard',
      isBoardColumnWidth)).toBe('standard')
    expect(readStoredPreference<BoardCardDetail>(storage, BOARD_CARD_DETAIL_KEY, 'full',
      isBoardCardDetail)).toBe('full')
  })

  it('uses the fallback when storage throws', () => {
    const storage = makeStorage(vi.fn(() => { throw new Error('storage unavailable') }))

    expect(readStoredPreference<BoardDensity>(storage, BOARD_DENSITY_KEY, 'comfortable',
      isBoardDensity)).toBe('comfortable')
  })

  it('parses only string column IDs and fails closed for malformed JSON', () => {
    expect([...parseCollapsedColumnIds(JSON.stringify(['c2', 42, null, 'c1']))])
      .toEqual(['c2', 'c1'])
    expect(parseCollapsedColumnIds('not-json')).toEqual(new Set())
    expect(parseCollapsedColumnIds(JSON.stringify({ columnId: 'c1' }))).toEqual(new Set())
  })

  it('normalizes authenticated keys without migrating the legacy global key', () => {
    expect(collapsedColumnsStorageKey('  user-1  ')).toBe(`${BOARD_COLLAPSED_COLUMNS_KEY}:user-1`)
    expect(collapsedColumnsStorageKey('   ')).toBeNull()
    expect(collapsedColumnsStorageKey(null)).toBeNull()
  })

  it('reads and writes collapsed IDs in stable order under the user-scoped key', () => {
    const storage = makeStorage(vi.fn((key: string) => (
      key === `${BOARD_COLLAPSED_COLUMNS_KEY}:user-1`
        ? JSON.stringify(['c2', 'c1'])
        : JSON.stringify(['legacy'])
    )))

    expect([...readCollapsedColumnIds(storage, 'user-1')]).toEqual(['c2', 'c1'])
    expect([...readCollapsedColumnIds(storage, null)]).toEqual([])

    const next = new Set(['c3', 'c1'])
    writeCollapsedColumnIds(storage, 'user-1', next)
    expect(storage.setItem).toHaveBeenCalledWith(
      `${BOARD_COLLAPSED_COLUMNS_KEY}:user-1`,
      JSON.stringify(['c1', 'c3']),
    )
    writeCollapsedColumnIds(storage, null, next)
    expect(storage.setItem).toHaveBeenCalledTimes(1)
  })

  it('keeps active preferences when writing to storage throws', () => {
    const storage = makeStorage(vi.fn(), vi.fn(() => { throw new Error('quota exceeded') }))

    expect(() => writeStoredPreference(storage, BOARD_DENSITY_KEY, 'compact')).not.toThrow()
    expect(() => writeCollapsedColumnIds(storage, 'user-1', new Set(['c1']))).not.toThrow()
  })
})
