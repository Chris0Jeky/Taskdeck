import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewAuthorCard from '../../../../views/paper/review/ReviewAuthorCard.vue'
import type {
  ConfidenceBreakdown,
  ConfidenceValueSource,
} from '../../../../composables/usePaperReviewSelectors'

/**
 * ReviewAuthorCard — the confidence SOURCE sentence must be readable before the
 * disclosure opens (#1940, the retained residual of #2166).
 *
 * Why this one sentence matters more than it looks: on an Applied record the
 * primary confidence-source badge in ReviewMain.vue is gated off, and
 * PaperReviewView passes an empty `authorMeta` for the deterministic and
 * not-reported sources. The sentence inside this collapsed region was then the
 * only statement on the whole screen about where the confidence number came
 * from — and it was invisible until the reviewer opened a control that gave no
 * reason to be opened.
 *
 * The disclosure itself stays in every state (the view specs and the required
 * E2E smoke drive it), so the fix hoists the sentence rather than moving the
 * control.
 */

function breakdownOf(
  source: ConfidenceValueSource,
  components: Array<{ key: string; value: number }> = [],
  note?: string,
): ConfidenceBreakdown {
  return { overall: components.length > 0 ? 0.9 : null, components, threshold: null, source, note }
}

function mountCard(breakdown: ConfidenceBreakdown, authorMeta = '') {
  return mount(ReviewAuthorCard, {
    attachTo: document.body,
    props: {
      authorName: 'Taskdeck',
      authorMeta,
      proposedDate: '2026-08-22',
      proposedTime: '18:00',
      proposedNum: '001',
      breakdown,
    },
  })
}

const sourceLine = '[data-testid="paper-review-author-confidence-source"]'

describe('ReviewAuthorCard', () => {
  describe('confidence source, stated before the disclosure opens', () => {
    // The two sources the backend actually emits with an empty components
    // array. Their wording is unchanged by #1940 — only its position is.
    const CASES: Array<[string, ConfidenceValueSource, string]> = [
      ['deterministic extraction', 'deterministic', 'Deterministic extraction · no model confidence'],
      ['a model that reported nothing', 'not-reported', 'No model confidence reported'],
      ['a derived average', 'derived', 'No model confidence reported'],
    ]

    it.each(CASES)('names %s at first paint, outside the collapsed region', (_label, source, copy) => {
      const wrapper = mountCard(breakdownOf(source))
      const line = wrapper.get(sourceLine)
      const details = wrapper.get('[data-testid="paper-review-confidence-details"]')

      expect(line.text()).toBe(copy)
      expect(line.isVisible()).toBe(true)
      expect(details.find(sourceLine).exists()).toBe(false)
      expect(details.isVisible()).toBe(false)

      wrapper.unmount()
    })

    it('says it exactly once, before and after the region opens', async () => {
      const wrapper = mountCard(breakdownOf('deterministic'))
      const copy = 'Deterministic extraction · no model confidence'

      expect(wrapper.text().split(copy).length - 1).toBe(1)

      await wrapper.get('[data-testid="paper-review-confidence-disclosure"]').trigger('click')

      expect(wrapper.text().split(copy).length - 1).toBe(1)
      expect(wrapper.get('[data-testid="paper-review-confidence-details"]').text()).not.toContain(copy)

      wrapper.unmount()
    })

    it('drops the sentence entirely once there are per-component bars to read', async () => {
      const wrapper = mountCard(
        breakdownOf('model-reported', [
          { key: 'Operation 1: create card', value: 0.92 },
          { key: 'Operation 2: update card', value: 0.4 },
        ]),
        '0.90 model-reported average',
      )
      const details = wrapper.get('[data-testid="paper-review-confidence-details"]')

      expect(wrapper.find(sourceLine).exists()).toBe(false)
      expect(details.isVisible()).toBe(false)

      await wrapper.get('[data-testid="paper-review-confidence-disclosure"]').trigger('click')

      expect(details.isVisible()).toBe(true)
      expect(details.get('.paper-review-author__bd-heading').text()).toBe(
        'Model-reported item confidence',
      )
      expect(details.findAll('.paper-review-author__bar-key').map((n) => n.text())).toEqual([
        'Operation 1: create card',
        'Operation 2: update card',
      ])

      wrapper.unmount()
    })

    // #1940: `model-reported` with an empty components array made the heading
    // announce "Model-reported item confidence" directly above a body saying
    // no model confidence was reported. The backend never emits that pair —
    // PaperReviewView.spec.ts builds it — but the heading is derived from what
    // is actually on screen now, so the contradiction cannot be constructed.
    it('derives the heading from the bars it has, not from the claimed source', async () => {
      const wrapper = mountCard(breakdownOf('model-reported'))
      const details = wrapper.get('[data-testid="paper-review-confidence-details"]')

      expect(wrapper.get(sourceLine).text()).toBe('No model confidence reported')

      await wrapper.get('[data-testid="paper-review-confidence-disclosure"]').trigger('click')

      const heading = details.get('.paper-review-author__bd-heading').text()
      expect(heading).toBe('Confidence source')
      expect(heading).not.toBe('Model-reported item confidence')
      expect(details.findAll('.paper-review-author__bar')).toHaveLength(0)

      wrapper.unmount()
    })

    it('keeps the model note behind the disclosure', async () => {
      const wrapper = mountCard(
        breakdownOf('model-reported', [{ key: 'Operation 1: create card', value: 0.96 }], 'Reported by the model for the proposed operation.'),
      )
      const details = wrapper.get('[data-testid="paper-review-confidence-details"]')

      expect(details.isVisible()).toBe(false)

      await wrapper.get('[data-testid="paper-review-confidence-disclosure"]').trigger('click')

      expect(details.text()).toContain('Reported by the model for the proposed operation.')

      wrapper.unmount()
    })
  })

  it('keeps the disclosure present and correctly paired while collapsed', () => {
    const wrapper = mountCard(breakdownOf('deterministic'))
    const button = wrapper.get('[data-testid="paper-review-confidence-disclosure"]')
    const details = wrapper.get('[data-testid="paper-review-confidence-details"]')

    expect(button.element.tagName).toBe('BUTTON')
    expect(button.attributes('type')).toBe('button')
    expect(button.attributes('aria-expanded')).toBe('false')
    expect(button.attributes('aria-controls')).toBe(details.attributes('id'))
    expect(details.attributes('aria-labelledby')).toBe(button.attributes('id'))
    expect(details.attributes('role')).toBe('region')

    wrapper.unmount()
  })
})
