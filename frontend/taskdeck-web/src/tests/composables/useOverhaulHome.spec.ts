import { mount } from '@vue/test-utils'
import { defineComponent, reactive } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useOverhaulHome } from '../../composables/useOverhaulHome'

const mocks = vi.hoisted(() => ({ createItem: vi.fn(), fetchTodaySummary: vi.fn(), fetchHomeSummary: vi.fn() }))
const workspace = reactive({ homeSummary: null, todaySummary: null as unknown, ...mocks })
vi.mock('../../store/workspaceStore', () => ({ useWorkspaceStore: () => workspace }))
vi.mock('../../store/captureStore', () => ({ useCaptureStore: () => mocks }))
vi.mock('../../composables/useErrorMapper', () => ({ getErrorDisplay: () => ({ message: 'Save unavailable' }) }))

function setup() {
  let model!: ReturnType<typeof useOverhaulHome>
  const wrapper = mount(defineComponent({ setup() { model = useOverhaulHome(); return () => null } }))
  return { model, wrapper }
}

describe('overhaul home uses shared work and preserves capture input', () => {
  beforeEach(() => { vi.resetAllMocks(); workspace.todaySummary = null; mocks.fetchTodaySummary.mockResolvedValue(undefined) })
  it('saves the exact original without a board or automatic triage', async () => {
    mocks.createItem.mockResolvedValue({ id: 'saved' })
    const { model, wrapper } = setup()
    model.captureText.value = '  Keep this unfinished thought.\n'
    await model.saveCapture()
    expect(mocks.createItem).toHaveBeenCalledExactlyOnceWith({ text: '  Keep this unfinished thought.\n', boardId: null, source: 'Typed' })
    expect(model.captureText.value).toBe('')
    expect(model.captureSaved.value).toBe(true)
    wrapper.unmount()
  })
  it('keeps a newer draft and rejects duplicate submission during an in-flight save', async () => {
    let finish!: () => void
    mocks.createItem.mockImplementation(() => new Promise<void>(resolve => { finish = resolve }))
    const { model, wrapper } = setup()
    model.captureText.value = 'original'
    const pending = model.saveCapture()
    model.captureText.value = 'a new thought'
    await model.saveCapture()
    expect(mocks.createItem).toHaveBeenCalledTimes(1)
    finish(); await pending
    expect(model.captureText.value).toBe('a new thought')
    wrapper.unmount()
  })
  it('retains input when persistence fails and shows no saved receipt', async () => {
    mocks.createItem.mockRejectedValue(new Error('offline'))
    const { model, wrapper } = setup()
    model.captureText.value = 'do not lose this'
    await model.saveCapture()
    expect(model.captureText.value).toBe('do not lose this')
    expect(model.captureError.value).toBe('Save unavailable')
    expect(model.captureSaved.value).toBe(false)
    wrapper.unmount()
  })
  it('deduplicates cards appearing in multiple agenda groups by stable identity', () => {
    const card = { cardId: 'one', boardId: 'board', title: 'First' }
    workspace.todaySummary = { blockedCards: [card], dueTodayCards: [card], overdueCards: [{ ...card, cardId: 'two' }] }
    const { model, wrapper } = setup()
    expect(model.agenda.value.map(item => item.cardId)).toEqual(['one', 'two'])
    wrapper.unmount()
  })
})
