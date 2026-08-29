import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ShellKeyboardHelp from '../../components/shell/ShellKeyboardHelp.vue'
import { PAPER_SHORTCUT_GROUPS } from '../../utils/keyboardShortcuts'

function mountHelp(visible: boolean) {
  return mount(ShellKeyboardHelp, {
    props: { visible },
    attachTo: document.body,
  })
}

function bodyText() {
  return document.body.textContent ?? ''
}

function renderedRows() {
  return Array.from(document.body.querySelectorAll('.td-shortcut-row')).map((row) => ({
    id: row.getAttribute('data-shortcut-id'),
    key: row.querySelector('kbd')?.textContent?.trim(),
    label: row.querySelector('span')?.textContent?.trim(),
  }))
}

describe('ShellKeyboardHelp', () => {
  it('renders nothing when visible is false', () => {
    const wrapper = mountHelp(false)
    expect(document.body.querySelector('.td-keyboard-help')).toBeNull()
    wrapper.unmount()
  })

  it('renders keyboard shortcuts dialog when visible is true', () => {
    const wrapper = mountHelp(true)
    expect(document.body.querySelector('.td-keyboard-help')).not.toBeNull()
    expect(bodyText()).toContain('Keyboard Shortcuts')
    wrapper.unmount()
  })

  it('displays the shared ledger groups plus the help section', () => {
    const wrapper = mountHelp(true)
    const text = bodyText()
    for (const group of PAPER_SHORTCUT_GROUPS) {
      expect(text).toContain(group.title)
    }
    expect(text).toContain('Help')
    wrapper.unmount()
  })

  it('stays in agreement with the Paper overlay by rendering every ledger row', () => {
    const wrapper = mountHelp(true)
    const ledgerIds = PAPER_SHORTCUT_GROUPS.flatMap((group) => group.rows.map((row) => row.id))
    const renderedIds = renderedRows().map((row) => row.id)

    expect(renderedIds).toEqual([...ledgerIds, 'keyboard-help', 'escape'])
    wrapper.unmount()
  })

  it('renders modifier notation through formatShortcut rather than a hardcoded literal', () => {
    const wrapper = mountHelp(true)
    const rows = renderedRows()

    // jsdom reports a non-Apple platform, so `mod` formats as Ctrl here. The
    // point of the assertion is that the value comes from the shared formatter
    // (the template holds no `Ctrl+` literal), which is what makes Apple
    // platforms render the Command glyph instead.
    expect(rows).toContainEqual({ id: 'command-palette', key: 'Ctrl+K', label: 'Command palette (anywhere)' })
    expect(rows).toContainEqual({ id: 'quick-capture', key: 'Ctrl+Shift+C', label: 'Quick capture (anywhere)' })
    expect(bodyText()).toContain('Close top surface')
    wrapper.unmount()
  })

  it('advertises Left, not H, for previous column', () => {
    const wrapper = mountHelp(true)
    const rows = renderedRows()

    expect(rows).toContainEqual({ id: 'board-previous-column', key: 'Left', label: 'Previous column' })
    expect(rows.filter((row) => row.label === 'Previous column')).toHaveLength(1)
    // `H` is the workspace Home binding now, and the map must say so.
    expect(rows).toContainEqual({ id: 'workspace-home', key: 'H', label: 'Home (workspace)' })
    wrapper.unmount()
  })

  it('drops the rows no runtime implements', () => {
    const wrapper = mountHelp(true)
    const text = bodyText()

    expect(text).not.toContain('Editor')
    expect(text).not.toContain('Save section')
    expect(text).not.toContain('Save and close')
    expect(text).not.toContain('Jump to title')
    expect(text).not.toContain('New column')
    wrapper.unmount()
  })

  it('emits close when close button is clicked', async () => {
    const wrapper = mountHelp(true)
    const closeBtn = document.body.querySelector('.td-keyboard-help__header button') as HTMLElement
    expect(closeBtn).not.toBeNull()
    closeBtn.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })

  it('has dialog role and aria-label for accessibility', () => {
    const wrapper = mountHelp(true)
    const overlay = document.body.querySelector('.td-overlay') as HTMLElement
    expect(overlay).not.toBeNull()
    expect(overlay.getAttribute('role')).toBe('dialog')
    expect(overlay.getAttribute('aria-label')).toBe('Keyboard shortcuts')
    expect(overlay.getAttribute('aria-modal')).toBe('true')
    wrapper.unmount()
  })
})
