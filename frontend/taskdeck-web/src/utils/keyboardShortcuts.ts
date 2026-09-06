export type ShortcutGroupTitle = 'Navigate' | 'Capture & Review' | 'Boards'
export type ShortcutHandlerOwner = 'app-shell' | 'review-keymap' | 'board-keymap'

/**
 * The two shipped skins. Each renders exactly one help surface:
 * `paper` -> PaperShortcutsOverlay, `legacy` -> ShellKeyboardHelp.
 */
export type ShortcutSkin = 'paper' | 'legacy'

export type ShortcutStroke = Readonly<{
  key: string
  mod?: boolean
  shift?: boolean
  alt?: boolean
}>

export type AppShellShortcutAction =
  | Readonly<{ type: 'navigate'; path: string }>
  | Readonly<{ type: 'command-palette' }>
  | Readonly<{ type: 'quick-capture' }>
  | Readonly<{ type: 'keyboard-help' }>

type ShortcutBindingBase = Readonly<{
  id: string
  descriptor: string
  label: string
  note?: string
  group?: ShortcutGroupTitle
  handlerOwner: ShortcutHandlerOwner
  /**
   * The skins whose help surface may advertise this row. Absent means every
   * skin. A row names skins when its handler is reachable in only some of
   * them, which is true in both directions today: `f` runs only when Paper is
   * off, because the Legacy filter panel is the control it toggles, and the
   * four review-keymap rows run only when Paper is on, because
   * `useReviewKeymap` is installed by `PaperReviewView` alone.
   */
  skins?: readonly ShortcutSkin[]
}>

export type AppShellShortcutBinding = ShortcutBindingBase & Readonly<{
  handlerOwner: 'app-shell'
  sequence: readonly ShortcutStroke[]
  action: AppShellShortcutAction
  allowInTextEntry?: boolean
}>

export type ContextShortcutBinding = ShortcutBindingBase & Readonly<{
  handlerOwner: 'review-keymap' | 'board-keymap'
  handlerEvidence: string
}>

export type ShortcutBinding = AppShellShortcutBinding | ContextShortcutBinding

type NavigatorHints = Readonly<{
  userAgent?: string
  userAgentData?: Readonly<{ platform?: string }>
  /**
   * Deprecated and therefore a LAST-RESORT tier only: consulted through a
   * `typeof` feature check when neither high-entropy platform data nor a
   * platform-bearing user agent string is available.
   */
  platform?: string
}>

const APPLE_PLATFORM = /mac|iphone|ipad|ipod/i
const KNOWN_NON_APPLE_USER_AGENT = /windows|android|linux|x11|cros|freebsd|openbsd|netbsd|sunos/i
const LEGACY_COMMAND_PREFIX = '\u2318'

const KEY_LABELS: Readonly<Record<string, string>> = {
  arrowdown: 'Down',
  arrowleft: 'Left',
  arrowright: 'Right',
  arrowup: 'Up',
  backspace: 'Backspace',
  enter: 'Enter',
}

function runtimeNavigator(): NavigatorHints | null {
  return typeof navigator === 'undefined' ? null : navigator as NavigatorHints
}

/**
 * Read the deprecated `navigator.platform` only behind a feature check, so an
 * SSR or jsdom runtime that omits it cannot throw. Returning `null` means the
 * tier had nothing to say, which is distinct from "said it is not Apple".
 */
function legacyPlatformIsApple(navigatorHints: NavigatorHints | null): boolean | null {
  if (!navigatorHints) return null
  if (typeof navigatorHints.platform !== 'string') return null

  const legacyPlatform = navigatorHints.platform.trim()
  if (!legacyPlatform) return null

  return APPLE_PLATFORM.test(legacyPlatform)
}

/**
 * Detection runs in three tiers, deprecated data strictly last:
 *
 * 1. `navigator.userAgentData.platform` — the supported high-entropy hint.
 * 2. `navigator.userAgent` — trusted when it names ANY platform we recognise,
 *    including a reduced Windows/Android UA, which must stay on Ctrl notation.
 * 3. `navigator.platform` — a feature-detected last resort reached only when
 *    the first two tiers are absent or platform-generic. This is what keeps an
 *    older browser exposing just `MacIntel` from showing Ctrl on a Mac; it is
 *    never the primary mechanism.
 */
function isApplePlatform(navigatorHints: NavigatorHints | null): boolean {
  const platform = navigatorHints?.userAgentData?.platform?.trim()
  if (platform) return APPLE_PLATFORM.test(platform)

  const userAgent = navigatorHints?.userAgent ?? ''
  if (APPLE_PLATFORM.test(userAgent)) return true
  if (KNOWN_NON_APPLE_USER_AGENT.test(userAgent)) return false

  return legacyPlatformIsApple(navigatorHints) ?? false
}

