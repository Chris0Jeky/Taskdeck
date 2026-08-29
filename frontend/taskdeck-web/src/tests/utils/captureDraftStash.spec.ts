/**
 * Capture draft stash (GH-2142) — the cross-navigation half of the capture
 * draft-preservation contract. The 401 handler hard-navigates to /login, so
 * everything the user typed has to survive in sessionStorage or not at all.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import {
  CAPTURE_DRAFT_STORAGE_KEY,
  MAX_AGE_MS,
  MAX_DETAILS_CHARS,
  MAX_LABELS,
  MAX_LABEL_CHARS,
  MAX_MESSAGE_CHARS,
  MAX_TEXT_CHARS,
  clearCaptureDraft,
  peekCaptureDraft,
  stashCaptureDraft,
  takeCaptureDraft,
} from '../../utils/captureDraftStash'

describe('captureDraftStash', () => {
  beforeEach(() => {
    window.sessionStorage.clear()
  })

  afterEach(() => {
    window.sessionStorage.clear()
  })

  it('round-trips every draft field the composer owns', () => {
    expect(
      stashCaptureDraft({
        variant: 'composer',
        text: 'ship the release notes',
        boardId: 'board-7',
        labels: ['release', 'docs'],
        dueAt: '2026-09-01',
        failure: { message: 'Capture not saved.', details: 'Status: 401' },
      }),
    ).toBe(true)

    const restored = takeCaptureDraft()

    expect(restored).toEqual({
      variant: 'composer',
      text: 'ship the release notes',
      boardId: 'board-7',
      labels: ['release', 'docs'],
      dueAt: '2026-09-01',
      failure: { message: 'Capture not saved.', details: 'Status: 401' },
      truncated: false,
      stashedAt: expect.any(Number),
    })
  })

  it('is single use — a take clears the stash so a later reload cannot resurrect it', () => {
    stashCaptureDraft({ variant: 'nib', text: 'one thought' })

    expect(takeCaptureDraft()?.text).toBe('one thought')
    expect(takeCaptureDraft()).toBeNull()
    expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
  })

  it('peek leaves the stash in place for a surface that cannot restore yet', () => {
    stashCaptureDraft({ variant: 'composer', text: 'still waiting' })

    expect(peekCaptureDraft()?.text).toBe('still waiting')
    expect(peekCaptureDraft()?.text).toBe('still waiting')
  })

  it('stores nothing for a blank draft and clears any earlier one', () => {
    stashCaptureDraft({ variant: 'composer', text: 'earlier' })

    expect(stashCaptureDraft({ variant: 'composer', text: '   \n ' })).toBe(false)
    expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
  })

  it('bounds the body and flags that the tail was dropped', () => {
    stashCaptureDraft({ variant: 'composer', text: 'x'.repeat(MAX_TEXT_CHARS + 500) })

    const restored = takeCaptureDraft()
    expect(restored?.text).toHaveLength(MAX_TEXT_CHARS)
    expect(restored?.truncated).toBe(true)
  })

  it('bounds the label list, drops over-long labels, and bounds the receipt', () => {
    stashCaptureDraft({
      variant: 'composer',
      text: 'bounded',
      labels: [
        ...Array.from({ length: MAX_LABELS + 10 }, (_, i) => `label-${i}`),
        'z'.repeat(MAX_LABEL_CHARS + 1),
      ],
      failure: { message: 'm'.repeat(MAX_MESSAGE_CHARS + 50), details: 'd'.repeat(MAX_DETAILS_CHARS + 50) },
    })

    const restored = takeCaptureDraft()
    expect(restored?.labels).toHaveLength(MAX_LABELS)
    expect(restored?.labels).not.toContain('z'.repeat(MAX_LABEL_CHARS + 1))
    expect(restored?.failure?.message).toHaveLength(MAX_MESSAGE_CHARS)
    expect(restored?.failure?.details).toHaveLength(MAX_DETAILS_CHARS)
  })

  it('persists only allowlisted fields — an attached secret never reaches storage', () => {
    stashCaptureDraft({
      variant: 'composer',
      text: 'safe body',
      // A caller handing in extra fields must not be able to widen the record.
      ...({ token: 'Bearer super-secret', headers: { Authorization: 'Bearer x' } } as object),
    })

    const raw = window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY) ?? ''
    expect(raw).toContain('safe body')
    expect(raw).not.toContain('super-secret')
    expect(raw).not.toContain('Authorization')
  })

  it('discards a stash older than the maximum age', () => {
    stashCaptureDraft({ variant: 'composer', text: 'stale' })

    expect(peekCaptureDraft(Date.now() + MAX_AGE_MS + 1)).toBeNull()
    expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
  })

  it('discards a malformed or foreign record instead of restoring it', () => {
    window.sessionStorage.setItem(CAPTURE_DRAFT_STORAGE_KEY, 'not json at all')
    expect(takeCaptureDraft()).toBeNull()

    window.sessionStorage.setItem(
      CAPTURE_DRAFT_STORAGE_KEY,
      JSON.stringify({ variant: 'somewhere-else', text: 'x', stashedAt: Date.now() }),
    )
    expect(takeCaptureDraft()).toBeNull()

    window.sessionStorage.setItem(CAPTURE_DRAFT_STORAGE_KEY, JSON.stringify({ variant: 'nib', text: 'x' }))
    expect(takeCaptureDraft()).toBeNull()
  })

  it('clear is safe with nothing stashed', () => {
    expect(() => clearCaptureDraft()).not.toThrow()
    expect(peekCaptureDraft()).toBeNull()
  })
})
