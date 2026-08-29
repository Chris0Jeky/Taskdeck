/**
 * Capture draft stash (GH-2142).
 *
 * A 401 outside demo mode makes the response interceptor assign
 * `window.location.href = '/login?redirect=…'` — a full document navigation
 * that destroys both the retained in-input capture draft (PR #2023) and the
 * inline failure receipt (GH-1938). This module is the cross-navigation half:
 * the capture surface writes a bounded snapshot of the draft to
 * `sessionStorage` immediately before the redirect, and reads it back once,
 * after re-authentication, into the composer.
 *
 * Rules this module enforces, so no caller has to remember them:
 *
 * - **Never a secret.** The stored record is assembled field by field from an
 *   allowlist (text, board target, labels, due date, and the failure receipt's
 *   already-safe human message plus request diagnostics). Nothing is spread in
 *   from arbitrary input, so a token, a header, or a raw error object cannot
 *   reach storage through here.
 * - **Bounded.** Text, label list, label length and diagnostics are clamped,
 *   and a record that still serialises above `MAX_SERIALIZED_CHARS` is dropped
 *   rather than stored — a capture surface must never be the reason
 *   sessionStorage fills up.
 * - **Single use and short lived.** `takeCaptureDraft` reads and clears in one
 *   step, and anything older than `MAX_AGE_MS` is discarded on read, so a
 *   stale draft cannot resurrect itself days later.
 * - **Session scoped.** `sessionStorage`, not `localStorage`: the draft is for
 *   this tab's interrupted journey, and it dies with the tab.
 */

const STORAGE_KEY = 'taskdeck.capture.draft.v1'

/** Longest capture body kept. Beyond this the tail is dropped (`truncated`). */
export const MAX_TEXT_CHARS = 20_000
/** Most labels kept; the rest are dropped. */
export const MAX_LABELS = 50
/** Longest single label kept; longer ones are dropped, not truncated. */
export const MAX_LABEL_CHARS = 100
/** Longest failure-receipt message / diagnostics blob kept. */
export const MAX_MESSAGE_CHARS = 500
export const MAX_DETAILS_CHARS = 2_000
/** Hard ceiling on the serialised record. Above it nothing is stored. */
export const MAX_SERIALIZED_CHARS = 64_000
/** A stash older than this is discarded on read. */
export const MAX_AGE_MS = 24 * 60 * 60 * 1000

/** The capture surface a stash belongs to; restoring switches to it. */
export type CaptureDraftVariant = 'nib' | 'composer'

/** The inline failure receipt (GH-1938) carried across the redirect. */
export interface CaptureDraftFailure {
  message: string
  details: string | null
}

/** What a capture surface hands in. */
export interface CaptureDraftInput {
  variant: CaptureDraftVariant
  text: string
  boardId?: string | null
  labels?: string[]
  dueAt?: string | null
  failure?: CaptureDraftFailure | null
}

/** What comes back out, with the bounds already applied. */
export interface StashedCaptureDraft {
  variant: CaptureDraftVariant
  text: string
  boardId: string | null
  labels: string[]
  dueAt: string | null
  failure: CaptureDraftFailure | null
  /** True when the body was longer than `MAX_TEXT_CHARS` and lost its tail. */
  truncated: boolean
  /** Epoch ms the stash was written; drives the `MAX_AGE_MS` expiry. */
  stashedAt: number
}

function storage(): Storage | null {
  try {
    // Access can throw outright (Safari private mode, blocked site data) and
    // can also be simply absent in a non-browser context.
    return typeof window !== 'undefined' ? window.sessionStorage : null
  } catch {
    return null
  }
}

function clampText(value: string, max: number): string {
  return value.length > max ? value.slice(0, max) : value
}

function isVariant(value: unknown): value is CaptureDraftVariant {
  return value === 'nib' || value === 'composer'
}

/**
 * Persist a bounded snapshot of the draft for this tab.
 *
 * Returns true when a record was written. A blank body writes nothing and
 * clears any earlier stash — there is no draft left worth restoring, and a
 * stale one must not outlive it.
 */