function keyLabel(token: string): string {
  const normalized = token.toLowerCase()
  return KEY_LABELS[normalized] ?? (token.length === 1 ? token.toUpperCase() : token)
}

function normalizeDescriptor(descriptor: string): string {
  if (!descriptor.startsWith(LEGACY_COMMAND_PREFIX)) return descriptor

  const key = descriptor.slice(LEGACY_COMMAND_PREFIX.length)
    .replace('\u21e7', 'shift+')
    .replace('\u23ce', 'enter')
  return `mod+${key}`
}

function formatChord(chord: string, apple: boolean): string {
  const tokens = chord.split('+').map((token) => token.trim()).filter(Boolean)
  const usesPlatformModifier = tokens.some((token) => token.toLowerCase() === 'mod')

  if (apple && usesPlatformModifier) {
    return tokens.map((token) => {
      switch (token.toLowerCase()) {
        case 'mod': return LEGACY_COMMAND_PREFIX
        case 'shift': return '\u21e7'
        case 'enter': return '\u23ce'
        default: return keyLabel(token)
      }
    }).join('')
  }

  return tokens.map((token) => {
    switch (token.toLowerCase()) {
      case 'mod': return 'Ctrl'
      case 'shift': return 'Shift'
      case 'alt': return 'Alt'
      default: return keyLabel(token)
    }
  }).join('+')
}

/**
 * Format one canonical shortcut descriptor for the current keyboard platform.
 *
 * Descriptors use `mod` for Command-or-Control, `+` inside a chord, ` ` between
 * chord steps, and `|` between alternative keys. Platform detection prefers
 * `userAgentData.platform`, then a platform-bearing `userAgent`, then the
 * feature-detected legacy `navigator.platform`, and safely defaults to Ctrl
 * notation when no browser navigator exists.
 */
export function formatShortcut(
  descriptor: string,
  navigatorHints: NavigatorHints | null = runtimeNavigator(),
): string {
  const apple = isApplePlatform(navigatorHints)
  return normalizeDescriptor(descriptor)
    .split('|')
    .map((alternative) => alternative
      .trim()
      .split(/\s+/)
      .map((chord) => formatChord(chord, apple))
      .join(' '))
    .join(' / ')
}

/**
 * A stroke key the keyboard produces the same way whichever modifiers reach it.
 * Letters are the case that needs the guard below: `Shift+H` arrives as `H`,
 * which a case-insensitive comparison cannot tell apart from `h`.
 */
const LETTER_KEY = /^[a-z]$/i

/**
 * A printable character the LAYOUT produces, `?` being the one in the ledger.
 * Which modifiers were held to reach it is a property of the layout, not of the
 * shortcut: on a layout where `?` needs AltGr the browser reports `altKey`, and
 * on Windows `ctrlKey` too, because AltGr is Ctrl+Alt there.
 */
function isLayoutProducedCharacter(key: string): boolean {
  return key.length === 1 && !/[a-z0-9]/i.test(key)
}

/**
 * Match one keydown against one canonical stroke.
 *
 * Shift: a stroke that declares `shift` is exact. A stroke that does not
 * declare it requires Shift to be UP over a letter, so `Shift+H` no longer
 * navigates Home (#1968); over a layout-produced character it stays permissive,
 * because Shift is usually how you type the character at all.
 *
 * Alt and mod: exact, except over a layout-produced character that declares
 * neither. There Alt is ignored, and Ctrl is ignored while Alt is also down --
 * the AltGr signature -- so the `?` help key stays reachable on those layouts
 * without loosening any ordinary Ctrl or Alt combination.
 */
export function strokeMatches(event: KeyboardEvent, stroke: ShortcutStroke): boolean {
  if (event.key.toLowerCase() !== stroke.key.toLowerCase()) return false

  const layoutCharacter = !stroke.mod && !stroke.alt && isLayoutProducedCharacter(stroke.key)

  if (stroke.shift !== undefined) {
    if (event.shiftKey !== stroke.shift) return false
  } else if (LETTER_KEY.test(stroke.key) && event.shiftKey) {
    return false
  }

  if (!layoutCharacter) {
    return (event.ctrlKey || event.metaKey) === Boolean(stroke.mod) &&
      event.altKey === Boolean(stroke.alt)
  }

  const altGrPressed = event.ctrlKey && event.altKey
  return altGrPressed || !(event.ctrlKey || event.metaKey)
}

/**
 * The handler owner is part of every displayed row. Adding an overlay entry
 * therefore requires naming the concrete runtime that owns it rather than
 * documenting an aspirational key.
 */
