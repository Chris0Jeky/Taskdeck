import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import ThinkingWorkspaceView from '../../views/overhaul/ThinkingWorkspaceView.vue'

const navigation = vi.hoisted(() => ({ leave: null as null | (() => boolean | Promise<boolean>) }))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { boardId: 'b1', cardId: 'c1' }, query: {} }),
  onBeforeRouteLeave: (guard: () => boolean | Promise<boolean>) => { navigation.leave = guard },
  onBeforeRouteUpdate: vi.fn(),
}))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn().mockResolvedValue({ id: 'b1', name: 'Board' }) } }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: { getCards: vi.fn().mockResolvedValue([{ id: 'c1', title: 'Card' }]) } }))
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => ({ isDemo: false }) }))
vi.mock('../../utils/demoMode', () => ({ isDemoMode: false }))
vi.mock('../../views/AutomationChatView.vue', () => ({ __esModule: true, default: defineComponent({
  name: 'AutomationChatView', props: ['thinkingDirty'], emits: ['dirty-change', 'sending-change'], template: '<div>Companion</div>',
}) }))
async function setup() {
  const wrapper = mount(ThinkingWorkspaceView, { global: { stubs: {
    RouterLink: { template: '<a><slot /></a>' },
    ThinkingDeckPanel: defineComponent({ name: 'ThinkingDeckPanel', emits: ['dirty-change'], template: '<div>Thinking</div>' }),
    TdDialog: { props: ['open', 'title', 'description'], template: '<section v-if="open" role="dialog"><h2>{{ title }}</h2><p>{{ description }}</p><slot name="footer" /></section>' },
  } } })
  await flushPromises()
  await wrapper.findAll('button').find(button => button.text() === 'Open card companion')!.trigger('click')
  await flushPromises()
  return wrapper
}
describe('thinking workspace continuity', () => {
  beforeEach(() => { navigation.leave = null })
  it('passes unsaved thinking state to the companion and releases it after save', async () => {
    const wrapper = await setup()
    const thinking = wrapper.findComponent({ name: 'ThinkingDeckPanel' })
    thinking.vm.$emit('dirty-change', true); await flushPromises()
    expect(wrapper.findComponent({ name: 'AutomationChatView' }).props('thinkingDirty')).toBe(true)
    thinking.vm.$emit('dirty-change', false); await flushPromises()
    expect(wrapper.findComponent({ name: 'AutomationChatView' }).props('thinkingDirty')).toBe(false)
    wrapper.unmount()
  })
  it('holds in-app navigation while sending, then permits leaving without claiming to discard the sent turn', async () => {
    const wrapper = await setup()
    const companion = wrapper.findComponent({ name: 'AutomationChatView' })
    companion.vm.$emit('sending-change', true); await flushPromises()
    const leaving = navigation.leave!()
    await flushPromises()
    const leaveButton = wrapper.get('[role="dialog"]').findAll('button')[1]!
    expect(leaveButton.attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('Closing the browser does not cancel')
    await leaveButton.trigger('click')
    expect(wrapper.find('[role="dialog"]').exists()).toBe(true)
    companion.vm.$emit('sending-change', false); await flushPromises()
    expect(leaveButton.attributes('disabled')).toBeUndefined()
    expect(leaveButton.text()).toBe('Leave thinking space')
    await leaveButton.trigger('click')
    expect(await leaving).toBe(true)
    wrapper.unmount()
  })
})
