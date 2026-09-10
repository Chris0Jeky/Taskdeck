import { defineComponent, h, nextTick, reactive, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { usePersonalPlanFocus } from '../../composables/usePersonalPlanFocus'

const session = reactive({ userId: 'owner' })
const plan = reactive({ available: true, ready: true, loading: false, saving: false, focus: vi.fn() })
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
vi.mock('../../store/workspacePlanStore', () => ({ useWorkspacePlanStore: () => plan }))
const card = { boardId: 'board', cardId: 'card', available: true, title: 'Thread', boardName: 'Board', columnName: null, dueDate: null, isBlocked: false, blockReason: null }
const destination = '/workspace/boards/board/cards/card/thinking?focus=1'
async function setup() {
  const component = defineComponent({ template: '<div />' })
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/:pathMatch(.*)*', component }] })
  await router.push('/workspace/plan')
  let focus!: ReturnType<typeof usePersonalPlanFocus>
  const attached = ref(true)
  const child = defineComponent({ setup() { focus = usePersonalPlanFocus(); return {} }, template: '<div />' })
  const wrapper = mount(defineComponent({ setup: () => () => attached.value ? h(child) : h('div') }), { global: { plugins: [router] } })
  return { router, wrapper, focus, detach: async () => { attached.value = false; await nextTick() } }
}

describe('accepted personal focus navigation', () => {
  beforeEach(() => { vi.clearAllMocks(); session.userId = 'owner'; plan.ready = true; plan.loading = plan.saving = false; plan.focus.mockResolvedValue(true) })
  it('records only after the destination is accepted, even as the originating component unmounts', async () => {
    const { router, wrapper, focus, detach } = await setup()
    let release!: () => void
    const waiting = new Promise<void>(resolve => { release = resolve })
    router.beforeEach(async () => { await waiting })
    const pending = focus.openFocus(card)
    expect(plan.focus).not.toHaveBeenCalled()
    expect(focus.opening.value).toBe(true)
    await detach()
    release()
    expect(await pending).toBe(true)
    expect(router.currentRoute.value.fullPath).toBe(destination)
    expect(plan.focus).toHaveBeenCalledExactlyOnceWith('board', 'card')
    wrapper.unmount()
  })
  it.each(['abort', 'redirect', 'account'] as const)('does not record after an %s guard outcome', async outcome => {
    const { router, wrapper, focus } = await setup()
    router.beforeEach(to => {
      if (!to.path.endsWith('/thinking')) return
      if (outcome === 'abort') return false
      if (outcome === 'redirect') return '/login'
      session.userId = 'other'
    })
    expect(await focus.openFocus(card)).toBe(false)
    expect(plan.focus).not.toHaveBeenCalled()
    wrapper.unmount()
  })
  it('does not pull the user back after leaving during the Focus save', async () => {
    const { router, wrapper, focus } = await setup()
    let settle!: (saved: boolean) => void
    let started!: () => void
    const saving = new Promise<void>(resolve => { started = resolve })
    plan.focus.mockImplementationOnce(() => { started(); return new Promise<boolean>(resolve => { settle = resolve }) })
    const pending = focus.openFocus(card)
    await saving
    expect(router.currentRoute.value.fullPath).toBe(destination)
    await router.push('/workspace/boards')
    settle(true)
    expect(await pending).toBe(true)
    expect(router.currentRoute.value.fullPath).toBe('/workspace/boards')
    wrapper.unmount()
  })
  it('does not navigate until uncertain state has been refreshed', async () => {
    const { router, wrapper, focus } = await setup()
    plan.ready = false
    expect(await focus.openFocus(card)).toBe(false)
    expect(router.currentRoute.value.fullPath).toBe('/workspace/plan')
    expect(plan.focus).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})
