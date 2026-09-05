import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import PaperShortcutsOverlay from '../../components/paper/PaperShortcutsOverlay.vue'
import ShellKeyboardHelp from '../../components/shell/ShellKeyboardHelp.vue'
import appShellSource from '../../components/shell/AppShell.vue?raw'
import boardViewSource from '../../views/BoardView.vue?raw'
import escapeStackSource from '../../composables/useEscapeStack.ts?raw'
import reviewKeymapSource from '../../composables/useReviewKeymap.ts?raw'
import {
  bindingAppliesToSkin,
  LEGACY_SHORTCUT_GROUPS,
  PAPER_SHORTCUT_BINDINGS,
  PAPER_SHORTCUT_GROUPS,
  SHORTCUT_HANDLER_CONTRACTS,
  type ShortcutBinding,
  type ShortcutHandlerOwner,
  type ShortcutSkin,
} from '../../utils/keyboardShortcuts'

/**
 * The keystroke ledger's truth guard (#2007 AC4).
 *
 * The defect this exists to stop: a help surface that names a key nothing
 * handles. The retired board dialog did it twice, advertising `h` as "Move to
 * previous column" after that binding was deleted and `?` as its own toggle
 * while AppShell shadowed the key. Both were invisible to the suite because the
 * dialog's key list lived in its own template.
 *
 * The guard closes that in two halves:
 *   1. Every row a reachable surface renders comes from the shared ledger, so a
 *      surface cannot invent a key.
 *   2. Every ledger row names a handler that exists in its owner's source, so
 *      the ledger cannot keep a key its runtime dropped.
 *
 * What it does NOT prove: that a handler present in source is reachable at
 * runtime. `?` was live in BoardView's source too until AppShell's capture-phase
 * listener started eating it. Reachability is asserted behaviourally in
 * AppShell.spec and BoardView.spec.
 */

const HANDLER_SOURCES: Record<ShortcutHandlerOwner, string> = {
  'app-shell': appShellSource,
  'review-keymap': reviewKeymapSource,
  'board-keymap': boardViewSource,
}

/**
 * The two rows ShellKeyboardHelp renders outside the grouped ledger: the help
 * toggle itself (a ledger binding with no group) and Escape (owned by the
 * shared escape stack, which is not a per-skin shortcut).
 */
const STRUCTURAL_LEGACY_ROW_IDS = ['keyboard-help', 'escape'] as const

const MODIFIER_TOKENS = new Set(['mod', 'shift', 'alt'])

/** Split a canonical descriptor into the plain keys it names. */
function descriptorKeys(descriptor: string): string[] {
  return descriptor
    .split('|')
    .flatMap((alternative) => alternative.trim().split(/\s+/))
    .flatMap((chord) => chord.split('+'))
    .map((token) => token.trim().toLowerCase())
    .filter((token) => token.length > 0 && !MODIFIER_TOKENS.has(token))
}

function bindingsNaming(key: string): ShortcutBinding[] {
  return PAPER_SHORTCUT_BINDINGS.filter((binding) => descriptorKeys(binding.descriptor).includes(key))
}

function renderedShortcutIds(): (string | undefined)[] {
  return Array.from(document.body.querySelectorAll<HTMLElement>('[data-shortcut-id]'))
    .map((row) => row.dataset.shortcutId)
}

