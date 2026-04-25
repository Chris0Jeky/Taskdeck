import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperEmptyState from '../../../components/paper/PaperEmptyState.vue'

describe('PaperEmptyState', () => {
  it('renders title and body via slots', () => {
    const wrapper = mount(PaperEmptyState, {
      slots: {
        title: 'Inbox is clean',
        default: 'Nothing waits to be triaged.',
      },
    })
    expect(wrapper.find('.paper-empty-state__title').text()).toBe('Inbox is clean')
    expect(wrapper.find('.paper-empty-state__copy').text()).toBe('Nothing waits to be triaged.')
  })

  it('renders the cta slot when provided', () => {
    const wrapper = mount(PaperEmptyState, {
      slots: {
        title: 'No boards yet',
        cta: '<button class="probe">Start one</button>',
      },
    })
    expect(wrapper.find('.paper-empty-state__cta').exists()).toBe(true)
    expect(wrapper.find('button.probe').exists()).toBe(true)
  })

  it('omits the cta wrapper when no cta slot is given', () => {
    const wrapper = mount(PaperEmptyState, {
      slots: { title: 'Quiet' },
    })
    expect(wrapper.find('.paper-empty-state__cta').exists()).toBe(false)
  })

  it('applies the neutral tone class by default', () => {
    const wrapper = mount(PaperEmptyState, { slots: { title: 'x' } })
    expect(wrapper.classes()).toContain('paper-empty-state--neutral')
    expect(wrapper.attributes('data-tone')).toBe('neutral')
  })

  it('applies the ember tone class when tone="ember"', () => {
    const wrapper = mount(PaperEmptyState, {
      props: { tone: 'ember' },
      slots: { title: 'x' },
    })
    expect(wrapper.classes()).toContain('paper-empty-state--ember')
    expect(wrapper.attributes('data-tone')).toBe('ember')
  })

  it('uses the provided mark glyph and falls back to · when omitted', () => {
    const wrapper = mount(PaperEmptyState, {
      props: { mark: '✎' },
      slots: { title: 'x' },
    })
    expect(wrapper.find('.paper-empty-state__mark').text()).toBe('✎')

    const fallback = mount(PaperEmptyState, { slots: { title: 'x' } })
    expect(fallback.find('.paper-empty-state__mark').text()).toBe('·')
  })
})