export function stashCaptureDraft(input: CaptureDraftInput): boolean {
  const store = storage()
  if (!store) return false

  const rawText = typeof input.text === 'string' ? input.text : ''
  if (rawText.trim().length === 0) {
    clearCaptureDraft()
    return false
  }

  const labels = Array.isArray(input.labels)
    ? input.labels
        .filter((label): label is string => typeof label === 'string')
        .map((label) => label.trim())
        .filter((label) => label.length > 0 && label.length <= MAX_LABEL_CHARS)
        .slice(0, MAX_LABELS)
    : []

  const failure = input.failure
    ? {
        message: clampText(String(input.failure.message ?? ''), MAX_MESSAGE_CHARS),
        details:
          typeof input.failure.details === 'string'
            ? clampText(input.failure.details, MAX_DETAILS_CHARS)
            : null,
      }
    : null

  const record: StashedCaptureDraft = {
    variant: isVariant(input.variant) ? input.variant : 'composer',
    text: clampText(rawText, MAX_TEXT_CHARS),
    boardId: typeof input.boardId === 'string' ? input.boardId : null,
    labels,
    dueAt: typeof input.dueAt === 'string' && input.dueAt.length > 0 ? input.dueAt : null,
    failure,
    truncated: rawText.length > MAX_TEXT_CHARS,
    stashedAt: Date.now(),
  }

  let serialized: string
  try {
    serialized = JSON.stringify(record)
  } catch {
    return false
  }
  if (serialized.length > MAX_SERIALIZED_CHARS) {
    // The clamps above make this reachable only via pathological metadata.
    // Refusing beats silently storing an unbounded blob.
    clearCaptureDraft()
    return false
  }

  try {
    store.setItem(STORAGE_KEY, serialized)
    return true
  } catch {
    // Quota or a blocked store — the draft is simply not recoverable here.
    return false
  }
}

/**
 * Read the stash WITHOUT clearing it. Used by callers that cannot restore yet
 * (e.g. the Inbox opened in archived-history mode, which has no composer).
 * Expired or malformed records are cleared and reported as absent.
 */
export function peekCaptureDraft(now: number = Date.now()): StashedCaptureDraft | null {
  const store = storage()
  if (!store) return null

  let raw: string | null
  try {
    raw = store.getItem(STORAGE_KEY)
  } catch {
    return null
  }
  if (!raw) return null

  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    clearCaptureDraft()
    return null
  }

  if (typeof parsed !== 'object' || parsed === null) {
    clearCaptureDraft()
    return null
  }
  const candidate = parsed as Partial<StashedCaptureDraft>
  if (!isVariant(candidate.variant) || typeof candidate.text !== 'string') {
    clearCaptureDraft()
    return null
  }
  if (typeof candidate.stashedAt !== 'number' || !Number.isFinite(candidate.stashedAt)) {
    clearCaptureDraft()
    return null
  }
  if (now - candidate.stashedAt > MAX_AGE_MS || now < candidate.stashedAt - MAX_AGE_MS) {
    clearCaptureDraft()
    return null
  }

  const failure =
    candidate.failure && typeof candidate.failure === 'object'
      ? {
          message: typeof candidate.failure.message === 'string' ? candidate.failure.message : '',
          details: typeof candidate.failure.details === 'string' ? candidate.failure.details : null,
        }
      : null

  return {
    variant: candidate.variant,
    text: clampText(candidate.text, MAX_TEXT_CHARS),
    boardId: typeof candidate.boardId === 'string' ? candidate.boardId : null,
    labels: Array.isArray(candidate.labels)
      ? candidate.labels
          .filter((label): label is string => typeof label === 'string')
          .slice(0, MAX_LABELS)
      : [],
    dueAt: typeof candidate.dueAt === 'string' ? candidate.dueAt : null,
    failure: failure && failure.message.length > 0 ? failure : null,
    truncated: candidate.truncated === true,
    stashedAt: candidate.stashedAt,
  }
}

/**
 * Read the stash and clear it in one step — the normal restore path. Single
 * use on purpose: once the draft is back in the composer it lives there, and a
 * later reload must not resurrect a copy the user already discarded or sent.
 */
export function takeCaptureDraft(now: number = Date.now()): StashedCaptureDraft | null {
  const draft = peekCaptureDraft(now)
  clearCaptureDraft()
  return draft
}

/** Drop any stash. Safe to call when there is none. */
export function clearCaptureDraft(): void {
  const store = storage()
  if (!store) return
  try {
    store.removeItem(STORAGE_KEY)
  } catch {
    // Nothing to do — a store that cannot be written cannot be leaking either.
  }
}

/** Exported for specs that assert on the raw record. */
export const CAPTURE_DRAFT_STORAGE_KEY = STORAGE_KEY
