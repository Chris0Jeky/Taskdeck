import { createPinia, setActivePinia } from 'pinia'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'
import WorkspacePlanView from '../../views/overhaul/WorkspacePlanView.vue'
import PersonalPlanResume from '../../components/workspace/PersonalPlanResume.vue'
import { workspacePlanApi } from '../../api/workspacePlanApi'
import { boardsApi } from '../../api/boardsApi'

vi.mock('../../utils/demoMode', () => ({ isDemoMode: true }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => ({ userId: 'demo-user', isDemo: true }) }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('../../api/workspacePlanApi', () => ({ workspacePlanApi: { get: vi.fn(), save: vi.fn(), focus: vi.fn() } }))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoardsPaginated: vi.fn() } }))
const global = { stubs: { RouterLink: { template: '<a><slot /></a>' } } }
beforeEach(() => { vi.clearAllMocks(); setActivePinia(createPinia()) })
it('explains a direct demo route without fetching the plan or card choices', async () => {
  const wrapper = mount(WorkspacePlanView, { global })
  await flushPromises()
  expect(wrapper.get('[role="status"]').text()).toContain('preview has no backend')
  expect(wrapper.find('form').exists()).toBe(false)
  expect(workspacePlanApi.get).not.toHaveBeenCalled()
  expect(boardsApi.getBoardsPaginated).not.toHaveBeenCalled()
})
it('hides Home continuation without sending a request', async () => {
  const wrapper = mount(PersonalPlanResume, { global })
  await flushPromises()
  expect(wrapper.find('[aria-label="Personal continuity"]').exists()).toBe(false)
  expect(workspacePlanApi.get).not.toHaveBeenCalled()
})
