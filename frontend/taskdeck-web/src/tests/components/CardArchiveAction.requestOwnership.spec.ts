import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import CardArchiveAction from '../../components/board/CardArchiveAction.vue'
import type { Card, CardDetachPreview } from '../../types/board'

const mocks = vi.hoisted(() => ({ previewDetach: vi.fn(), setCardArchived: vi.fn() }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: { previewDetach: mocks.previewDetach } }))
vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => ({
    currentBoard: { id: 'b', canWrite: true, isArchived: false },
    setCardArchived: mocks.setCardArchived,
  }),
}))

const card = {
  id: 'c1', boardId: 'b', columnId: 'col', title: 'Parent', description: '',
  labels: [], updatedAt: '2026-09-10T10:00:00Z', isArchived: false,
} as unknown as Card
const preview: CardDetachPreview = {
  cardId: 'c1', expectedUpdatedAt: card.updatedAt,
  expectedChildrenFingerprint: 'v1:stale', children: [],
}
const freshPreview: CardDetachPreview = { ...preview, expectedChildrenFingerprint: 'v2:fresh' }
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: Error) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
const modal = () => document.querySelector<HTMLElement>('[aria-modal="true"]')
const buttonIn = (root: ParentNode | null, text: string) =>
  Array.from(root?.querySelectorAll<HTMLButtonElement>('button') ?? [])
    .find(button => button.textContent?.trim().startsWith(text))!
let wrapper: VueWrapper | null = null
async function openConfirmation() {
  wrapper = mount(CardArchiveAction, { props: { card }, attachTo: document.body })
  await wrapper.get('button').trigger('click')
  await flushPromises()
  expect(modal()).not.toBeNull()
  return wrapper
}
async function escape() {
  expect(modal()).not.toBeNull()
  modal()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
  await flushPromises()
  expect(modal()).toBeNull()
}
function focusElsewhere() {
  const button = document.createElement('button')
  button.textContent = 'Unrelated action'
  document.body.append(button)
  button.focus()
  return button
}

describe('CardArchiveAction request ownership (GH-2996)', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    document.body.innerHTML = ''
    mocks.previewDetach.mockResolvedValue(preview)
    mocks.setCardArchived.mockResolvedValue(undefined)
  })
  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
  })

  it.each(['success', 'failure'] as const)('Escape rejects a late child-refresh %s without reopening or stealing focus', async outcome => {
    mocks.setCardArchived.mockRejectedValueOnce(new Error('Original conflict'))
    const view = await openConfirmation()
    buttonIn(modal(), 'Confirm archive').click()
    await flushPromises()
    const held = deferred<CardDetachPreview>()
    mocks.previewDetach.mockReturnValueOnce(held.promise)
    buttonIn(modal(), 'Refresh child list').click()
    await flushPromises()
    expect(buttonIn(modal(), 'Cancel').disabled).toBe(true)
    await escape()
    expect(view.get('[role="alert"]').text()).toContain('Original conflict')
    const elsewhere = focusElsewhere()

    if (outcome === 'success') held.resolve(freshPreview)
    else held.reject(new Error('Obsolete refresh failure'))
    await flushPromises()

    expect(modal()).toBeNull()
    expect(view.get('[role="alert"]').text()).toContain('Original conflict')
    expect(view.text()).not.toContain('Obsolete refresh failure')
    expect(view.text()).not.toContain('Child list refreshed')
    expect(document.activeElement).toBe(elsewhere)
    expect(mocks.setCardArchived).toHaveBeenCalledTimes(1)
    expect(view.emitted('changed')).toBeUndefined()
    expect(view.emitted('refresh')).toBeUndefined()
  })

  it.each(['success', 'failure'] as const)('Escape during a submitted write preserves its %s outcome without reopening', async outcome => {
    const held = deferred<void>()
    mocks.setCardArchived.mockReturnValueOnce(held.promise)
    const view = await openConfirmation()
    buttonIn(modal(), 'Confirm archive').click()
    await flushPromises()
    await escape()
    const elsewhere = focusElsewhere()
    if (outcome === 'success') held.resolve(undefined)
    else held.reject(new Error('Could not confirm archive'))
    await flushPromises()

    expect(modal()).toBeNull()
    expect(document.activeElement).toBe(elsewhere)
    expect(mocks.setCardArchived).toHaveBeenCalledTimes(1)
    expect(mocks.setCardArchived).toHaveBeenCalledWith('b', 'c1', true, preview.expectedUpdatedAt, preview.expectedChildrenFingerprint)
    if (outcome === 'success') expect(view.emitted('changed')).toHaveLength(1)
    else {
      expect(view.emitted('changed')).toBeUndefined()
      expect(view.get('[role="alert"]').text()).toContain('Could not confirm archive')
    }
  })

  it.each(['success', 'failure'] as const)('a card switch discards old preview %s without clearing a newer busy state', async outcome => {
    const old = deferred<CardDetachPreview>()
    const current = deferred<CardDetachPreview>()
    mocks.previewDetach.mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise)
    const view = mount(CardArchiveAction, { props: { card }, attachTo: document.body })
    wrapper = view
    await view.get('button').trigger('click')
    await view.setProps({ card: { ...card, id: 'c2', title: 'Other card' } })
    await view.get('button').trigger('click')
    expect(mocks.previewDetach).toHaveBeenCalledTimes(2)
    if (outcome === 'success') old.resolve(preview)
    else old.reject(new Error('Old card failure'))
    await flushPromises()
    expect(modal()).toBeNull()
    expect(view.find('[role="alert"]').exists()).toBe(false)
    expect(view.get('button').attributes('disabled')).toBeDefined()
    current.resolve({ ...freshPreview, cardId: 'c2' })
    await flushPromises()
    expect(modal()).not.toBeNull()
    expect(buttonIn(modal(), 'Confirm archive').disabled).toBe(false)
  })

  it('a same-card prop refresh does not discard a committed write receipt', async () => {
    const held = deferred<void>()
    mocks.setCardArchived.mockReturnValueOnce(held.promise)
    const view = await openConfirmation()
    buttonIn(modal(), 'Confirm archive').click()
    await flushPromises()
    await view.setProps({ card: { ...card, isArchived: true, updatedAt: '2026-09-10T11:00:00Z' } })
    held.resolve(undefined)
    await flushPromises()
    expect(view.emitted('changed')).toHaveLength(1)
  })

  it('unmount discards a late write receipt instead of notifying an obsolete parent', async () => {
    const held = deferred<void>()
    mocks.setCardArchived.mockReturnValueOnce(held.promise)
    const view = await openConfirmation()
    buttonIn(modal(), 'Confirm archive').click()
    await flushPromises()
    view.unmount()
    wrapper = null
    held.resolve(undefined)
    await flushPromises()
    expect(view.emitted('changed')).toBeUndefined()
    expect(modal()).toBeNull()
  })
})
