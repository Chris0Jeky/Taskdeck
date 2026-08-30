export type ShortcutGroupTitle = 'Navigate' | 'Capture & Review' | 'Boards'
export type ShortcutHandlerOwner = 'app-shell' | 'review-keymap' | 'board-keymap'

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
}>

const APPLE_PLATFORM = /mac|iphone|ipad|ipod/i
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

function isApplePlatform(navigatorHints: NavigatorHints | null): boolean {
  const platform = navigatorHints?.userAgentData?.platform?.trim()
  if (platform) return APPLE_PLATFORM.test(platform)

  return APPLE_PLATFORM.test(navigatorHints?.userAgent ?? '')
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
 * `userAgentData.platform`, falls back to `userAgent`, and safely defaults to
 * Ctrl notation when no browser navigator exists.
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
  {
    id: 'review-apply',
    descriptor: '\u23ce',
    label: 'Apply / commit decision',
    group: 'Capture & Review',
    handlerOwner: 'review-keymap',
    handlerEvidence: "case 'Enter':",
  },
  {
    id: 'review-reject',
    descriptor: '\u232b',
    label: 'Reject / dismiss',
    group: 'Capture & Review',
    handlerOwner: 'review-keymap',
    handlerEvidence: "case 'Backspace':",
  },
  {
    id: 'review-request-edit',
    descriptor: 'e',
    label: 'Request edit',
    group: 'Capture & Review',
    handlerOwner: 'review-keymap',
    handlerEvidence: "if (k === 'e')",
  },
  {
    id: 'review-provenance',
    descriptor: 'p',
    label: 'Provenance pane',
    note: 'during review',
    group: 'Capture & Review',
    handlerOwner: 'review-keymap',
    handlerEvidence: "if (k === 'p')",
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
    id: 'keyboard-help',
    descriptor: '?',
    label: 'Keyboard map',
    handlerOwner: 'app-shell',
    sequence: [{ key: '?' }],
    action: { type: 'keyboard-help' },
  },
]

const GROUP_ORDER: readonly ShortcutGroupTitle[] = ['Navigate', 'Capture & Review', 'Boards']

export const PAPER_SHORTCUT_GROUPS = GROUP_ORDER.map((title) => ({
  title,
  rows: PAPER_SHORTCUT_BINDINGS.filter((binding) => binding.group === title),
}))

export const APP_SHELL_SHORTCUT_BINDINGS = PAPER_SHORTCUT_BINDINGS.filter(
  (binding): binding is AppShellShortcutBinding => binding.handlerOwner === 'app-shell',
)

export const KEYBOARD_HELP_SHORTCUT = APP_SHELL_SHORTCUT_BINDINGS.find(
  (binding) => binding.action.type === 'keyboard-help',
)!
