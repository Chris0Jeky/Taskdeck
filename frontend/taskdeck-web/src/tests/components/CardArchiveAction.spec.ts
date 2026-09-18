import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import CardArchiveAction from '../../components/board/CardArchiveAction.vue'
import { useBoardStore } from '../../store/boardStore'
import { cardsApi } from '../../api/cardsApi'
import type { BoardDetail, Card, CardDetachPreview } from '../../types/board'

vi.mock('../../api/cardsApi', () => ({
  cardsApi: { previewDetach: vi.fn(), setArchived: vi.fn(), getArchivedCards: vi.fn() },
}))

const activeCard = {
  id: 'c1', boardId: 'b', columnId: 'col', title: 'Parent', description: '',
  labels: [], updatedAt: '2026-09-10T10:00:00Z', isArchived: false,
} as unknown as Card

const archivedCard = { ...activeCard, id: 'c2', isArchived: true } as unknown as Card

const child = (id: string, title: string) => ({
  id, parentCardId: 'c1', title, isArchived: false, updatedAt: '2026-09-10T10:00:00Z',
})

const stalePreview: CardDetachPreview = {
  cardId: 'c1', expectedUpdatedAt: '2026-09-10T10:00:00Z',
  expectedChildrenFingerprint: 'v1:stale', children: [child('child-1', 'First child')],
}
const refreshedPreview: CardDetachPreview = {
  cardId: 'c1', expectedUpdatedAt: '2026-09-10T11:00:00Z',
  expectedChildrenFingerprint: 'v2:fresh', children: [child('child-1', 'First child'), child('child-2', 'Second child')],
}

/** The confirmation is teleported to <body>, so assertions resolve it from the document. */
const modal = () => document.querySelector<HTMLElement>('[aria-modal="true"]')
const inModal = (selector: string) => modal()?.querySelector<HTMLElement>(selector) ?? null
const buttonIn = (root: ParentNode | null, label: string) =>
  Array.from(root?.querySelectorAll<HTMLButtonElement>('button') ?? [])
    .find(node => node.textContent?.trim().startsWith(label)) ?? null

let wrapper: VueWrapper | null = null
const mountAction = (card: Card) => {
  wrapper = mount(CardArchiveAction, { props: { card }, attachTo: document.body })
  return wrapper
}

/** Opens the confirmation for `activeCard` with the stale child list rendered. */
async function openConfirmation() {
  const view = mountAction(activeCard)
  await view.get('button').trigger('click')
  await flushPromises()
  expect(modal()).not.toBeNull()
  return view
}

