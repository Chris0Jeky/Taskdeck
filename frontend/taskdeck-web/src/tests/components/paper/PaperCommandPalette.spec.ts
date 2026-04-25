import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { nextTick } from 'vue'
import PaperCommandPalette from '../../../components/paper/PaperCommandPalette.vue'
import type { CommandItem } from '../../../components/shell/ShellCommandPalette.vue'

const items: CommandItem[] = [
  {
    id: 'nav:home',
    label: 'Home',
    icon: 'H',
    path: '/workspace/home',
    keywords: 'workspace home',
    kind: 'navigation',
  },
  {
    id: 'nav:boards',
    label: 'Boards',
    icon: 'B',
    path: '/workspace/boards',
    keywords: 'kanban boards',
    kind: 'navigation',
  },
  {
    id: 'action:capture',
    label: 'New Capture',
    icon: '+',
    keywords: 'capture inbox',
    kind: 'action',
    action: () => {},
  },
  {
    id: 'action:propose-split',
    label: 'Propose: split into 3 cards',
    icon: '◆',
    keywords: 'haiku ai split',
    kind: 'action',
    action: () => {},
  },
]

function backdrop(): HTMLElement | null {
  return document.body.querySelector('.paper-palette-backdrop')
}

function rows(): HTMLElement[] {
  return Array.from(document.body.querySelectorAll('.paper-palette__row')) as HTMLElement[]
}

describe('PaperCommandPalette', () => {
  let wrapper: VueWrapper | null = null

  beforeEach(() => {
    document.body.innerHTML = ''
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
  })

  it('renders nothing while visible=false', () => {
    wrapper = mount(PaperCommandPalette, {
      props: { visible: false, items },
      attachTo: document.body,
    })
    expect(backdrop()).toBeNull()
  })

  it('filters items locally based on the input query', async () => {
    wrapper = mount(PaperCommandPalette, {
      props: { visible: true, items },
      attachTo: document.body,
    })
    await nextTick()

    expect(rows().length).toBe(items.length)

    const input = backdrop()?.querySelector('input.paper-palette__input') as HTMLInputElement
    expect(input).not.toBeNull()
    input.value = 'boards'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await nextTick()

    const visibleLabels = rows().map((r) => r.textContent ?? '')
    expect(visibleLabels.some((t) => t.includes('Boards'))).toBe(true)
    expect(visibleLabels.some((t) => t.includes('Home'))).toBe(false)
  })

  it('separates AI (haiku) actions into their own section with the haiku tag', async () => {
    wrapper = mount(PaperCommandPalette, {
      props: { visible: true, items },
      attachTo: document.body,
    })
    await nextTick()

    const aiSection = backdrop()?.querySelector('section[data-section="ai"]')
    const otherSection = backdrop()?.querySelector('section[data-section="other"]')
    expect(aiSection).not.toBeNull()
    expect(otherSection).not.toBeNull()

    const aiRows = aiSection!.querySelectorAll('.paper-palette__row--ai')
    expect(aiRows.length).toBe(1)
    expect(aiRows[0].textContent).toContain('Propose: split into 3 cards')
    expect(aiRows[0].textContent).toContain('haiku')
  })

  it('navigates rows with ArrowDown / ArrowUp and emits activate on Enter', async () => {
    wrapper = mount(PaperCommandPalette, {
      props: { visible: true, items },
      attachTo: document.body,
    })
    await nextTick()

    const input = backdrop()?.querySelector('input.paper-palette__input') as HTMLInputElement
    expect(input).not.toBeNull()

    // Selection starts at index 0; Down moves to 1.
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
    await nextTick()
    let active = backdrop()?.querySelector('.paper-palette__row--active')
    expect(active?.id).toBe('paper-palette-row-1')

    // Up wraps within the combined ordered list; Down again then Enter activates.
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }))
    await nextTick()
    active = backdrop()?.querySelector('.paper-palette__row--active')
    expect(active?.id).toBe('paper-palette-row-0')

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
    await nextTick()
    const events = wrapper.emitted('activate')
    expect(events?.length).toBe(1)
    // Index 0 in ordered items is the AI row (it comes first).
    expect(events?.[0]?.[0]).toMatchObject({ id: 'action:propose-split' })
  })

  it('emits close when Escape is pressed in the input', async () => {
    wrapper = mount(PaperCommandPalette, {
      props: { visible: true, items },
      attachTo: document.body,
    })
    await nextTick()
    const input = backdrop()?.querySelector('input.paper-palette__input') as HTMLInputElement
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await nextTick()
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('emits close on backdrop click', async () => {
    wrapper = mount(PaperCommandPalette, {
      props: { visible: true, items },
      attachTo: document.body,
    })
    await nextTick()
    const bd = backdrop() as HTMLElement
    bd.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await nextTick()
    expect(wrapper.emitted('close')).toHaveLength(1)
  })
})
