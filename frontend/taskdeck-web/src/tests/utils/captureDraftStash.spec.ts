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

const USER_A = 'user-a'
const USER_B = 'user-b'

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
        userId: USER_A,
        variant: 'composer',
        text: 'ship the release notes',
        boardId: 'board-7',
        labels: ['release', 'docs'],
        dueAt: '2026-09-01',
        failure: { message: 'Capture not saved.', details: 'Status: 401' },
      }),
    ).toBe(true)

    const restored = takeCaptureDraft(USER_A)

    expect(restored).toEqual({
      userId: USER_A,
      variant: 'composer',
      text: 'ship the release notes',
      boardId: 'board-7',
      labels: ['release', 'docs'],
      dueAt: '2026-09-01',
      failure: { message: 'Capture not saved.', details: 'Status: 401' },
      truncated: false,
      labelsDropped: false,
      stashedAt: expect.any(Number),
    })
  })

  it('is single use — a take clears the stash so a later reload cannot resurrect it', () => {
    stashCaptureDraft({ userId: USER_A, variant: 'nib', text: 'one thought' })

    expect(takeCaptureDraft(USER_A)?.text).toBe('one thought')
    expect(takeCaptureDraft(USER_A)).toBeNull()
    expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
  })

  it('peek leaves the stash in place for a surface that cannot restore yet', () => {
    stashCaptureDraft({ userId: USER_A, variant: 'composer', text: 'still waiting' })

    expect(peekCaptureDraft(USER_A)?.text).toBe('still waiting')
    expect(peekCaptureDraft(USER_A)?.text).toBe('still waiting')
  })

  it('stores nothing for a blank draft and clears any earlier one', () => {
    stashCaptureDraft({ userId: USER_A, variant: 'composer', text: 'earlier' })

    expect(stashCaptureDraft({ userId: USER_A, variant: 'composer', text: '   \n ' })).toBe(false)
    expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
  })

  it('bounds the body and flags that the tail was dropped', () => {
    stashCaptureDraft({ userId: USER_A, variant: 'composer', text: 'x'.repeat(MAX_TEXT_CHARS + 500) })

    const restored = takeCaptureDraft(USER_A)
    expect(restored?.text).toHaveLength(MAX_TEXT_CHARS)
    expect(restored?.truncated).toBe(true)
  })

  it('keeps every label the server itself would accept', () => {
    // Hard-coded on purpose: this is the server's own
    // CaptureRequestContract.MaxLabelCount. Deriving the fixture from
    // MAX_LABELS would let the bound drift below the API's and still pass.
    const SERVER_MAX_LABEL_COUNT = 100
    expect(MAX_LABELS).toBe(SERVER_MAX_LABEL_COUNT)
    const labels = Array.from({ length: SERVER_MAX_LABEL_COUNT }, (_, i) => `label-${i}`)
    stashCaptureDraft({ userId: USER_A, variant: 'composer', text: 'full house', labels })

    const restored = takeCaptureDraft(USER_A)
    expect(restored?.labels).toEqual(labels)
    expect(restored?.labelsDropped).toBe(false)
  })

  it('flags a draft whose labels were dropped, by count or by length', () => {
    stashCaptureDraft({
      userId: USER_A,
      variant: 'composer',
      text: 'over the count',
      labels: Array.from({ length: MAX_LABELS + 1 }, (_, i) => `label-${i}`),
    })
    expect(takeCaptureDraft(USER_A)?.labelsDropped).toBe(true)

    stashCaptureDraft({
      userId: USER_A,
      variant: 'composer',
      text: 'over the length',
      labels: ['fine', 'z'.repeat(MAX_LABEL_CHARS + 1)],
    })
    const restored = takeCaptureDraft(USER_A)
    expect(restored?.labels).toEqual(['fine'])
    expect(restored?.labelsDropped).toBe(true)
  })

  it('bounds the label list, drops over-long labels, and bounds the receipt', () => {
    stashCaptureDraft({
      userId: USER_A,
      variant: 'composer',
      text: 'bounded',
      labels: [
        ...Array.from({ length: MAX_LABELS + 10 }, (_, i) => `label-${i}`),
        'z'.repeat(MAX_LABEL_CHARS + 1),
      ],
      failure: { message: 'm'.repeat(MAX_MESSAGE_CHARS + 50), details: 'd'.repeat(MAX_DETAILS_CHARS + 50) },
    })

    const restored = takeCaptureDraft(USER_A)
    expect(restored?.labels).toHaveLength(MAX_LABELS)
    expect(restored?.labels).not.toContain('z'.repeat(MAX_LABEL_CHARS + 1))
    expect(restored?.failure?.message).toHaveLength(MAX_MESSAGE_CHARS)
    expect(restored?.failure?.details).toHaveLength(MAX_DETAILS_CHARS)
  })

  it('persists only allowlisted fields — an attached secret never reaches storage', () => {
    stashCaptureDraft({
      userId: USER_A,
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
    stashCaptureDraft({ userId: USER_A, variant: 'composer', text: 'stale' })

    expect(peekCaptureDraft(USER_A, Date.now() + MAX_AGE_MS + 1)).toBeNull()
    expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
  })

  it('discards a malformed or foreign record instead of restoring it', () => {
    window.sessionStorage.setItem(CAPTURE_DRAFT_STORAGE_KEY, 'not json at all')
    expect(takeCaptureDraft(USER_A)).toBeNull()

    window.sessionStorage.setItem(
      CAPTURE_DRAFT_STORAGE_KEY,
      JSON.stringify({ userId: USER_A, variant: 'somewhere-else', text: 'x', stashedAt: Date.now() }),
    )
    expect(takeCaptureDraft(USER_A)).toBeNull()

    window.sessionStorage.setItem(
      CAPTURE_DRAFT_STORAGE_KEY,
      JSON.stringify({ userId: USER_A, variant: 'nib', text: 'x' }),
    )
    expect(takeCaptureDraft(USER_A)).toBeNull()
  })

  // GH-2142 review M1: sessionStorage outlives the /login round trip and
  // nothing forces the SAME account to sign back in. Without an identity gate
  // user A's draft lands in user B's composer and is posted under B's session.
  describe('account binding', () => {
    it('restores a draft to the account it was stashed for', () => {
      stashCaptureDraft({ userId: USER_A, variant: 'composer', text: 'mine' })

      expect(takeCaptureDraft(USER_A)?.text).toBe('mine')
    })

    it('never hands a draft to a different account, and destroys it on the attempt', () => {
      stashCaptureDraft({ userId: USER_A, variant: 'composer', text: 'A private thought' })

      expect(peekCaptureDraft(USER_B)).toBeNull()
      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
      // And it is gone for its owner too: dropped beats leaked.
      expect(takeCaptureDraft(USER_A)).toBeNull()
    })

    it('takeCaptureDraft applies the same gate as peek', () => {
      stashCaptureDraft({ userId: USER_A, variant: 'composer', text: 'A private thought' })

      expect(takeCaptureDraft(USER_B)).toBeNull()
      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
    })

    it('stashes nothing when no user is signed in', () => {
      expect(stashCaptureDraft({ userId: null, variant: 'composer', text: 'unowned' })).toBe(false)
      expect(stashCaptureDraft({ userId: '  ', variant: 'composer', text: 'unowned' })).toBe(false)
      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
    })

    it('an unidentified reader gets nothing and destroys nothing', () => {
      stashCaptureDraft({ userId: USER_A, variant: 'composer', text: 'still A\u2019s' })

      expect(peekCaptureDraft(null)).toBeNull()
      expect(takeCaptureDraft(undefined)).toBeNull()
      // The owner can still come back for it.
      expect(takeCaptureDraft(USER_A)?.text).toBe('still A\u2019s')
    })

    it('drops an unstamped record from an older build instead of restoring it', () => {
      window.sessionStorage.setItem(
        CAPTURE_DRAFT_STORAGE_KEY,
        JSON.stringify({ variant: 'composer', text: 'no owner', stashedAt: Date.now() }),
      )

      expect(peekCaptureDraft(USER_A)).toBeNull()
      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
    })
  })

  it('clear is safe with nothing stashed', () => {
    expect(() => clearCaptureDraft()).not.toThrow()
    expect(peekCaptureDraft(USER_A)).toBeNull()
  })
})
