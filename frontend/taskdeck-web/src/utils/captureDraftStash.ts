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
 * - **Bound to one account.** `sessionStorage` outlives the /login round trip
 *   and nothing forces the SAME account to sign back in, so an unstamped
 *   record would restore user A's draft into user B's composer and send it
 *   under B's session. Every record carries the `userId` it was stashed for;
 *   a read by anyone else drops it. No signed-in user, no stash.
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
/**
 * Most labels kept. This is the server's own `CaptureRequestContract
 * .MaxLabelCount`, so a draft the API would accept is never trimmed here; a
 * draft beyond it loses its tail, and `labelsDropped` says so.
 */
export const MAX_LABELS = 100
/**
 * Longest single label kept; longer ones are dropped whole rather than
 * truncated (a silently shortened label is a different label). Deliberately
 * looser than the server's `MaxLabelNameLength` of 30 — the stash preserves
 * what was typed and lets the server be the authority on what is valid.
 */
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
  /** The signed-in user this draft belongs to. Without it nothing is stored. */
  userId: string | null | undefined
  variant: CaptureDraftVariant
  text: string
  boardId?: string | null
  labels?: string[]
  dueAt?: string | null
  failure?: CaptureDraftFailure | null
}

/** What comes back out, with the bounds already applied. */
export interface StashedCaptureDraft {
  /** The account the draft was stashed for; a read by any other drops it. */
  userId: string
  variant: CaptureDraftVariant
  text: string
  boardId: string | null
  labels: string[]
  dueAt: string | null
  failure: CaptureDraftFailure | null
  /** True when the body was longer than `MAX_TEXT_CHARS` and lost its tail. */
  truncated: boolean
  /** True when any label was dropped, by count or by length. */
  labelsDropped: boolean
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
 * stale one must not outlive it. An unidentified session writes nothing and
 * leaves any earlier record alone: with no account to stamp, a stored draft
 * could be restored by whoever signs in next.
 */
export function stashCaptureDraft(input: CaptureDraftInput): boolean {
  const store = storage()
  if (!store) return false

  const userId = typeof input.userId === 'string' ? input.userId.trim() : ''
  if (userId.length === 0) return false

  const rawText = typeof input.text === 'string' ? input.text : ''
  if (rawText.trim().length === 0) {
    clearCaptureDraft()
    return false
  }

  const requestedLabels = Array.isArray(input.labels)
    ? input.labels
        .filter((label): label is string => typeof label === 'string')
        .map((label) => label.trim())
        .filter((label) => label.length > 0)
    : []
  const keepableLabels = requestedLabels.filter((label) => label.length <= MAX_LABEL_CHARS)
  const labels = keepableLabels.slice(0, MAX_LABELS)
  // Losing part of a draft is allowed; losing it silently is not. The restore
  // affordance reads this flag and tells the user something was left behind.
  const labelsDropped = labels.length < requestedLabels.length

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
    userId,
    variant: isVariant(input.variant) ? input.variant : 'composer',
    text: clampText(rawText, MAX_TEXT_CHARS),
    boardId: typeof input.boardId === 'string' ? input.boardId : null,
    labels,
    dueAt: typeof input.dueAt === 'string' && input.dueAt.length > 0 ? input.dueAt : null,
    failure,
    truncated: rawText.length > MAX_TEXT_CHARS,
    labelsDropped,
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
 * Read the stash WITHOUT clearing it, for `currentUserId`. Used by callers
 * that cannot restore yet (e.g. the Inbox opened in archived-history mode,
 * which has no composer). Expired, malformed, and other-account records are
 * cleared and reported as absent.
 *
 * A caller with no signed-in user gets nothing AND leaves the record in place:
 * it is not this session's to read, and not this session's to destroy either.
 */
export function peekCaptureDraft(
  currentUserId: string | null | undefined,
  now: number = Date.now(),
): StashedCaptureDraft | null {
  const store = storage()
  if (!store) return null

  const reader = typeof currentUserId === 'string' ? currentUserId.trim() : ''
  if (reader.length === 0) return null

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
  // The identity gate. An unstamped record predates this rule (or was not
  // written by us) and is as untrustworthy as one belonging to someone else:
  // both are dropped rather than handed to the account reading now.
  if (typeof candidate.userId !== 'string' || candidate.userId !== reader) {
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
    userId: candidate.userId,
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
    labelsDropped: candidate.labelsDropped === true,
    stashedAt: candidate.stashedAt,
  }
}

/**
 * Read the stash and clear it in one step — the normal restore path. Single
 * use on purpose: once the draft is back in the composer it lives there, and a
 * later reload must not resurrect a copy the user already discarded or sent.
 */
export function takeCaptureDraft(
  currentUserId: string | null | undefined,
  now: number = Date.now(),
): StashedCaptureDraft | null {
  // No signed-in reader: nothing to restore, and nothing of ours to destroy.
  const reader = typeof currentUserId === 'string' ? currentUserId.trim() : ''
  if (reader.length === 0) return null
  const draft = peekCaptureDraft(reader, now)
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
