import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewAuthorCard from '../../../../views/paper/review/ReviewAuthorCard.vue'
import type {
  ConfidenceBreakdown,
  ConfidenceValueSource,
  PaperReviewEvidenceStatus,
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

/**
 * `settled` is the default because every case below is about what a landed
 * confidence read says. The state cases pass their own.
 */
function mountCard(
  breakdown: ConfidenceBreakdown,
  authorMeta = '',
  evidenceState: PaperReviewEvidenceStatus = 'settled',
) {
  return mount(ReviewAuthorCard, {
    attachTo: document.body,
    props: {
      authorName: 'Taskdeck',
      authorMeta,
      proposedDate: '2026-08-22',
      proposedTime: '18:00',
      proposedNum: '001',
      breakdown,
      evidenceState,
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

  /**
   * #1940, the second residual recorded with PR #2662. `EMPTY_CONFIDENCE` —
   * zero components, source `not-reported` — is what the composable holds while
   * a read is in flight and after one failed, so the sentence above was also
   * being rendered as a statement about a response that never arrived.
   */
  describe('an absent breakdown it cannot yet explain', () => {
    const stateLine = '[data-testid="paper-review-author-confidence-state"]'

    it('says the read is still running rather than that no confidence was reported', () => {
      const wrapper = mountCard(breakdownOf('not-reported'), '', 'loading')

      expect(wrapper.find(sourceLine).exists()).toBe(false)
      expect(wrapper.text()).not.toContain('No model confidence reported')
      expect(wrapper.get(stateLine).text()).toBe(
        'Reading the confidence evidence for this proposal…',
      )
      expect(wrapper.get(stateLine).isVisible()).toBe(true)

      wrapper.unmount()
    })

    it('says the read failed rather than that no confidence was reported', () => {
      const wrapper = mountCard(breakdownOf('not-reported'), '', 'failed')

      expect(wrapper.find(sourceLine).exists()).toBe(false)
      expect(wrapper.text()).not.toContain('No model confidence reported')
      expect(wrapper.get(stateLine).text()).toBe(
        'Confidence evidence could not be read, so its source is unknown.',
      )

      wrapper.unmount()
    })

    // The deterministic wording is a claim about the producer, not about the
    // number, so it is withheld by the same rule.
    it('withholds the deterministic sentence too until the read has landed', () => {
      const wrapper = mountCard(breakdownOf('deterministic'), '', 'loading')

      expect(wrapper.find(sourceLine).exists()).toBe(false)
      expect(wrapper.text()).not.toContain('Deterministic extraction')

      wrapper.unmount()
    })

    it('states nothing at all when no proposal is active', () => {
      const wrapper = mountCard(breakdownOf('not-reported'), '', 'idle')

      expect(wrapper.find(sourceLine).exists()).toBe(false)
      expect(wrapper.find(stateLine).exists()).toBe(false)

      wrapper.unmount()
    })

    it('adds no state line to bars that already show where the number came from', () => {
      const wrapper = mountCard(
        breakdownOf('model-reported', [{ key: 'Operation 1: create card', value: 0.92 }]),
        '0.90 model-reported average',
        'loading',
      )

      expect(wrapper.find(stateLine).exists()).toBe(false)
      expect(wrapper.find(sourceLine).exists()).toBe(false)

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

  // Matches ReviewProvenance.vue: `v-show` alone leaves the collapsed region in
  // the accessibility tree for anything that reads the DOM rather than the
  // computed style.
  it('binds `hidden` to the collapsed state, like the provenance card', async () => {
    const wrapper = mountCard(breakdownOf('model-reported', [{ key: 'Operation 1', value: 0.9 }]))
    const button = wrapper.get('[data-testid="paper-review-confidence-disclosure"]')
    const details = wrapper.get('[data-testid="paper-review-confidence-details"]')

    expect(details.attributes('hidden')).toBeDefined()

    await button.trigger('click')
    expect(details.attributes('hidden')).toBeUndefined()

    await button.trigger('click')
    expect(details.attributes('hidden')).toBeDefined()

    wrapper.unmount()
  })
})
