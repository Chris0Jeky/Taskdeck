import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewDecisionRail from '../../../../views/paper/review/ReviewDecisionRail.vue'

function mountRail(props: Partial<{ summary: string; busy: boolean; dismissable: boolean }> = {}) {
  return mount(ReviewDecisionRail, {
    props: {
      summary: '1 operation · undo 6h · atomic',
      ...props,
    },
  })
}

describe('ReviewDecisionRail', () => {
  it('renders the four decision actions in the default (actionable) state', () => {
    const wrapper = mountRail()
    expect(wrapper.find('[data-testid="decision-reject"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="decision-edit"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="decision-defer"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="decision-file-away"]').exists()).toBe(false)
    expect(wrapper.find('[role="toolbar"]').attributes('aria-label')).toBe('Decision actions')
    expect(wrapper.text()).toContain('DECISION')
  })

  it('emits each decision event when its button is clicked', async () => {
    const wrapper = mountRail()
    await wrapper.find('[data-testid="decision-apply"]').trigger('click')
    await wrapper.find('[data-testid="decision-reject"]').trigger('click')
    await wrapper.find('[data-testid="decision-edit"]').trigger('click')
    await wrapper.find('[data-testid="decision-defer"]').trigger('click')
    expect(wrapper.emitted('apply')).toHaveLength(1)
    expect(wrapper.emitted('reject')).toHaveLength(1)
    expect(wrapper.emitted('request-edit')).toHaveLength(1)
    expect(wrapper.emitted('defer')).toHaveLength(1)
  })

  it('becomes a filing rail with a single "File away" button when dismissable', () => {
    const wrapper = mountRail({ dismissable: true })
    expect(wrapper.find('[data-testid="decision-reject"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-edit"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-defer"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="decision-apply"]').exists()).toBe(false)

    const fileAway = wrapper.find('[data-testid="decision-file-away"]')
    expect(fileAway.exists()).toBe(true)
    // Visible label stays in the paper voice; accessible name matches the
    // backend/API dismiss vocabulary so tests and a11y tooling agree.
    expect(fileAway.text()).toContain('File away')
    expect(fileAway.attributes('aria-label')).toBe('Dismiss proposal')
    expect(wrapper.find('[role="toolbar"]').attributes('aria-label')).toBe('Filing actions')
    expect(wrapper.text()).toContain('SETTLED')
  })

  it('emits dismiss when File away is clicked', async () => {
    const wrapper = mountRail({ dismissable: true })
    await wrapper.find('[data-testid="decision-file-away"]').trigger('click')
    expect(wrapper.emitted('dismiss')).toHaveLength(1)
  })

  it('disables the File away button while a network action is in flight', () => {
    const wrapper = mountRail({ dismissable: true, busy: true })
    expect(wrapper.find('[data-testid="decision-file-away"]').attributes('disabled')).toBeDefined()
  })
})
