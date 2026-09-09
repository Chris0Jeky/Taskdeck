import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import ThinkingWorkspaceView from '../../views/overhaul/ThinkingWorkspaceView.vue'
import { useSessionStore } from '../../store/sessionStore'

const navigation = vi.hoisted(() => ({ leave: null as null | (() => boolean | Promise<boolean>) }))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { boardId: 'b1', cardId: 'c1' }, query: {} }),
  onBeforeRouteLeave: (guard: () => boolean | Promise<boolean>) => { navigation.leave = guard },
  onBeforeRouteUpdate: vi.fn(),
}))
vi.mock('../../api/boardsApi', () => ({ boardsApi: { getBoard: vi.fn().mockResolvedValue({ id: 'b1', name: 'Board' }) } }))
vi.mock('../../api/cardsApi', () => ({ cardsApi: { getCards: vi.fn().mockResolvedValue([{ id: 'c1', title: 'Card' }]) } }))
vi.mock('../../store/sessionStore', async () => {
  const { reactive } = await import('vue')
  const session = reactive({ isDemo: false, userId: 'owner', token: 'first' })
  return { useSessionStore: () => session }
})
vi.mock('../../utils/demoMode', () => ({ isDemoMode: false }))
vi.mock('../../views/AutomationChatView.vue', () => ({ __esModule: true, default: defineComponent({
  name: 'AutomationChatView', props: ['thinkingDirty'], emits: ['dirty-change', 'sending-change'], template: '<div>Companion</div>',
}) }))
async function setup() {
  const wrapper = mount(ThinkingWorkspaceView, { global: { stubs: {
    RouterLink: { template: '<a><slot /></a>' },
    ThinkingDeckPanel: defineComponent({ name: 'ThinkingDeckPanel', emits: ['dirty-change', 'busy'], template: '<div>Thinking</div>' }),
    TdDialog: { props: ['open', 'title', 'description'], template: '<section v-if="open" role="dialog"><h2>{{ title }}</h2><p>{{ description }}</p><slot name="footer" /></section>' },
  } } })
  await flushPromises()
  await wrapper.findAll('button').find(button => button.text() === 'Open card companion')!.trigger('click')
  await flushPromises()
  return wrapper
}
describe('thinking workspace continuity', () => {
  beforeEach(() => { navigation.leave = null; useSessionStore().userId = 'owner'; useSessionStore().token = 'first' })
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
  it('holds navigation for private recording or saving until its busy receipt clears', async () => {
    const wrapper = await setup()
    const thinking = wrapper.findComponent({ name: 'ThinkingDeckPanel' })
    thinking.vm.$emit('busy', true); await flushPromises()
    const leaving = navigation.leave!(); await flushPromises()
    expect(wrapper.text()).toContain('Your private answer is still in progress')
    const leaveButton = wrapper.get('[role="dialog"]').findAll('button')[1]!
    expect(leaveButton.attributes('disabled')).toBeDefined()
    thinking.vm.$emit('busy', false); await flushPromises()
    await leaveButton.trigger('click'); expect(await leaving).toBe(true)
    wrapper.unmount()
  })
  it('retains drafts through same-user token refresh and resets them when the actor changes', async () => {
    const wrapper = await setup()
    const thinking = wrapper.findComponent({ name: 'ThinkingDeckPanel' }).vm
    thinking.$emit('dirty-change', true); await flushPromises()
    useSessionStore().token = 'refreshed'; await flushPromises()
    expect(wrapper.findComponent({ name: 'ThinkingDeckPanel' }).vm).toBe(thinking)
    expect(wrapper.findComponent({ name: 'AutomationChatView' }).props('thinkingDirty')).toBe(true)
    useSessionStore().userId = 'different-owner'; await flushPromises()
    expect(wrapper.findComponent({ name: 'ThinkingDeckPanel' }).vm).not.toBe(thinking)
    expect(wrapper.findComponent({ name: 'AutomationChatView' }).exists()).toBe(false)
    wrapper.unmount()
  })
})
