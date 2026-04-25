import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import InboxView from '../../views/InboxView.vue'

const paperTheme = vi.hoisted(() => ({ isOn: true }))

vi.mock('../../store/paperThemeStore', () => ({
  usePaperThemeStore: () => paperTheme,
}))

const PaperInboxViewStub = defineComponent({
  name: 'PaperInboxView',
  setup: () => () => h('div', { 'data-testid': 'paper-inbox-stub' }, 'paper inbox'),
})

const LegacyInboxViewStub = defineComponent({
  name: 'LegacyInboxView',
  setup: () => () => h('div', { 'data-testid': 'legacy-inbox-stub' }, 'legacy inbox'),
})

function mountView() {
  return mount(InboxView, {
    global: {
      stubs: {
        PaperInboxView: PaperInboxViewStub,
        LegacyInboxView: LegacyInboxViewStub,
      },
    },
  })
}

describe('InboxView paper mode selection', () => {
  it('mounts only the paper inbox branch when paper mode is on', () => {
    paperTheme.isOn = true
    const wrapper = mountView()

    expect(wrapper.find('[data-testid="paper-inbox-stub"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="legacy-inbox-stub"]').exists()).toBe(false)
  })

  it('mounts only the legacy inbox branch when paper mode is off', () => {
    paperTheme.isOn = false
    const wrapper = mountView()

    expect(wrapper.find('[data-testid="paper-inbox-stub"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="legacy-inbox-stub"]').exists()).toBe(true)
  })
})