describe('keystroke ledger truth', () => {
  let wrapper: VueWrapper | null = null

  beforeEach(() => {
    document.body.innerHTML = ''
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
  })

  it('backs every ledger row with a handler that exists in its owner source', () => {
    expect(PAPER_SHORTCUT_BINDINGS.length).toBeGreaterThan(0)

    for (const binding of PAPER_SHORTCUT_BINDINGS) {
      const contract = SHORTCUT_HANDLER_CONTRACTS[binding.handlerOwner]
      expect(contract.status).toBe('implemented')

      const source = HANDLER_SOURCES[binding.handlerOwner]
      expect(source.length).toBeGreaterThan(0)

      if (binding.handlerOwner === 'app-shell') {
        // The shell dispatches by action type, so a row whose type has no case
        // arm would be a silent no-op.
        expect(source).toContain(`case '${binding.action.type}':`)
        continue
      }

      expect(source).toContain(binding.handlerEvidence)
    }
  })

  it('renders only ledger rows on the Paper help surface', () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })

    const expectedIds = PAPER_SHORTCUT_GROUPS.flatMap((group) => group.rows.map((row) => row.id))
    expect(renderedShortcutIds()).toEqual(expectedIds)
  })

  it('renders only ledger rows plus the escape-stack row on the Legacy help surface', () => {
    wrapper = mount(ShellKeyboardHelp, { props: { visible: true }, attachTo: document.body })

    const expectedIds = LEGACY_SHORTCUT_GROUPS.flatMap((group) => group.rows.map((row) => row.id))
    expect(renderedShortcutIds()).toEqual([...expectedIds, ...STRUCTURAL_LEGACY_ROW_IDS])

    // The one row with no ledger binding still has a real handler behind it.
    expect(escapeStackSource).toContain("event.key !== 'Escape'")
  })

  it('renders a skin-scoped row on that skin surface and nowhere else', () => {
    wrapper = mount(ShellKeyboardHelp, { props: { visible: true }, attachTo: document.body })
    const legacyIds = new Set(renderedShortcutIds())

    wrapper.unmount()
    document.body.innerHTML = ''
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const paperIds = new Set(renderedShortcutIds())

    const renderedBySkin: Record<ShortcutSkin, Set<string | undefined>> = {
      legacy: legacyIds,
      paper: paperIds,
    }

    const scoped = PAPER_SHORTCUT_BINDINGS.filter((binding) => binding.skins !== undefined)
    // A scoping mechanism nothing uses would pass vacuously.
    expect(scoped.length).toBeGreaterThan(0)

    for (const binding of scoped) {
      for (const skin of ['paper', 'legacy'] as const) {
        const claimed = bindingAppliesToSkin(binding, skin)
        expect({ id: binding.id, skin, rendered: renderedBySkin[skin].has(binding.id) })
          .toEqual({ id: binding.id, skin, rendered: claimed })
      }
    }
  })

  it('gives h to the workspace Home navigation and to nothing on the board', () => {
    expect(bindingsNaming('h').map((binding) => binding.id)).toEqual(['workspace-home'])
    expect(bindingsNaming('h').map((binding) => binding.handlerOwner)).toEqual(['app-shell'])

    // AppShell consumes `h` in the capture phase, so a board binding could only
    // ever be a lie. The retired dialog advertised exactly that.
    expect(boardViewSource).not.toContain("key: 'h'")
  })

  it('gives ? to the shell help toggle and to nothing on the board', () => {
    expect(bindingsNaming('?').map((binding) => binding.id)).toEqual(['keyboard-help'])
    expect(bindingsNaming('?').map((binding) => binding.handlerOwner)).toEqual(['app-shell'])

    expect(boardViewSource).not.toContain("key: '?'")
    expect(appShellSource).toContain("case 'keyboard-help':")
  })

  it('leaves no surface repeating the retired dialog claims about h and ?', () => {
    wrapper = mount(ShellKeyboardHelp, { props: { visible: true }, attachTo: document.body })
    const legacyText = document.body.textContent ?? ''

    wrapper.unmount()
    document.body.innerHTML = ''
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const paperText = document.body.textContent ?? ''

    for (const text of [legacyText, paperText]) {
      expect(text).not.toContain('Move to previous column')
      expect(text).not.toContain('Toggle this help dialog')
    }
  })

  it('keeps the retired board help component out of production source', () => {
    const sources = import.meta.glob('../../**/*.{ts,vue}', {
      query: '?raw',
      import: 'default',
      eager: true,
    }) as Record<string, string>

    // Assembled from parts on purpose: spelling the component name here would
    // put a hit back into `grep -rn <name> src`, which is the check this guard
    // is meant to keep clean.
    const retiredComponent = ['Keyboard', 'Shortcuts', 'Help'].join('')

    // Production entries resolve two levels up (`../../components/...`); every
    // path under `src/tests/` resolves one level up and is excluded.
    const offenders = Object.entries(sources)
      .filter(([path]) => path.startsWith('../../'))
      .filter(([, source]) => source.includes(retiredComponent))
      .map(([path]) => path)

    expect(Object.keys(sources).length).toBeGreaterThan(100)
    expect(offenders).toEqual([])
  })
})
