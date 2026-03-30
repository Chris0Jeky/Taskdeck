import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, VueWrapper } from '@vue/test-utils'
import ShellCommandPalette from '../../components/shell/ShellCommandPalette.vue'
import type { CommandItem } from '../../components/shell/ShellCommandPalette.vue'

// Mock the useGlobalSearch composable so we control search state
const mockBoards = { value: [] as Array<{ id: string; name: string; description: string | null; isArchived: boolean }> }
const mockCards = { value: [] as Array<{ id: string; boardId: string; boardName: string; columnId: string; columnName: string; title: string; description: string }> }
const mockSearchLoading = { value: false }
const mockSearchQuery = { value: '' }
const mockReset = vi.fn()

vi.mock('../../composables/useGlobalSearch', () => ({
  useGlobalSearch: () => ({
    query: mockSearchQuery,
    boards: mockBoards,
    cards: mockCards,
    loading: mockSearchLoading,
    error: { value: null },
    reset: mockReset,
    executeSearch: vi.fn(),
  }),
}))

const baseItems: CommandItem[] = [
  { id: 'nav:home', label: 'Home', icon: 'H', path: '/workspace/home', kind: 'navigation' },
  { id: 'nav:boards', label: 'Boards', icon: 'B', path: '/workspace/boards', kind: 'navigation' },
  { id: 'action:capture', label: 'New Capture', icon: '+', keywords: 'capture', kind: 'action' },
]

