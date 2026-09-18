import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import CardArchiveAction from '../../components/board/CardArchiveAction.vue'
import type { Card, CardDetachPreview } from '../../types/board'

const mocks = vi.hoisted(() => ({ previewDetach: vi.fn(), setCardArchived: vi.fn(), toastError: vi.fn() }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: { previewDetach: mocks.previewDetach } }))
vi.mock('../../store/toastStore', () => ({ useToastStore: () => ({ error: mocks.toastError }) }))
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
  wrapper.get('button').element.focus()
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

  it.each(['body', 'disabled', 'removed'] as const)('a dismissed write failure rescues %s focus', async lostFocus => {
    const held = deferred<void>()
    mocks.setCardArchived.mockReturnValueOnce(held.promise)
    const view = await openConfirmation()
    buttonIn(modal(), 'Confirm archive').click()
    await flushPromises()
    await escape()
    if (lostFocus === 'body') (document.activeElement as HTMLElement)?.blur()
    else {
      const previous = focusElsewhere()
      if (lostFocus === 'disabled') previous.disabled = true
      else previous.remove()
    }
    held.reject(new Error('Could not confirm archive'))
    await flushPromises()

    expect(modal()).toBeNull()
    expect(document.activeElement).toBe(buttonIn(view.element, 'Refresh card state'))
    expect(mocks.setCardArchived).toHaveBeenCalledTimes(1)
    expect(view.emitted('changed')).toBeUndefined()
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

  it.each([
    { archive: true, outcome: 'success' }, { archive: true, outcome: 'failure' },
    { archive: false, outcome: 'success' }, { archive: false, outcome: 'failure' },
  ])('a switched-card write (archive=$archive, $outcome) preserves the new request owner', async ({ archive, outcome }) => {
    const old = deferred<void>()
    const current = deferred<CardDetachPreview>()
    mocks.setCardArchived.mockReturnValueOnce(old.promise)
    if (archive) {
      await openConfirmation()
      buttonIn(modal(), 'Confirm archive').click()
    } else {
      wrapper = mount(CardArchiveAction, { props: { card: { ...card, isArchived: true } }, attachTo: document.body })
      await wrapper.get('button').trigger('click')
    }
    await flushPromises()
    const view = wrapper!
    await view.setProps({ card: { ...card, id: 'c2' } })
    mocks.previewDetach.mockReturnValueOnce(current.promise)
    await view.get('button').trigger('click')
    const elsewhere = focusElsewhere()

    if (outcome === 'success') old.resolve(undefined)
    else old.reject(new Error('Old card private error detail'))
    await flushPromises()

    expect(modal()).toBeNull()
    expect(view.find('[role="alert"]').exists()).toBe(false)
    expect(view.get('button').attributes('disabled')).toBeDefined()
    expect(document.activeElement).toBe(elsewhere)
    expect(view.emitted('changed')).toBeUndefined()
    expect(view.emitted('refresh')).toBeUndefined()
    if (outcome === 'failure') {
      expect(mocks.toastError).toHaveBeenCalledExactlyOnceWith(`The ${archive ? 'archive' : 'restore'} requested for a previously viewed card could not be confirmed. Reopen that card and refresh its state before trying again.`)
    } else expect(mocks.toastError).not.toHaveBeenCalled()

    current.resolve({ ...freshPreview, cardId: 'c2' })
    await flushPromises()
    expect(buttonIn(modal(), 'Confirm archive').disabled).toBe(false)
  })

  it.each([
    { archive: true, outcome: 'success' }, { archive: true, outcome: 'failure' },
    { archive: false, outcome: 'success' }, { archive: false, outcome: 'failure' },
  ])('A-to-B-to-A preserves independent pending writes (archive=$archive, $outcome)', async ({ archive, outcome }) => {
    const original = deferred<void>()
    const other = deferred<void>()
    mocks.setCardArchived.mockReturnValueOnce(original.promise).mockReturnValueOnce(other.promise)
    const originalCard = { ...card, isArchived: !archive }
    if (archive) {
      await openConfirmation()
      buttonIn(modal(), 'Confirm archive').click()
    } else {
      wrapper = mount(CardArchiveAction, { props: { card: originalCard }, attachTo: document.body })
      await wrapper.get('button').trigger('click')
    }
    await flushPromises()
    const view = wrapper!
    const otherCard = { ...card, id: 'c2', title: 'Other card' }
    await view.setProps({ card: otherCard })
    expect(view.get('button').attributes('disabled')).toBeUndefined()
    mocks.previewDetach.mockResolvedValueOnce({ ...freshPreview, cardId: otherCard.id })
    await view.get('button').trigger('click')
    await flushPromises()
    buttonIn(modal(), 'Confirm archive').click()
    await flushPromises()
    expect(mocks.setCardArchived).toHaveBeenCalledTimes(2)
    expect(mocks.setCardArchived.mock.calls.map(call => call.slice(0, 3))).toEqual([
      ['b', 'c1', archive], ['b', 'c2', true],
    ])

    await view.setProps({ card: originalCard })
    expect(modal()).toBeNull()
    const previewsBeforeReturn = mocks.previewDetach.mock.calls.length
    expect(view.get('button').attributes('disabled')).toBeDefined()
    await view.get('button').trigger('click')
    expect(mocks.previewDetach).toHaveBeenCalledTimes(previewsBeforeReturn)
    expect(mocks.setCardArchived).toHaveBeenCalledTimes(2)

    await view.setProps({ card: otherCard })
    const elsewhere = focusElsewhere()
    if (outcome === 'success') original.resolve(undefined)
    else original.reject(Object.assign(new Error('Private original failure'), { response: { status: 403 } }))
    await flushPromises()
    expect(view.get('button').attributes('disabled')).toBeDefined()
    expect(document.activeElement).toBe(elsewhere)
    expect(view.find('[role="alert"]').exists()).toBe(false)
    expect(view.emitted('changed')).toBeUndefined()
    expect(view.emitted('permission-denied')).toBeUndefined()
    if (outcome === 'failure') {
      expect(mocks.toastError).toHaveBeenCalledExactlyOnceWith(`The ${archive ? 'archive' : 'restore'} requested for a previously viewed card could not be confirmed. Reopen that card and refresh its state before trying again.`)
    } else expect(mocks.toastError).not.toHaveBeenCalled()

    await view.setProps({ card: originalCard })
    expect(view.get('button').attributes('disabled')).toBeUndefined()
    expect(modal()).toBeNull()
    expect(mocks.setCardArchived).toHaveBeenCalledTimes(2)
    other.resolve(undefined)
    await flushPromises()
    expect(view.get('button').attributes('disabled')).toBeUndefined()
    expect(view.emitted('changed')).toBeUndefined()
    expect(view.emitted('permission-denied')).toBeUndefined()
  })

  it('pending ownership includes the board identity', async () => {
    const original = deferred<void>()
    const other = deferred<void>()
    mocks.setCardArchived.mockReturnValueOnce(original.promise).mockReturnValueOnce(other.promise)
    const archivedCard = { ...card, isArchived: true }
    wrapper = mount(CardArchiveAction, { props: { card: archivedCard, canWrite: true }, attachTo: document.body })
    await wrapper.get('button').trigger('click')
    await wrapper.setProps({ card: { ...archivedCard, boardId: 'other-board' } })
    expect(wrapper.get('button').attributes('disabled')).toBeUndefined()
    await wrapper.get('button').trigger('click')
    expect(mocks.setCardArchived.mock.calls.map(call => call.slice(0, 2))).toEqual([
      ['b', 'c1'], ['other-board', 'c1'],
    ])
    await wrapper.setProps({ card: archivedCard })
    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
    other.resolve(undefined)
    await flushPromises()
    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
    original.resolve(undefined)
    await flushPromises()
    expect(wrapper.get('button').attributes('disabled')).toBeUndefined()
  })

  it('a returning card keeps its pending preview until that request settles', async () => {
    const held = deferred<CardDetachPreview>()
    mocks.previewDetach.mockReturnValueOnce(held.promise)
    wrapper = mount(CardArchiveAction, { props: { card }, attachTo: document.body })
    await wrapper.get('button').trigger('click')
    await wrapper.setProps({ card: { ...card, id: 'c2' } })
    expect(wrapper.get('button').attributes('disabled')).toBeUndefined()
    await wrapper.setProps({ card })
    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
    await wrapper.get('button').trigger('click')
    expect(mocks.previewDetach).toHaveBeenCalledTimes(1)
    held.resolve(preview)
    await flushPromises()
    expect(wrapper.get('button').attributes('disabled')).toBeUndefined()
    expect(modal()).toBeNull()
    expect(mocks.setCardArchived).not.toHaveBeenCalled()
  })

  it('unmount suppresses late failed-write UI and global notices', async () => {
    const held = deferred<void>()
    mocks.setCardArchived.mockReturnValueOnce(held.promise)
    const view = await openConfirmation()
    buttonIn(modal(), 'Confirm archive').click()
    await flushPromises()
    view.unmount()
    wrapper = null
    const elsewhere = focusElsewhere()
    held.reject(new Error('Obsolete failure'))
    await flushPromises()

    expect(mocks.toastError).not.toHaveBeenCalled()
    expect(document.activeElement).toBe(elsewhere)
    expect(view.emitted('changed')).toBeUndefined()
  })

  it.each([
    { archived: false, dismiss: false },
    { archived: false, dismiss: true },
    { archived: true, dismiss: false },
  ])('a current-card write 403 emits permission-denied (archived=$archived, dismiss=$dismiss)', async ({ archived, dismiss }) => {
    const held = deferred<void>()
    mocks.setCardArchived.mockReturnValueOnce(held.promise)
    if (archived) {
      wrapper = mount(CardArchiveAction, { props: { card: { ...card, isArchived: true } }, attachTo: document.body })
      await wrapper.get('button').trigger('click')
    } else {
      await openConfirmation()
      buttonIn(modal(), 'Confirm archive').click()
      await flushPromises()
      if (dismiss) await escape()
    }
    held.reject(Object.assign(new Error('Write access denied'), { response: { status: 403 } }))
    await flushPromises()

    expect(wrapper!.emitted('permission-denied')).toEqual([[]])
    expect(wrapper!.emitted('changed')).toBeUndefined()
    expect(mocks.setCardArchived).toHaveBeenCalledTimes(1)
  })

  it.each(['card', 'board', 'unmount'] as const)('a 403 after a %s context change cannot invalidate current permissions', async contextChange => {
    const held = deferred<void>()
    mocks.setCardArchived.mockReturnValueOnce(held.promise)
    const view = await openConfirmation()
    buttonIn(modal(), 'Confirm archive').click()
    await flushPromises()
    if (contextChange === 'unmount') {
      view.unmount()
      wrapper = null
    } else {
      await view.setProps({ card: contextChange === 'card'
        ? { ...card, id: 'c2' }
        : { ...card, boardId: 'other-board' } })
    }
    held.reject(Object.assign(new Error('Old permission denial'), { response: { status: 403 } }))
    await flushPromises()

    expect(view.emitted('permission-denied')).toBeUndefined()
    expect(view.emitted('changed')).toBeUndefined()
    if (contextChange !== 'unmount') expect(view.find('[role="alert"]').exists()).toBe(false)
  })

  it.each([404, 409, 500])('a write %s does not emit permission-denied', async status => {
    mocks.setCardArchived.mockRejectedValueOnce({ response: { status } })
    const view = await openConfirmation()
    buttonIn(modal(), 'Confirm archive').click()
    await flushPromises()
    expect(view.emitted('permission-denied')).toBeUndefined()
  })

  it('a preview 403 does not claim a confirmed write permission denial', async () => {
    mocks.previewDetach.mockRejectedValueOnce({ response: { status: 403 } })
    wrapper = mount(CardArchiveAction, { props: { card }, attachTo: document.body })
    await wrapper.get('button').trigger('click')
    await flushPromises()
    expect(wrapper.emitted('permission-denied')).toBeUndefined()
    expect(mocks.setCardArchived).not.toHaveBeenCalled()
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
