export type BoardDensity = 'comfortable' | 'compact'

export const BOARD_DENSITY_KEY = 'td.paper.board-density.v1'
export const BOARD_COLUMN_WIDTH_KEY = 'td.paper.board-column-width.v1'
export const BOARD_COLLAPSED_COLUMNS_KEY = 'td.paper.board-collapsed-columns.v2'
export const BOARD_CARD_DETAIL_KEY = 'td.paper.board-card-detail.v1'

export function isBoardDensity(value: string | null): value is BoardDensity {
  return value === 'comfortable' || value === 'compact'
}

/*
 * Card detail is a presentation preference, not a card prop. The board keeps
 * every card node so the opener, drag handle and column count remain available
 * when excerpts and metadata are hidden (#2090 AC2).
 */
export type BoardCardDetail = 'full' | 'titles'
export const DEFAULT_BOARD_CARD_DETAIL: BoardCardDetail = 'full'

export function isBoardCardDetail(value: string | null): value is BoardCardDetail {
  return value === 'full' || value === 'titles'
}

/*
 * The stored values are English identifiers; only the label keys are localized
 * by the view. Keeping the vocabulary here gives every preference consumer the
 * same validation and fallback contract.
 */
export const BOARD_COLUMN_WIDTH_PRESETS = [
  { value: 'narrow', labelKey: 'boardDetail.actions.widthNarrow', width: '240px' },
  { value: 'standard', labelKey: 'boardDetail.actions.widthStandard', width: '280px' },
  { value: 'wide', labelKey: 'boardDetail.actions.widthWide', width: '340px' },
] as const

export type BoardColumnWidth = typeof BOARD_COLUMN_WIDTH_PRESETS[number]['value']
export const DEFAULT_BOARD_COLUMN_WIDTH: BoardColumnWidth = 'standard'

export function isBoardColumnWidth(value: string | null): value is BoardColumnWidth {
  return BOARD_COLUMN_WIDTH_PRESETS.some((preset) => preset.value === value)
}

/** Read one validated string preference without making storage availability a product failure. */
export function readStoredPreference<T extends string>(
  storage: Storage,
  key: string,
  fallback: T,
  isValid: (value: string | null) => value is T,
): T {
  try {
    const value = storage.getItem(key)
    return isValid(value) ? value : fallback
  } catch {
    return fallback
  }
}

/** Persist one preference; the in-memory state remains authoritative if storage is unavailable. */
export function writeStoredPreference(storage: Storage, key: string, value: string): void {
  try {
    storage.setItem(key, value)
  } catch {
    // Local fallback only. The active board preference remains in memory.
  }
}

// Total over the stored string: malformed, stale and future entries become an
// expanded board rather than throwing during mount.
export function parseCollapsedColumnIds(value: string | null): Set<string> {
  if (!value) return new Set()

  try {
    const parsed: unknown = JSON.parse(value)
    if (!Array.isArray(parsed)) return new Set()
    return new Set(parsed.filter((entry): entry is string => typeof entry === 'string'))
  } catch {
    return new Set()
  }
}

/**
 * Collapsed lanes are scoped to an authenticated user. The old v1 key was
 * shared by every account in a browser profile, so it is intentionally not
 * migrated into whichever account loads the board first.
 */
export function collapsedColumnsStorageKey(userId: string | null | undefined): string | null {
  const normalizedUserId = userId?.trim()
  return normalizedUserId ? `${BOARD_COLLAPSED_COLUMNS_KEY}:${normalizedUserId}` : null
}

export function readCollapsedColumnIds(
  storage: Storage,
  userId: string | null | undefined,
): Set<string> {
  const storageKey = collapsedColumnsStorageKey(userId)
  if (!storageKey) return new Set()

  try {
    return parseCollapsedColumnIds(storage.getItem(storageKey))
  } catch {
    return new Set()
  }
}

export function writeCollapsedColumnIds(
  storage: Storage,
  userId: string | null | undefined,
  columnIds: Set<string>,
): void {
  const storageKey = collapsedColumnsStorageKey(userId)
  if (!storageKey) return

  try {
    storage.setItem(storageKey, JSON.stringify([...columnIds].sort()))
  } catch {
    // Local fallback only. The collapse remains active for this mounted board.
  }
}
