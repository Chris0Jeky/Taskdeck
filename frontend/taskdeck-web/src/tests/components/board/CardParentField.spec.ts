import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import CardParentField from '../../../components/board/CardParentField.vue'
import { useBoardStore } from '../../../store/boardStore'
import { cardsApi } from '../../../api/cardsApi'
import type { BoardDetail, Card } from '../../../types/board'

vi.mock('../../../api/cardsApi', () => ({ cardsApi: { getCards: vi.fn() } }))

const PERMISSION_COPY = 'You no longer have access to this board'
const RETRY_COPY = 'Close and reopen this card to retry'

const makeCard = (id: string, title = `Card ${id}`) =>
  ({ id, boardId: 'b', columnId: 'col', title, description: '', labels: [], updatedAt: '2026-09-10T10:00:00Z' }) as unknown as Card

const forbidden = () => ({ response: { status: 403 } })

/*
 * `canWrite` is the host's resolved, server-authoritative answer (#3028). This field no
 * longer derives one from the loaded board payload, so the default here is the writer case
 * and a test that wants the read-only case says so explicitly.
 */
const mountField = (card: Card = makeCard('self'), canWrite = true) =>
  mount(CardParentField, { props: { card, canWrite, modelValue: null } })

/** A promise whose settlement this test controls, so request ordering is explicit. */
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

describe('CardParentField', () => {
  beforeEach(() => {
    setActivePinia(createPinia()); vi.clearAllMocks()
    useBoardStore().currentBoard = { id: 'b', canWrite: true, isArchived: false, columns: [] } as unknown as BoardDetail
    vi.mocked(cardsApi.getCards).mockResolvedValue([makeCard('other', 'Other card')])
  })

  it('lists the other cards on the board and offers the selector', async () => {
    const wrapper = mountField(); await flushPromises()
    expect(cardsApi.getCards).toHaveBeenCalledWith('b')
    expect(wrapper.findAll('option').map(option => option.text())).toEqual(['No parent', 'Other card (Task)'])
    expect(wrapper.get('select').attributes('disabled')).toBeUndefined()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Optional, on this board.')
  })

  it('retires a pending read after denied access and reloads only when reads are allowed again', async () => {
    const stale = deferred<Card[]>()
    vi.mocked(cardsApi.getCards).mockReturnValueOnce(stale.promise)
    const wrapper = mountField()
    await wrapper.setProps({ readsBlocked: true, canWrite: false })
    stale.resolve([makeCard('stale', 'Stale choice')])
    await flushPromises()
    expect(wrapper.text()).not.toContain('Stale choice')
    expect(wrapper.get('select').attributes('disabled')).toBeDefined()
    expect(cardsApi.getCards).toHaveBeenCalledTimes(1)
    await wrapper.setProps({ readsBlocked: false, canWrite: true })
    await flushPromises()
    expect(cardsApi.getCards).toHaveBeenCalledTimes(2)
    expect(wrapper.text()).toContain('Other card')
    expect(wrapper.get('select').attributes('disabled')).toBeUndefined()
    wrapper.unmount()
  })

  /*
   * #3028. This field reads no board payload at all any more: the host's one
   * server-authoritative answer is the whole gate, which is what stops it reading an
   * omitted optional `canWrite` as "no". The payload the store holds is left at the
   * writer fixture here on purpose — what this case proves is that the prop alone
   * decides. The omitted-payload journey itself is covered end to end in
   * `CardModal.spec.ts` ("enables all four once the server answers").
   */
  it('offers the selector on the host answer alone', async () => {
    const wrapper = mountField(); await flushPromises()
    expect(wrapper.get('select').attributes('disabled')).toBeUndefined()

    const readOnly = mountField(makeCard('self'), false); await flushPromises()
    expect(readOnly.get('select').attributes('disabled')).toBeDefined()
  })

  it('names revoked board access instead of offering a retry that cannot succeed', async () => {
    vi.mocked(cardsApi.getCards).mockRejectedValueOnce(forbidden())
    const wrapper = mountField(); await flushPromises()
    const alert = wrapper.get('[role="alert"]')
    expect(alert.text()).toContain(PERMISSION_COPY)
    expect(alert.text()).not.toContain(RETRY_COPY)
    expect(wrapper.get('select').attributes('disabled')).toBeDefined()
    expect(wrapper.findAll('option').map(option => option.text())).toEqual(['No parent'])
    expect(cardsApi.getCards).toHaveBeenCalledTimes(1)
  })

  it('keeps the retry guidance for an ordinary read failure', async () => {
    vi.mocked(cardsApi.getCards).mockRejectedValueOnce({ response: { status: 500 } })
    const wrapper = mountField(); await flushPromises()
    const alert = wrapper.get('[role="alert"]')
    expect(alert.text()).toContain(RETRY_COPY)
    expect(alert.text()).not.toContain(PERMISSION_COPY)
    expect(wrapper.get('select').attributes('disabled')).toBeDefined()
  })

  it('classifies a network failure with no response as transient, not as permission loss', async () => {
    vi.mocked(cardsApi.getCards).mockRejectedValueOnce(new Error('Network Error'))
    const wrapper = mountField(); await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain(RETRY_COPY)
  })

  it('ignores an obsolete request that fails after a newer one succeeded', async () => {
    const stale = deferred<Card[]>()
    vi.mocked(cardsApi.getCards).mockReturnValueOnce(stale.promise).mockResolvedValueOnce([makeCard('other', 'Other card')])
    const wrapper = mountField(makeCard('first'))
    await wrapper.setProps({ card: makeCard('second') })
    await flushPromises()
    stale.reject(forbidden())
    await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(wrapper.findAll('option').map(option => option.text())).toEqual(['No parent', 'Other card (Task)'])
    expect(wrapper.get('select').attributes('disabled')).toBeUndefined()
  })

  it('ignores an obsolete request that succeeds after a newer one lost access', async () => {
    const stale = deferred<Card[]>()
    vi.mocked(cardsApi.getCards).mockReturnValueOnce(stale.promise).mockRejectedValueOnce(forbidden())
    const wrapper = mountField(makeCard('first'))
    await wrapper.setProps({ card: makeCard('second') })
    await flushPromises()
    stale.resolve([makeCard('other', 'Other card')])
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain(PERMISSION_COPY)
    expect(wrapper.findAll('option').map(option => option.text())).toEqual(['No parent'])
    expect(wrapper.get('select').attributes('disabled')).toBeDefined()
  })
})
