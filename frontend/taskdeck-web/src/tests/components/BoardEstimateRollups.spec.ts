import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import BoardEstimateRollups from '../../components/board/BoardEstimateRollups.vue'
import { estimateRollupsApi } from '../../api/estimateRollupsApi'
import { useBoardStore } from '../../store/boardStore'
import { useSessionStore } from '../../store/sessionStore'
import type { BoardEstimateRollup } from '../../types/estimateRollups'
import type { BoardDetail, Card } from '../../types/board'

vi.mock('../../api/estimateRollupsApi', () => ({ estimateRollupsApi: { get: vi.fn() } }))
const total = { cardCount: 3, knownEstimateMinutes: 90, missingEstimateCount: 1 }
const fixture: BoardEstimateRollup = {
  boardId: 'board', generatedAt: '2026-09-12T10:00:00Z', board: total,
  unassigned: { cardCount: 1, knownEstimateMinutes: 0, missingEstimateCount: 0 },
  columns: [{ columnId: 'next', name: 'Next', totals: total }],
  participants: [{ userId: 'owner', username: 'Owner', totals: total }],
}
const deferred = <T,>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}
const mountPanel = () => mount(BoardEstimateRollups, { props: { boardId: 'board' }, attachTo: document.body })

describe('Board estimate rollups', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    useSessionStore().userId = 'owner'
    useSessionStore().token = 'session'
    useBoardStore().currentBoard = { id: 'board', columns: [] } as unknown as BoardDetail
    vi.mocked(estimateRollupsApi.get).mockResolvedValue(fixture)
  })

  it('loads only on request and explains known, missing, unassigned and overlapping totals', async () => {
    const wrapper = mountPanel()
    expect(estimateRollupsApi.get).not.toHaveBeenCalled()
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(estimateRollupsApi.get).toHaveBeenCalledWith('board', { signal: expect.any(AbortSignal) })
    expect(wrapper.text()).toContain('1h 30m known estimate')
    expect(wrapper.text()).toContain('3 cards · 2 estimated')
    expect(wrapper.text()).toContain('1 missing estimates')
    expect(wrapper.get('[aria-label="Unassigned total"]').text()).toContain('0m known estimate')
    expect(wrapper.text()).toContain('Participant totals overlap')
    expect(wrapper.text()).toContain('Do not add these totals')
    expect(wrapper.text()).toContain('Parent and child estimates stay independent')
    expect(wrapper.get('time').attributes('datetime')).toBe(fixture.generatedAt)
    wrapper.unmount()
  })

  it('marks a mutation snapshot stale and explicitly refreshes it', async () => {
    const wrapper = mountPanel()
    await wrapper.get('button').trigger('click'); await flushPromises()
    useBoardStore().currentBoardCards = [{ id: 'new', boardId: 'board' } as Card]
    await flushPromises()
    expect(wrapper.text()).toContain('Board state changed')
    expect(estimateRollupsApi.get).toHaveBeenCalledTimes(1)
    await wrapper.findAll('button')[1]!.trigger('click'); await flushPromises()
    expect(wrapper.text()).not.toContain('Board state changed')
    expect(estimateRollupsApi.get).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })

  it('drops a response overtaken by a board mutation until explicit refresh', async () => {
    const pending = deferred<BoardEstimateRollup>()
    vi.mocked(estimateRollupsApi.get).mockReturnValueOnce(pending.promise)
    const wrapper = mountPanel()
    await wrapper.get('button').trigger('click')
    useBoardStore().currentBoardCards = [{ id: 'changed', boardId: 'board' } as Card]
    pending.resolve(fixture); await flushPromises()
    expect(wrapper.text()).toContain('Board state changed')
    expect(wrapper.find('time').exists()).toBe(false)
    wrapper.unmount()
  })

  it('clears data on account/token/board changes and ignores late responses', async () => {
    for (const transition of ['account', 'token', 'board']) {
      const pending = deferred<BoardEstimateRollup>()
      vi.mocked(estimateRollupsApi.get).mockReturnValueOnce(pending.promise)
      const wrapper = mountPanel()
      await wrapper.get('button').trigger('click')
      if (transition === 'account') useSessionStore().userId = 'someone-else'
      else if (transition === 'token') useSessionStore().token = 'new-session'
      else await wrapper.setProps({ boardId: 'different-board' })
      pending.resolve(fixture); await flushPromises()
      expect(wrapper.get('button').attributes('aria-expanded')).toBe('false')
      expect(wrapper.find('time').exists()).toBe(false)
      wrapper.unmount()
    }
  })

  it('shows errors, rejects a wrong-board response and offers refresh recovery', async () => {
    vi.mocked(estimateRollupsApi.get).mockRejectedValueOnce(new Error('Offline'))
      .mockResolvedValueOnce({ ...fixture, boardId: 'wrong' })
    const wrapper = mountPanel()
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    await wrapper.findAll('button')[1]!.trigger('click'); await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    expect(wrapper.find('time').exists()).toBe(false)
    await wrapper.findAll('button')[1]!.trigger('click'); await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(wrapper.find('time').exists()).toBe(true)
    wrapper.unmount()
  })

  it('renders the empty state and restores trigger focus on Escape', async () => {
    vi.mocked(estimateRollupsApi.get).mockResolvedValue({ ...fixture,
      board: { cardCount: 0, knownEstimateMinutes: 0, missingEstimateCount: 0 } })
    const wrapper = mountPanel()
    await wrapper.get('button').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('No active cards on this board')
    await wrapper.findAll('button')[1]!.trigger('keydown', { key: 'Escape' })
    expect(wrapper.get('button').attributes('aria-expanded')).toBe('false')
    expect(document.activeElement).toBe(wrapper.get('button').element)
    wrapper.unmount()
  })
})
