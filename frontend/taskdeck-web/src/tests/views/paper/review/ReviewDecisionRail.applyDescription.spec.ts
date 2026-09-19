import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewDecisionRail from '../../../../views/paper/review/ReviewDecisionRail.vue'

function mountRail(props: Record<string, unknown> = {}) {
  return mount(ReviewDecisionRail, {
    props: {
      summary: '1 operation · explicit review',
      ...props,
    } as never,
  })
}

describe('ReviewDecisionRail Apply-only recovery description (GH-2578)', () => {
  it('keeps a persistent unavailable note on enabled Apply without describing unrelated actions', () => {
    const wrapper = mountRail({
      busy: false,
      applyDescriptionIds: 'paper-review-evidence-unavailable-note',
    })

    expect(wrapper.get('[data-testid="decision-apply"]').attributes('aria-describedby'))
      .toBe('paper-review-evidence-unavailable-note')

    for (const testid of ['decision-reject', 'decision-edit', 'decision-defer']) {
      const control = wrapper.get(`[data-testid="${testid}"]`)
      expect(control.attributes('disabled')).toBeUndefined()
      expect(control.attributes('aria-describedby')).toBeUndefined()
    }
  })

  it('deduplicates an Apply-only note that is also part of a shared busy-state explanation', () => {
    const wrapper = mountRail({
      busy: true,
      decisionDescriptionIds: 'paper-review-revision-refresh-lock paper-review-evidence-unavailable-note',
      applyDescriptionIds: ' paper-review-evidence-unavailable-note  paper-review-evidence-unavailable-note ',
    })

    const expected = 'paper-review-revision-refresh-lock paper-review-evidence-unavailable-note'
    expect(wrapper.get('[data-testid="decision-apply"]').attributes('aria-describedby'))
      .toBe(expected)

    for (const testid of ['decision-reject', 'decision-edit', 'decision-defer']) {
      expect(wrapper.get(`[data-testid="${testid}"]`).attributes('aria-describedby'))
        .toBe(expected)
    }
  })

  it('preserves the Apply-only description when approval leaves only Apply visible', () => {
    const wrapper = mountRail({
      applyOnly: true,
      applyPhase: 'execute',
      applyDescriptionIds: 'paper-review-evidence-unavailable-note',
    })

    expect(wrapper.find('[data-testid="decision-reject"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="decision-apply"]').attributes('aria-describedby'))
      .toBe('paper-review-evidence-unavailable-note')
  })
})
