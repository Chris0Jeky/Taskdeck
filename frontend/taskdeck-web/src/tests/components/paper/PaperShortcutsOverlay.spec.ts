import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import PaperShortcutsOverlay from '../../../components/paper/PaperShortcutsOverlay.vue'
import overlaySource from '../../../components/paper/PaperShortcutsOverlay.vue?raw'
import appShellSource from '../../../components/shell/AppShell.vue?raw'
import reviewKeymapSource from '../../../composables/useReviewKeymap.ts?raw'
import boardViewSource from '../../../views/BoardView.vue?raw'
import {
  APP_SHELL_SHORTCUT_BINDINGS,
  formatShortcut,
  PAPER_SHORTCUT_GROUPS,
  SHORTCUT_HANDLER_CONTRACTS,
  type ShortcutHandlerOwner,
} from '../../../utils/keyboardShortcuts'

const HANDLER_SOURCES: Record<ShortcutHandlerOwner, string> = {
  'app-shell': appShellSource,
  'review-keymap': reviewKeymapSource,
  'board-keymap': boardViewSource,
}

function teleportContent(): HTMLElement {
  return document.body
}

const SCOPED_STYLE = /<style scoped>([\s\S]*)<\/style>/

/**
 * The component's own scoped CSS, injected so `getComputedStyle` resolves the
 * rules that really apply to a rendered row. Vitest does not process SFC styles
 * (`css` is off in vitest.config.ts), so without this the cascade is empty.
 * The raw source is pre-compilation, so its selectors are plain classes with no
 * `[data-v-*]` scope attribute and match the mounted DOM as written.
 */
let injectedStyle: HTMLStyleElement | null = null

function injectOverlayStyles(): void {
  const css = SCOPED_STYLE.exec(overlaySource)?.[1] ?? ''
  expect(css.length).toBeGreaterThan(0)
  injectedStyle = document.createElement('style')
  injectedStyle.textContent = css
  document.head.appendChild(injectedStyle)
}