export const SHORTCUT_HANDLER_CONTRACTS = {
  'app-shell': {
    status: 'implemented',
    source: 'components/shell/AppShell.vue',
  },
  'review-keymap': {
    status: 'implemented',
    source: 'composables/useReviewKeymap.ts',
  },
  'board-keymap': {
    status: 'implemented',
    source: 'views/BoardView.vue',
  },
} as const satisfies Record<ShortcutHandlerOwner, {
  status: 'implemented'
  source: string
}>

export const PAPER_SHORTCUT_BINDINGS: readonly ShortcutBinding[] = [
  {
    id: 'workspace-home',
    descriptor: 'h',
    label: 'Home',
    note: 'workspace',
    group: 'Navigate',
    handlerOwner: 'app-shell',
    sequence: [{ key: 'h' }],
    action: { type: 'navigate', path: '/workspace/home' },
  },
  {
    id: 'workspace-today',
    descriptor: 't',
    label: 'Today',
    group: 'Navigate',
    handlerOwner: 'app-shell',
    sequence: [{ key: 't' }],
    action: { type: 'navigate', path: '/workspace/today' },
  },
  {
    id: 'workspace-boards',
    descriptor: 'b',
    label: 'Boards',
    group: 'Navigate',
    handlerOwner: 'app-shell',
    sequence: [{ key: 'b' }],
    action: { type: 'navigate', path: '/workspace/boards' },
  },
  {
    id: 'workspace-inbox',
    descriptor: 'i',
    label: 'Inbox',
    group: 'Navigate',
    handlerOwner: 'app-shell',
    sequence: [{ key: 'i' }],
    action: { type: 'navigate', path: '/workspace/inbox' },
  },
  {
    id: 'workspace-review',
    descriptor: 'r',
    label: 'Review',
    group: 'Navigate',
    handlerOwner: 'app-shell',
    sequence: [{ key: 'r' }],
    action: { type: 'navigate', path: '/workspace/review' },
  },
  {
    id: 'command-palette',
    descriptor: 'mod+k',
    label: 'Command palette',
    note: 'anywhere',
    group: 'Navigate',
    handlerOwner: 'app-shell',
    sequence: [{ key: 'k', mod: true }],
    action: { type: 'command-palette' },
    allowInTextEntry: true,
  },
  {
    id: 'workspace-today-chord',
    descriptor: 'g t',
    label: 'Go to Today',
    group: 'Navigate',
    handlerOwner: 'app-shell',
    sequence: [{ key: 'g' }, { key: 't' }],
    action: { type: 'navigate', path: '/workspace/today' },
  },
  {
    id: 'quick-capture',
    descriptor: 'mod+shift+c',
    label: 'Quick capture',
    note: 'anywhere',
    group: 'Capture & Review',
    handlerOwner: 'app-shell',
    sequence: [{ key: 'c', mod: true, shift: true }],
    action: { type: 'quick-capture' },
  },
  // The review keymap is installed by `PaperReviewView.vue` alone.
  // `LegacyReviewView.vue` has one element-scoped `@keydown` handling only
  // ArrowDown/ArrowUp, so the Legacy `?` map used to advertise four keys that
  // no Legacy runtime implements (#2007 AC1, and the 2026-08-29 MEDIUM on
  // #1968). All four are scoped to Paper.
  {
    id: 'review-apply',
    descriptor: '\u23ce',
    label: 'Apply / commit decision',
    group: 'Capture & Review',
    handlerOwner: 'review-keymap',
    handlerEvidence: "case 'Enter':",
    skins: ['paper'],
  },
  {
    id: 'review-reject',
    descriptor: '\u232b',
    label: 'Reject / dismiss',
    group: 'Capture & Review',
    handlerOwner: 'review-keymap',
    handlerEvidence: "case 'Backspace':",
    skins: ['paper'],
  },
  {
    id: 'review-request-edit',
    descriptor: 'e',
    label: 'Request edit',
    group: 'Capture & Review',
    handlerOwner: 'review-keymap',
    handlerEvidence: "if (k === 'e')",
    skins: ['paper'],
  },
  {
    id: 'review-provenance',
    descriptor: 'p',
    label: 'Provenance pane',
    note: 'during review',
    group: 'Capture & Review',
    handlerOwner: 'review-keymap',
    handlerEvidence: "if (k === 'p')",
    skins: ['paper'],
  },
  {
    id: 'board-next-card',
    descriptor: 'j|arrowdown',
    label: 'Next card',
    group: 'Boards',
    handlerOwner: 'board-keymap',
    handlerEvidence: "{ key: 'j', description: 'Next card'",
  },
  {
    id: 'board-previous-card',
    descriptor: 'k|arrowup',
    label: 'Previous card',
    group: 'Boards',
    handlerOwner: 'board-keymap',
    handlerEvidence: "{ key: 'k', description: 'Previous card'",
  },
  {
    id: 'board-previous-column',
    descriptor: 'arrowleft',
    label: 'Previous column',
    group: 'Boards',
    handlerOwner: 'board-keymap',
    handlerEvidence: "{ key: 'ArrowLeft', alt: false, description: 'Previous column'",
  },
  {
    id: 'board-next-column',
    descriptor: 'l|arrowright',
    label: 'Next column',
    group: 'Boards',
    handlerOwner: 'board-keymap',
    handlerEvidence: "{ key: 'l', description: 'Next column'",
  },
  {
    id: 'board-open-card',
    descriptor: 'enter',
    label: 'Open card',
    group: 'Boards',
    handlerOwner: 'board-keymap',
    handlerEvidence: "{ key: 'Enter', description: 'Open selected card'",
  },
  {
    id: 'board-new-card',
    descriptor: 'n',
    label: 'New card in column',
    group: 'Boards',
    handlerOwner: 'board-keymap',
    handlerEvidence: "{ key: 'n', description: 'New card in current column'",
  },
  {
    id: 'board-move-previous-column',
    descriptor: 'alt+arrowleft',
    label: 'Move card to previous column',
    group: 'Boards',
    handlerOwner: 'board-keymap',
    handlerEvidence: "{ key: 'ArrowLeft', alt: true, description: 'Move card to previous column'",
  },
  {
    id: 'board-move-next-column',
    descriptor: 'alt+arrowright',
    label: 'Move card to next column',
    group: 'Boards',
    handlerOwner: 'board-keymap',
    handlerEvidence: "{ key: 'ArrowRight', alt: true, description: 'Move card to next column'",
  },
  {
    id: 'board-move-up',
    descriptor: 'alt+arrowup',
    label: 'Move card up in column',
    group: 'Boards',
    handlerOwner: 'board-keymap',
    handlerEvidence: "{ key: 'ArrowUp', alt: true, description: 'Move card up in column'",
  },
  {
    id: 'board-move-down',
    descriptor: 'alt+arrowdown',
    label: 'Move card down in column',
    group: 'Boards',
    handlerOwner: 'board-keymap',
    handlerEvidence: "{ key: 'ArrowDown', alt: true, description: 'Move card down in column'",
  },
  {
    id: 'board-toggle-filter',
    descriptor: 'f',
    label: 'Filter panel',
    group: 'Boards',
    handlerOwner: 'board-keymap',
    handlerEvidence: "{ key: 'f', description: 'Toggle filter panel'",
    // `standardBoardOnlyShortcutsEnabled` in BoardView gates this on `!paperOn`:
    // the filter panel it toggles exists only on the Legacy board, so the Paper
    // overlay must not claim the key.
    skins: ['legacy'],
  },
  {
    id: 'keyboard-help',
    descriptor: '?',
    label: 'Keyboard map',
    handlerOwner: 'app-shell',
    sequence: [{ key: '?' }],
    action: { type: 'keyboard-help' },
  },
]

