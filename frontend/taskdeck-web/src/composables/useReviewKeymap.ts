import { onBeforeUnmount, onMounted } from 'vue'

/**
 * useReviewKeymap — keyboard shortcuts for the Paper deep-Review surface.
 *
 *  ⏎ / Enter           Apply
 *  ⌫ / Backspace       Reject
 *  E                   Request edit (opens composer)
 *  D                   Defer 1 hour
 *  P                   Toggle provenance pane
 *  Space               Preview diff in card detail
 *
 * Guards
 *  - When the focused element is a text input, textarea, contenteditable
 *    region, or `select`, NO shortcut fires. The user is typing — do not
 *    apply on ⏎ from within the defer-reason or edit composer.
 *  - When `enabled` returns false (e.g. modal open, view not visible) the
 *    handler is a no-op.
 *  - All handlers run with `event.preventDefault()` so the host page does
 *    not also act on the key (Space scrolling etc.).
 */
export interface ReviewKeymapHandlers {
  onApply?: () => void
  onReject?: () => void
  onRequestEdit?: () => void
  onDefer?: () => void
  onToggleProvenance?: () => void
  onPreviewDiff?: () => void
}

export interface ReviewKeymapOptions {
  enabled?: () => boolean
  /** Exposed for tests so we don't have to mount into a real document. */
  target?: () => EventTarget | null | undefined
}

const TEXT_INPUT_TYPES = new Set([
  'text',
  'search',
  'email',
  'url',
  'tel',
  'password',
  'number',
])

/**
 * Returns true when the event originated inside an editable surface where
 * the keystroke must be left to the input rather than dispatched to a
 * shortcut. `event.target` is preferred over `document.activeElement` so
 * the guard works correctly when listeners are attached to a child element.
 */
export function isEditableTarget(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false

  // Composing in an IME counts as typing.
  // (Caller passes the KeyboardEvent.target, so isComposing is checked
  //  separately on the event itself.)
  if (target instanceof HTMLTextAreaElement) return true
  if (target instanceof HTMLSelectElement) return true
  if (target instanceof HTMLInputElement) {
    return TEXT_INPUT_TYPES.has(target.type.toLowerCase())
  }
  if (target instanceof HTMLElement && target.isContentEditable) return true

  // Explicit opt-out hook: any element with `data-review-keymap="ignore"`
  // and its descendants are treated as editable. Useful for rich-text
  // composers that wrap multiple inputs.
  if (target instanceof HTMLElement && target.closest('[data-review-keymap="ignore"]')) {
    return true
  }
  return false
}

export function isInteractiveTarget(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false
  return !!target.closest(
    [
      'a[href]',
      'button',
      'summary',
      'select',
      'textarea',
      'input',
      '[role="button"]',
      '[role="link"]',
      '[role="menuitem"]',
      '[role="option"]',
      '[role="tab"]',
      '[tabindex]:not([tabindex="-1"])',
    ].join(','),
  )
}

/**
 * Attach the keymap. Returns the underlying handler so tests can invoke it
 * directly without going through `dispatchEvent`.
 */
export function useReviewKeymap(
  handlers: ReviewKeymapHandlers,
  options: ReviewKeymapOptions = {},
): { handleKeyDown: (event: KeyboardEvent) => void } {
  const enabled = options.enabled ?? (() => true)
  const target = options.target ?? (() => (typeof window !== 'undefined' ? window : null))

  function handleKeyDown(event: KeyboardEvent): void {
    if (!enabled()) return
    // Don't fire while the user is typing or composing in an IME.
    if (event.isComposing) return
    if (isEditableTarget(event.target)) return
    if (isInteractiveTarget(event.target)) return
    // Modifier keys (⌘/Ctrl/Alt) belong to other shortcut layers.
    if (event.metaKey || event.ctrlKey || event.altKey) return

    const action = matchAction(event)
    if (!action) return

    const fn = handlers[action]
    if (!fn) return

    event.preventDefault()
    event.stopPropagation()
    fn()
  }

  onMounted(() => {
    const t = target()
    if (!t) return
    t.addEventListener('keydown', handleKeyDown as EventListener)
  })

  onBeforeUnmount(() => {
    const t = target()
    if (!t) return
    t.removeEventListener('keydown', handleKeyDown as EventListener)
  })

  return { handleKeyDown }
}

function matchAction(event: KeyboardEvent): keyof ReviewKeymapHandlers | null {
  // `event.key` is the primary signal; we fall through to `event.code` for
  // Space because some browsers emit `' '` and others emit `Spacebar`.
  switch (event.key) {
    case 'Enter':
      return 'onApply'
    case 'Backspace':
      return 'onReject'
    case ' ':
    case 'Spacebar':
      return 'onPreviewDiff'
    default:
      break
  }
  if (event.code === 'Space') return 'onPreviewDiff'

  const k = event.key.toLowerCase()
  if (k === 'e') return 'onRequestEdit'
  if (k === 'd') return 'onDefer'
  if (k === 'p') return 'onToggleProvenance'
  return null
}