describe('CardArchiveAction confirmation recovery', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    document.body.innerHTML = ''
    useBoardStore().currentBoard = { id: 'b', canWrite: true, isArchived: false, columns: [] } as unknown as BoardDetail
    vi.mocked(cardsApi.previewDetach).mockResolvedValue(stalePreview)
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
  })

  it('archives through the explicit child-list confirmation on the happy path', async () => {
    vi.mocked(cardsApi.setArchived).mockResolvedValue({ ...activeCard, isArchived: true } as Card)
    const view = await openConfirmation()

    expect(modal()?.textContent).toContain('First child')
    expect(cardsApi.setArchived).not.toHaveBeenCalled()

    buttonIn(modal(), 'Confirm archive')!.click()
    await flushPromises()

    expect(cardsApi.setArchived).toHaveBeenCalledTimes(1)
    expect(cardsApi.setArchived).toHaveBeenCalledWith('b', 'c1', true, stalePreview.expectedUpdatedAt, stalePreview.expectedChildrenFingerprint)
    expect(modal()).toBeNull()
    expect(view.emitted('changed')).toHaveLength(1)
  })

  it('renders the failure and its recovery control inside the active modal', async () => {
    vi.mocked(cardsApi.setArchived).mockRejectedValue(new Error('Card changed'))
    const view = await openConfirmation()

    buttonIn(modal(), 'Confirm archive')!.click()
    await flushPromises()

    // The confirmation is still the active modal, and the failure lives inside it.
    expect(modal()).not.toBeNull()
    const alert = inModal('[role="alert"]')
    expect(alert).not.toBeNull()
    expect(alert!.textContent).toContain('Card changed')
    expect(buttonIn(inModal('[data-testid="archive-dialog-recovery"]'), 'Refresh child list')).not.toBeNull()
    // ...and nowhere underneath it.
    expect(view.find('[role="alert"]').exists()).toBe(false)
    // The stale child list the user agreed to is still on screen.
    expect(modal()?.textContent).toContain('First child')
  })

  it('disables Confirm after a failure and never auto-approves or auto-retries the stale confirmation', async () => {
    vi.mocked(cardsApi.setArchived).mockRejectedValue(new Error('Card changed'))
    await openConfirmation()

    buttonIn(modal(), 'Confirm archive')!.click()
    await flushPromises()
    expect(buttonIn(modal(), 'Confirm archive')!.disabled).toBe(true)
    expect(cardsApi.setArchived).toHaveBeenCalledTimes(1)

    // Refreshing the child list re-reads the preview. It must not resubmit the change.
    vi.mocked(cardsApi.previewDetach).mockResolvedValue(refreshedPreview)
    buttonIn(modal(), 'Refresh child list')!.click()
    await flushPromises()

    expect(cardsApi.previewDetach).toHaveBeenCalledTimes(2)
    expect(cardsApi.setArchived).toHaveBeenCalledTimes(1)
    expect(inModal('[role="alert"]')).toBeNull()
    expect(modal()?.textContent).toContain('Second child')
    expect(modal()?.textContent).toContain('Child list refreshed')

    // Only an explicit second Confirm submits, and it submits the REFRESHED tokens.
    const confirm = buttonIn(modal(), 'Confirm archive')!
    expect(confirm.disabled).toBe(false)
    vi.mocked(cardsApi.setArchived).mockResolvedValue({ ...activeCard, isArchived: true } as Card)
    confirm.click()
    await flushPromises()

    expect(cardsApi.setArchived).toHaveBeenCalledTimes(2)
    expect(cardsApi.setArchived).toHaveBeenLastCalledWith('b', 'c1', true, refreshedPreview.expectedUpdatedAt, refreshedPreview.expectedChildrenFingerprint)
  })

  it('keeps keyboard recovery inside the dialog and leaves Escape cancelling', async () => {
    vi.mocked(cardsApi.setArchived).mockRejectedValue(new Error('Card changed'))
    await openConfirmation()

    buttonIn(modal(), 'Confirm archive')!.click()
    await flushPromises()

    const refresh = buttonIn(modal(), 'Refresh child list')!
    const cancel = buttonIn(modal(), 'Cancel')!
    expect(document.activeElement).toBe(refresh)

    // Tab from the last focusable control cycles back to the in-dialog recovery
    // control, so the recovery affordance is inside the dialog's Tab cycle.
    cancel.focus()
    cancel.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }))
    await flushPromises()
    expect(document.activeElement).toBe(refresh)

    // Shift+Tab off the recovery control wraps to the end of the same dialog.
    refresh.focus()
    refresh.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }))
    await flushPromises()
    expect(document.activeElement).toBe(cancel)

    // Escape still cancels, and the unresolved failure moves to the page-level
    // alert whose Refresh control takes focus (the opener stays disabled).
    refresh.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await flushPromises()
    expect(modal()).toBeNull()
    const pageAlert = wrapper!.find('[role="alert"]')
    expect(pageAlert.exists()).toBe(true)
    expect(pageAlert.text()).toContain('Refresh card state')
    expect(document.activeElement).toBe(buttonIn(wrapper!.element, 'Refresh card state'))
  })

  it('keeps the page-level failure path for the no-dialog restore flow', async () => {
    vi.mocked(cardsApi.setArchived).mockRejectedValue(new Error('Card changed'))
    const view = mountAction(archivedCard)

    await view.get('button').trigger('click')
    await flushPromises()

    expect(modal()).toBeNull()
    expect(cardsApi.previewDetach).not.toHaveBeenCalled()
    expect(cardsApi.setArchived).toHaveBeenCalledTimes(1)
    expect(view.get('button').attributes('disabled')).toBeDefined()
    const alert = view.find('[role="alert"]')
    expect(alert.exists()).toBe(true)
    expect(alert.text()).toContain('Refresh card state')
    expect(document.activeElement).toBe(buttonIn(view.element, 'Refresh card state'))
  })

  it('keeps a preview read failure on the page when no confirmation ever opened', async () => {
    vi.mocked(cardsApi.previewDetach).mockRejectedValue(new Error('Offline'))
    const view = mountAction(activeCard)

    await view.get('button').trigger('click')
    await flushPromises()

    expect(modal()).toBeNull()
    expect(view.find('[role="alert"]').text()).toContain('Offline')
    expect(document.activeElement).toBe(buttonIn(view.element, 'Refresh card state'))
  })
})