const GROUP_ORDER: readonly ShortcutGroupTitle[] = ['Navigate', 'Capture & Review', 'Boards']

const ALL_SKINS: readonly ShortcutSkin[] = ['paper', 'legacy']

/**
 * A binding with no `skins` list applies everywhere; one that names skins is
 * advertised only where its handler can actually run.
 */
export function bindingAppliesToSkin(binding: ShortcutBinding, skin: ShortcutSkin): boolean {
  return (binding.skins ?? ALL_SKINS).includes(skin)
}

export function shortcutGroupsForSkin(skin: ShortcutSkin) {
  return GROUP_ORDER.map((title) => ({
    title,
    rows: PAPER_SHORTCUT_BINDINGS.filter(
      (binding) => binding.group === title && bindingAppliesToSkin(binding, skin),
    ),
  }))
}

export const PAPER_SHORTCUT_GROUPS = shortcutGroupsForSkin('paper')

export const LEGACY_SHORTCUT_GROUPS = shortcutGroupsForSkin('legacy')

export const APP_SHELL_SHORTCUT_BINDINGS = PAPER_SHORTCUT_BINDINGS.filter(
  (binding): binding is AppShellShortcutBinding => binding.handlerOwner === 'app-shell',
)

export const KEYBOARD_HELP_SHORTCUT = APP_SHELL_SHORTCUT_BINDINGS.find(
  (binding) => binding.action.type === 'keyboard-help',
)!