describe('PaperShortcutsOverlay', () => {
  let wrapper: VueWrapper | null = null

  beforeEach(() => {
    document.body.innerHTML = ''
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
    injectedStyle?.remove()
    injectedStyle = null
  })

  it('renders nothing while visible=false', () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: false }, attachTo: document.body })
    expect(teleportContent().querySelector('[data-paper-shortcuts]')).toBeNull()
  })

  it('renders all 3 groups (Navigate, Capture & Review, Boards) when open', async () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const root = teleportContent().querySelector('[data-paper-shortcuts]') as HTMLElement
    expect(root).not.toBeNull()
    const groups = root.querySelectorAll('.paper-shortcuts-overlay__group')
    expect(groups.length).toBe(3)
    const titles = Array.from(groups).map((g) => g.getAttribute('data-group'))
    expect(titles).toEqual(['Navigate', 'Capture & Review', 'Boards'])
  })

  it('documents the AppShell quick-capture shortcut', () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const root = teleportContent().querySelector('[data-paper-shortcuts]') as HTMLElement

    expect(root.textContent).toContain(formatShortcut('mod+shift+c'))
    expect(root.textContent).toContain('Quick capture')
  })

  it('renders every ledger row from the shared implemented-handler registry', () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const expectedRows = PAPER_SHORTCUT_GROUPS.flatMap((group) => group.rows)
    const displayedIds = Array.from(
      teleportContent().querySelectorAll<HTMLElement>('[data-shortcut-id]'),
    ).map((row) => row.dataset.shortcutId)

    expect(displayedIds).toEqual(expectedRows.map((row) => row.id))
    for (const row of expectedRows) {
      expect(SHORTCUT_HANDLER_CONTRACTS[row.handlerOwner].status).toBe('implemented')
      expect(HANDLER_SOURCES[row.handlerOwner]).toBeTruthy()
      if (row.handlerOwner !== 'app-shell') {
        expect(HANDLER_SOURCES[row.handlerOwner]).toContain(row.handlerEvidence)
      }
    }

    const appShellIds = new Set(APP_SHELL_SHORTCUT_BINDINGS.map((binding) => binding.id))
    expect(expectedRows.filter((row) => row.handlerOwner === 'app-shell').every(
      (row) => appShellIds.has(row.id),
    )).toBe(true)
    expect(appShellSource).toContain('APP_SHELL_SHORTCUT_BINDINGS.find')
  })

  it('documents the implemented Paper Board navigation and movement commands', () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const boards = teleportContent().querySelector('[data-group="Boards"]') as HTMLElement
    const rows = Array.from(boards.querySelectorAll('.paper-shortcuts-overlay__row'))
      .map((row) => ({
        key: row.querySelector('.paper-shortcuts-overlay__row-kbd')?.textContent?.trim(),
        label: row.querySelector('.paper-shortcuts-overlay__row-label')?.textContent?.trim(),
      }))

    expect(rows).toEqual(expect.arrayContaining([
      { key: 'J / Down', label: 'Next card' },
      { key: 'K / Up', label: 'Previous card' },
      { key: 'Left', label: 'Previous column' },
      { key: 'L / Right', label: 'Next column' },
      { key: 'Enter', label: 'Open card' },
      { key: 'Alt+Left', label: 'Move card to previous column' },
      { key: 'Alt+Right', label: 'Move card to next column' },
      { key: 'Alt+Up', label: 'Move card up in column' },
      { key: 'Alt+Down', label: 'Move card down in column' },
    ]))
    expect(rows).not.toContainEqual({ key: 'O', label: 'Open card' })
  })

  it('claims the shared new-card key but not the Legacy-only filter key', () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const displayedIds = Array.from(
      teleportContent().querySelectorAll<HTMLElement>('[data-shortcut-id]'),
    ).map((row) => row.dataset.shortcutId)

    // `n` is skin-agnostic (#1945): PaperBoardColumn renders the same
    // `[data-action="toggle-add-card"]` contract the handler drives.
    expect(displayedIds).toContain('board-new-card')
    // `f` is gated on `!paperOn` in BoardView, so Paper must not advertise it.
    expect(displayedIds).not.toContain('board-toggle-filter')
    expect(teleportContent().textContent).not.toContain('Filter panel')
  })

  it('does not advertise an undo shortcut that the product does not implement', () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const root = teleportContent().querySelector('[data-paper-shortcuts]') as HTMLElement

    expect(root.textContent).not.toContain('Undo last apply')
    expect(root.textContent).not.toContain('⌘Z')
  })

  it('does not claim a keyboard settings page exists', () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const root = teleportContent().querySelector('[data-paper-shortcuts]') as HTMLElement

    expect(root.textContent).not.toContain('Settings → Keyboard')
    expect(root.textContent).not.toContain('remappable')
  })

  it('does not handle ? because AppShell owns the toggle', async () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: false }, attachTo: document.body })
    window.dispatchEvent(new KeyboardEvent('keydown', { key: '?' }))
    expect(wrapper.emitted()).toEqual({})
  })

  it('emits close on Escape when open', async () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('does not close when ? is pressed while open', async () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    window.dispatchEvent(new KeyboardEvent('keydown', { key: '?' }))
    expect(wrapper.emitted('close')).toBeUndefined()
  })

  it('does not react to ? inside a text input', async () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: false }, attachTo: document.body })
    const input = document.createElement('input')
    document.body.appendChild(input)
    input.focus()
    input.dispatchEvent(new KeyboardEvent('keydown', { key: '?', bubbles: true }))
    expect(wrapper.emitted()).toEqual({})
  })

  it('sizes the kbd track to its content so a wide chip is not clipped', () => {
    injectOverlayStyles()
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })

    const row = teleportContent()
      .querySelector('[data-shortcut-id="quick-capture"]') as HTMLElement
    expect(row).not.toBeNull()

    const rowStyle = window.getComputedStyle(row)
    expect(rowStyle.display).toBe('grid')
    // The regression was a hard `56px` first track that `Ctrl+Shift+C` overran
    // onto the label. 56px survives only as a floor, because each row is its
    // own grid: a bare `max-content` would size the track per row and scatter
    // the label x-position down a group. Accepted consequence: a chip wider
    // than the floor grows its own row and only that row.
    expect(rowStyle.gridTemplateColumns).toBe('minmax(56px, max-content) minmax(0, 1fr) auto')
    expect(rowStyle.gridTemplateColumns).not.toMatch(/(^|\s)\d+px(\s|$)/)

    // The label may shrink below its content width and wrap, so a content-sized
    // kbd column cannot push the row past the group.
    const label = row.querySelector('.paper-shortcuts-overlay__row-label') as HTMLElement
    const labelStyle = window.getComputedStyle(label)
    // happy-dom returns the specified value, so `0` is not normalised to `0px`.
    expect(['0', '0px']).toContain(labelStyle.minWidth)
    expect(labelStyle.overflowWrap).toBe('anywhere')

    // The chip itself still carries the whole descriptor.
    const chip = row.querySelector('[data-paper-kbd]') as HTMLElement
    expect(chip.textContent?.trim()).toBe('Ctrl+Shift+C')

    // Honest limit: happy-dom resolves the cascade but performs no layout, so
    // this asserts the declared track, not a measured pixel width. Nothing in
    // the unit suite can observe the overrun itself, and nothing here can see
    // whether labels line up down a group either. Both are browser-only.
  })

  it('emits close when the close button is clicked', async () => {
    wrapper = mount(PaperShortcutsOverlay, { props: { visible: true }, attachTo: document.body })
    const closeBtn = teleportContent().querySelector('.paper-shortcuts-overlay__close') as HTMLButtonElement
    expect(closeBtn).not.toBeNull()
    closeBtn.click()
    expect(wrapper.emitted('close')).toHaveLength(1)
  })
})
