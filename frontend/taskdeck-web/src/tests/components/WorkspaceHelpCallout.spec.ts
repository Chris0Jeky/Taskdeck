import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { computed, ref } from 'vue'
import WorkspaceHelpCallout from '../../components/workspace/WorkspaceHelpCallout.vue'

const visibleRef = ref(true)
const mockDismiss = vi.fn()
const mockReplay = vi.fn()

vi.mock('../../composables/useWorkspaceHelp', () => ({
  useWorkspaceHelp: () => ({
    isVisible: computed(() => visibleRef.value),
    isDismissed: computed(() => !visibleRef.value),
    dismiss: mockDismiss,
    replay: mockReplay,
  }),
}))

describe('WorkspaceHelpCallout', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    visibleRef.value = true
  })

  it('renders title, description, and eyebrow when visible', () => {
    const wrapper = mount(WorkspaceHelpCallout, {
      props: {
        topic: 'home',
        title: 'Welcome to Home',
        description: 'This is your starting point.',
      },
    })
    expect(wrapper.text()).toContain('What is this?')
    expect(wrapper.text()).toContain('Welcome to Home')
    expect(wrapper.text()).toContain('This is your starting point.')
  })

  it('uses custom eyebrow text', () => {
    const wrapper = mount(WorkspaceHelpCallout, {
      props: {
        topic: 'today',
        title: 'Today',
        description: 'desc',
        eyebrow: 'Quick start',
      },
    })
    expect(wrapper.find('.td-help-callout__eyebrow').text()).toBe('Quick start')
  })

  it('calls dismiss when dismiss button is clicked', async () => {
    const wrapper = mount(WorkspaceHelpCallout, {
      props: {
        topic: 'home',
        title: 'Title',
        description: 'Desc',
      },
    })
    const dismissBtn = wrapper.findAll('button').find((b) => b.text().includes('Hide this guide'))
    expect(dismissBtn).toBeTruthy()
    await dismissBtn?.trigger('click')
    expect(mockDismiss).toHaveBeenCalledTimes(1)
  })

  it('shows dismissed state with replay button when not visible', () => {
    visibleRef.value = false
    const wrapper = mount(WorkspaceHelpCallout, {
      props: {
        topic: 'inbox',
        title: 'Inbox Guide',
        description: 'Learn about inbox.',
      },
    })
    expect(wrapper.text()).toContain('This page guide is hidden.')
    expect(wrapper.text()).toContain('Show page guide')
  })

  it('calls replay when replay button is clicked in dismissed state', async () => {
    visibleRef.value = false
    const wrapper = mount(WorkspaceHelpCallout, {
      props: {
        topic: 'review',
        title: 'Review Guide',
        description: 'Learn about review.',
      },
    })
    const replayBtn = wrapper.findAll('button').find((b) => b.text().includes('Show page guide'))
    expect(replayBtn).toBeTruthy()
    await replayBtn?.trigger('click')
    expect(mockReplay).toHaveBeenCalledTimes(1)
  })

  it('renders default slot content when visible', () => {
    const wrapper = mount(WorkspaceHelpCallout, {
      props: { topic: 'home', title: 'Title', description: 'Desc' },
      slots: { default: '<p>Extra help content</p>' },
    })
    expect(wrapper.find('.td-help-callout__body').exists()).toBe(true)
    expect(wrapper.text()).toContain('Extra help content')
  })

  it('renders actions slot when visible', () => {
    const wrapper = mount(WorkspaceHelpCallout, {
      props: { topic: 'home', title: 'Title', description: 'Desc' },
      slots: { actions: '<button>Get Started</button>' },
    })
    expect(wrapper.find('.td-help-callout__actions').exists()).toBe(true)
    expect(wrapper.text()).toContain('Get Started')
  })

  it('uses custom dismiss label when visible', () => {
    visibleRef.value = true
    const wrapper = mount(WorkspaceHelpCallout, {
      props: {
        topic: 'board',
        title: 'Board',
        description: 'Desc',
        dismissLabel: 'Got it',
      },
    })
    const dismissBtn = wrapper.findAll('button').find((b) => b.text().includes('Got it'))
    expect(dismissBtn).toBeTruthy()
  })

  it('uses custom replay label when dismissed', () => {
    visibleRef.value = false
    const wrapper = mount(WorkspaceHelpCallout, {
      props: {
        topic: 'board',
        title: 'Board',
        description: 'Desc',
        replayLabel: 'Bring it back',
      },
    })
    const replayBtn = wrapper.findAll('button').find((b) => b.text().includes('Bring it back'))
    expect(replayBtn).toBeTruthy()
  })

  it('sets data-help-topic attribute on root element', () => {
    const wrapper = mount(WorkspaceHelpCallout, {
      props: { topic: 'today', title: 'Today', description: 'Desc' },
    })
    expect(wrapper.find('[data-help-topic="today"]').exists()).toBe(true)
  })
})
