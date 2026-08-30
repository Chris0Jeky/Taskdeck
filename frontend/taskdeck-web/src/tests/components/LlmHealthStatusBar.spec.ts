import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import LlmHealthStatusBar from '../../components/chat/LlmHealthStatusBar.vue'
import type { ChatProviderHealth } from '../../types/chat'

function health(overrides: Partial<ChatProviderHealth> = {}): ChatProviderHealth {
  return {
    isAvailable: true,
    providerName: 'Mock',
    errorMessage: null,
    model: 'mock-default',
    isMock: true,
    isProbed: false,
    verificationStatus: 'unverified',
    ...overrides,
  }
}

function mountBar(chatHealth: ChatProviderHealth | null) {
  return mount(LlmHealthStatusBar, {
    props: {
      chatHealth,
      loadingHealth: false,
      chatHealthLoadError: null,
    },
  })
}

describe('LlmHealthStatusBar retired-configuration notice (#2233)', () => {
  it('stays silent when no retired configuration was ignored', () => {
    const wrapper = mountBar(health())

    expect(wrapper.find('[data-testid="llm-retired-configuration-ignored"]').exists()).toBe(false)
  })

  it('stays silent when the server omits the flag entirely', () => {
    const wrapper = mountBar(health({ retiredProviderConfigurationIgnored: undefined }))

    expect(wrapper.find('[data-testid="llm-retired-configuration-ignored"]').exists()).toBe(false)
  })

  it('explains that retired settings were found and ignored', () => {
    const wrapper = mountBar(health({ retiredProviderConfigurationIgnored: true }))

    const note = wrapper.find('[data-testid="llm-retired-configuration-ignored"]')
    expect(note.exists()).toBe(true)
    expect(note.text()).toContain('ignored')
    expect(note.text()).toContain('built-in provider')
  })
})