describe('ShellCommandPalette', () => {
  let wrapper: VueWrapper

  beforeEach(() => {
    mockBoards.value = []
    mockCards.value = []
    mockSearchLoading.value = false
    mockSearchQuery.value = ''
    mockReset.mockClear()
  })

  afterEach(() => {
    wrapper?.unmount()
    // Clean up any teleported content left on document.body
    document.body.innerHTML = ''
  })

  function mountPalette(props?: Partial<{ visible: boolean; items: CommandItem[] }>) {
    wrapper = mount(ShellCommandPalette, {
      props: {
        visible: true,
        items: baseItems,
        ...props,
      },
      attachTo: document.body,
    })
    return wrapper
  }

  // Helper: query the teleported dialog from document.body
  function findDialog() {
    return document.body.querySelector('[role="dialog"]')
  }

  function findAllOptions() {
    return Array.from(document.body.querySelectorAll('[role="option"]'))
  }

  function findInput() {
    return document.body.querySelector('input') as HTMLInputElement | null
  }

  it('renders all command items when visible and query is empty', () => {
    mountPalette()
    const options = findAllOptions()
    expect(options.length).toBe(3)
    expect(options[0].textContent).toContain('Go to Home')
    expect(options[1].textContent).toContain('Go to Boards')
    expect(options[2].textContent).toContain('New Capture')
  })

  it('does not render when visible is false', () => {
    mountPalette({ visible: false })
    expect(findDialog()).toBeNull()
  })

  it('filters commands locally by label', async () => {
    mountPalette()
    const input = findInput()!
    expect(input).not.toBeNull()
    // Simulate typing in the input
    input.value = 'home'
    input.dispatchEvent(new Event('input'))
    await wrapper.vm.$nextTick()
    const options = findAllOptions()
    expect(options.length).toBe(1)
    expect(options[0].textContent).toContain('Go to Home')
  })

  it('shows board search results when available', async () => {
    mockBoards.value = [
      { id: 'b1', name: 'Sprint Board', description: 'Sprint planning', isArchived: false },
    ]
    mountPalette()
    await wrapper.vm.$nextTick()
    const options = findAllOptions()
    // 3 commands + 1 board result
    expect(options.length).toBe(4)
    expect(options[3].textContent).toContain('Sprint Board')
  })

  it('shows card search results when available', async () => {
    mockCards.value = [
      {
        id: 'c1',
        boardId: 'b1',
        boardName: 'Dev Board',
        columnId: 'col1',
        columnName: 'To Do',
        title: 'Fix login bug',
        description: 'Auth issue',
      },
    ]
    mountPalette()
    await wrapper.vm.$nextTick()
    const options = findAllOptions()
    // 3 commands + 1 card result
    expect(options.length).toBe(4)
    expect(options[3].textContent).toContain('Fix login bug')
    expect(options[3].textContent).toContain('Dev Board / To Do')
  })

  it('emits navigateToBoard when a board result is clicked', async () => {
    mockBoards.value = [
      { id: 'b1', name: 'Sprint Board', description: null, isArchived: false },
    ]
    mountPalette()
    await wrapper.vm.$nextTick()
    const options = findAllOptions()
    ;(options[3] as HTMLElement).click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('navigateToBoard')).toBeTruthy()
    expect(wrapper.emitted('navigateToBoard')![0]).toEqual(['b1'])
  })

  it('emits navigateToCard when a card result is clicked', async () => {
    mockCards.value = [
      {
        id: 'c1',
        boardId: 'b1',
        boardName: 'Dev Board',
        columnId: 'col1',
        columnName: 'To Do',
        title: 'Fix bug',
        description: '',
      },
    ]
    mountPalette()
    await wrapper.vm.$nextTick()
    const options = findAllOptions()
    ;(options[3] as HTMLElement).click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('navigateToCard')).toBeTruthy()
    expect(wrapper.emitted('navigateToCard')![0]).toEqual(['b1', 'c1'])
  })

  it('supports keyboard navigation across all result types', async () => {
    mockBoards.value = [
      { id: 'b1', name: 'Board 1', description: null, isArchived: false },
    ]
    mountPalette()
    await wrapper.vm.$nextTick()
    const input = findInput()!

    // Initially first item is selected
    let activeItems = document.body.querySelectorAll('.td-command-palette__item--active')
    expect(activeItems.length).toBe(1)
    expect(activeItems[0].textContent).toContain('Go to Home')

    // Press down three times to reach the board result
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
    await wrapper.vm.$nextTick()
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
    await wrapper.vm.$nextTick()
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
    await wrapper.vm.$nextTick()

    activeItems = document.body.querySelectorAll('.td-command-palette__item--active')
    expect(activeItems.length).toBe(1)
    expect(activeItems[0].textContent).toContain('Board 1')

    // Press enter to activate
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('navigateToBoard')).toBeTruthy()
  })

  it('emits close when escape is pressed', async () => {
    mountPalette()
    const input = findInput()!
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('emits close when overlay backdrop is clicked', async () => {
    mountPalette()
    const overlay = document.body.querySelector('.td-overlay') as HTMLElement
    expect(overlay).not.toBeNull()
    overlay.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toBeTruthy()
  })

  it('shows loading indicator during search', async () => {
    mockSearchLoading.value = true
    mountPalette()
    const input = findInput()!
    input.value = 'te'
    input.dispatchEvent(new Event('input'))
    await wrapper.vm.$nextTick()
    const loadingEl = document.body.querySelector('.td-command-palette__loading')
    expect(loadingEl).not.toBeNull()
    expect(loadingEl!.textContent).toContain('Searching...')
  })

  it('shows group headers when results span multiple types', async () => {
    mockBoards.value = [
      { id: 'b1', name: 'Board X', description: null, isArchived: false },
    ]
    mockCards.value = [
      {
        id: 'c1',
        boardId: 'b1',
        boardName: 'Board X',
        columnId: 'col1',
        columnName: 'Done',
        title: 'Card Y',
        description: '',
      },
    ]
    mountPalette()
    await wrapper.vm.$nextTick()
    const headers = Array.from(document.body.querySelectorAll('.td-command-palette__group-title'))
    const headerTexts = headers.map((h) => h.textContent?.trim())
    expect(headerTexts).toContain('Commands')
    expect(headerTexts).toContain('Boards')
    expect(headerTexts).toContain('Cards')
  })

  it('renders keyboard hint footer', () => {
    mountPalette()
    const footer = document.body.querySelector('.td-command-palette__footer')
    expect(footer).not.toBeNull()
    expect(footer!.textContent).toContain('navigate')
    expect(footer!.textContent).toContain('select')
    expect(footer!.textContent).toContain('close')
  })
})
