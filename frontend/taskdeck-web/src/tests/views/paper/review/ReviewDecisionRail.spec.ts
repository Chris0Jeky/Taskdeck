import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewDecisionRail from '../../../../views/paper/review/ReviewDecisionRail.vue'

function mountRail(
  props: Partial<{
    summary: string
    busy: boolean
    dismissable: boolean
    applyPhase: 'approve' | 'execute'
  }> = {},
) {
  return mount(ReviewDecisionRail, {
    props: {
      summary: '1 operation · explicit review · atomic apply',
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
    // Accessible name must CONTAIN the visible label "File away" (WCAG 2.5.3
    // Label in Name) — it reads "File away proposal", not "Dismiss proposal".
    expect(fileAway.text()).toContain('File away')
    expect(fileAway.attributes('aria-label')).toBe('File away proposal')
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

  // --- #1818: the two-phase apply must be legible on the rail itself ---

  describe('two-phase apply presentation', () => {
    it('phase 1 (pending) offers Approve and says the board is untouched', () => {
      const wrapper = mountRail()
      const apply = wrapper.find('[data-testid="decision-apply"]')
      expect(apply.text()).toContain('Approve')
      expect(apply.text()).not.toContain('Confirm apply')
      expect(apply.attributes('data-apply-phase')).toBe('approve')
      expect(apply.attributes('aria-label')).toContain('step 1 of 2')
      expect(apply.attributes('aria-label')).toContain('does not change the board')

      const hint = wrapper.find('[data-testid="decision-step-hint"]')
      expect(hint.text()).toContain('Step 1 of 2')
      expect(hint.text()).toContain('does not change the board')
      expect(wrapper.find('[role="toolbar"]').attributes('data-apply-phase')).toBe('approve')
    })

    it('phase 2 (approved) offers Confirm apply and names the board write', () => {
      const wrapper = mountRail({ applyPhase: 'execute' })
      const apply = wrapper.find('[data-testid="decision-apply"]')
      expect(apply.text()).toContain('Confirm apply')
      expect(apply.attributes('data-apply-phase')).toBe('execute')
      expect(apply.attributes('aria-label')).toContain('step 2 of 2')
      expect(apply.attributes('aria-label')).toContain('writes this change to the board')

      const hint = wrapper.find('[data-testid="decision-step-hint"]')
      expect(hint.text()).toContain('Step 2 of 2')
      expect(hint.text()).toContain('write it to the board')
      expect(wrapper.find('[role="toolbar"]').attributes('data-apply-phase')).toBe('execute')
    })

    it('renders approved-but-not-executed distinctly from pending', () => {
      const pending = mountRail()
      const approved = mountRail({ applyPhase: 'execute' })

      // The whole point of #1818: the two states must not look identical.
      expect(approved.find('[data-testid="decision-apply"]').text()).not.toBe(
        pending.find('[data-testid="decision-apply"]').text(),
      )
      expect(approved.find('[role="toolbar"]').attributes('data-apply-phase')).not.toBe(
        pending.find('[role="toolbar"]').attributes('data-apply-phase'),
      )
      expect(approved.find('[data-testid="decision-step-hint"]').text()).not.toBe(
        pending.find('[data-testid="decision-step-hint"]').text(),
      )
    })

    it('a settled proposal has no apply phase and no step hint', () => {
      const wrapper = mountRail({ dismissable: true, applyPhase: 'execute' })
      expect(wrapper.find('[role="toolbar"]').attributes('data-apply-phase')).toBe('settled')
      expect(wrapper.find('[data-testid="decision-step-hint"]').exists()).toBe(false)
    })

    it('still emits a single apply event in the execute phase', async () => {
      const wrapper = mountRail({ applyPhase: 'execute' })
      await wrapper.find('[data-testid="decision-apply"]').trigger('click')
      expect(wrapper.emitted('apply')).toHaveLength(1)
    })
  })
})
