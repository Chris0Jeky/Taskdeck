import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PaperTodayView from '../../../../views/paper/PaperTodayView.vue'

const mockWorkspaceStore = {
  todaySummary: null,
}
const mockSessionStore = {
  userId: 'user-1' as string | null,
}

vi.mock('../../../../store/workspaceStore', () => ({
  useWorkspaceStore: () => mockWorkspaceStore,
}))

vi.mock('../../../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))

describe('PaperTodayView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    mockSessionStore.userId = 'user-1'
  })

  it('smoke renders the dossier with all 9 sections present', () => {
    const wrapper = mount(PaperTodayView)

    expect(wrapper.find('[data-paper-today]').exists()).toBe(true)

    // Sections we expect to land on the page (exposed via data-section)
    const expected = [
      'cover',
      'stats',
      'cadence',
      'ledger',
      'decisions',
      'boards',
      'carry-over',
      'streak',
      'line-for-tomorrow',
    ]
    for (const section of expected) {
      expect(wrapper.find(`[data-section="${section}"]`).exists()).toBe(true)
    }
  })

  it('renders the dossier serial in the cover and footer', () => {
    const wrapper = mount(PaperTodayView)
    const serial = wrapper.find('[data-testid="dossier-serial"]').text()
    expect(serial).toMatch(/^D-\d{4}-\d{2}-\d{2}-\d{3}$/)
    // Footer also surfaces the same serial token
    expect(wrapper.text()).toContain(serial)
  })

  it('scopes line-for-tomorrow storage by user and dossier date', () => {
    const today = new Date().toISOString().slice(0, 10)
    localStorage.setItem(`td.paper.line-for-tomorrow:user-1:${today}`, 'user-one note')
    localStorage.setItem(`td.paper.line-for-tomorrow:user-2:${today}`, 'user-two note')

    const wrapper = mount(PaperTodayView)
    const input = wrapper.find<HTMLTextAreaElement>('[data-testid="line-for-tomorrow-input"]')

    expect(input.element.value).toBe('user-one note')
    expect(input.element.value).not.toBe('user-two note')
  })

  it('does not advertise unimplemented global shortcuts in the footer', () => {
    const wrapper = mount(PaperTodayView)

    expect(wrapper.text()).not.toContain('PRESS S TO SEAL')
    expect(wrapper.text()).not.toContain('⌘L FOR LEDGER')
    expect(wrapper.text()).toContain('SEAL ABOVE')
  })
})
