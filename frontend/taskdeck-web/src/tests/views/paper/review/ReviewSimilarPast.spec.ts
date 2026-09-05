import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewSimilarPast from '../../../../views/paper/review/ReviewSimilarPast.vue'
import type { SimilarPastRow } from '../../../../composables/usePaperReviewSelectors'

/**
 * ReviewSimilarPast — the card must tell the truth BEFORE the disclosure opens
 * (#1940, the retained residual of #2166).
 *
 * The card shipped with the whole empty state locked inside a collapsed
 * region: a reviewer looking at a proposal with no comparable history saw only
 * "Show similar decisions" and had to open it to learn there was nothing to
 * show. The fix keeps the disclosure in every state — PaperReviewView.spec.ts,
 * PaperReviewView.language.spec.ts and the required E2E smoke all drive that
 * button on an empty fixture — and hoists the fact above it.
 *
 * The empty sentence lives in exactly one place. Duplicating it above and
 * inside the region would make the open state read as two separate findings.
 */

const ROWS: SimilarPastRow[] = [
  { serial: '#PAST-1', title: 'A prior comparable decision', verdict: 'applied', date: '2026-08-20' },
  { serial: '#PAST-2', title: 'A prior rejected decision', verdict: 'rejected', date: '2026-08-19' },
]

const EMPTY_SENTENCE = 'No comparable past decisions.'

function mountCard(rows: SimilarPastRow[]) {
  return mount(ReviewSimilarPast, {
    attachTo: document.body,
    props: {
      rows,
      applyRate:
        rows.length === 0
          ? { applied: 0, total: 0, ratio: 0 }
          : { applied: 1, total: 2, ratio: 0.5 },
    },
  })
}

/** How many times a sentence appears in the rendered text of the whole card. */
function occurrences(haystack: string, needle: string): number {
  return haystack.split(needle).length - 1
}

describe('ReviewSimilarPast', () => {
  describe('with no comparable decisions', () => {
    it('states the emptiness at first paint, above the still-collapsed disclosure', () => {
      const wrapper = mountCard([])
      const empty = wrapper.get('[data-testid="paper-review-similar-past-empty"]')
      const details = wrapper.get('[data-testid="paper-review-similar-past-details"]')

      expect(empty.text()).toBe(EMPTY_SENTENCE)
      expect(empty.isVisible()).toBe(true)
      // The hoisted line is a sibling of the region, not a child of it: a
      // child would still be invisible while the region is collapsed.
      expect(details.find('[data-testid="paper-review-similar-past-empty"]').exists()).toBe(false)
      expect(details.isVisible()).toBe(false)

      wrapper.unmount()
    })

    it('says so on the disclosure label too, so the closed control is not a promise', () => {
      const wrapper = mountCard([])
      const button = wrapper.get('[data-testid="paper-review-similar-past-disclosure"]')

      expect(button.text()).toContain('Show similar decisions')
      expect(button.text()).toContain('none found')

      wrapper.unmount()
    })

    it('keeps the disclosure present and correctly paired while collapsed', () => {
      const wrapper = mountCard([])
      const button = wrapper.get('[data-testid="paper-review-similar-past-disclosure"]')
      const details = wrapper.get('[data-testid="paper-review-similar-past-details"]')

      expect(button.element.tagName).toBe('BUTTON')
      expect(button.attributes('type')).toBe('button')
      expect(button.attributes('aria-expanded')).toBe('false')
      expect(button.attributes('aria-controls')).toBe(details.attributes('id'))
      expect(details.attributes('aria-labelledby')).toBe(button.attributes('id'))
      expect(details.attributes('role')).toBe('region')

      wrapper.unmount()
    })

    it('never says the same thing twice', async () => {
      const wrapper = mountCard([])
      const details = wrapper.get('[data-testid="paper-review-similar-past-details"]')

      expect(occurrences(wrapper.text(), EMPTY_SENTENCE)).toBe(1)
      expect(details.text()).not.toContain(EMPTY_SENTENCE)

      await wrapper.get('[data-testid="paper-review-similar-past-disclosure"]').trigger('click')

      expect(occurrences(wrapper.text(), EMPTY_SENTENCE)).toBe(1)
      expect(details.text()).not.toContain(EMPTY_SENTENCE)

      wrapper.unmount()
    })

    it('opens onto an explanation rather than onto nothing', async () => {
      const wrapper = mountCard([])
      const button = wrapper.get('[data-testid="paper-review-similar-past-disclosure"]')
      const details = wrapper.get('[data-testid="paper-review-similar-past-details"]')

      await button.trigger('click')

      expect(button.attributes('aria-expanded')).toBe('true')
      expect(details.isVisible()).toBe(true)
      // A region that opens to a zero-height void reads as a broken control,
      // and the required E2E asserts this region is *visible* once opened.
      expect(details.get('[data-testid="paper-review-similar-past-empty-detail"]').text()).toBe(
        'Decisions on comparable proposals will be listed here.',
      )
      expect(wrapper.find('.paper-review-past__rate').exists()).toBe(false)

      wrapper.unmount()
    })
  })

  describe('with comparable decisions', () => {
    it('keeps the rows and the apply-rate footer behind the disclosure', async () => {
      const wrapper = mountCard(ROWS)
      const button = wrapper.get('[data-testid="paper-review-similar-past-disclosure"]')
      const details = wrapper.get('[data-testid="paper-review-similar-past-details"]')
      const rate = wrapper.get('.paper-review-past__rate')

      expect(wrapper.find('[data-testid="paper-review-similar-past-empty"]').exists()).toBe(false)
      expect(wrapper.text()).not.toContain(EMPTY_SENTENCE)
      expect(button.text()).toContain('Show similar decisions')
      expect(button.text()).not.toContain('none found')
      expect(details.isVisible()).toBe(false)
      expect(rate.isVisible()).toBe(false)

      await button.trigger('click')

      expect(details.isVisible()).toBe(true)
      expect(details.text()).toContain('A prior comparable decision')
      expect(details.text()).toContain('A prior rejected decision')
      expect(rate.isVisible()).toBe(true)
      expect(rate.text()).toContain('1 of 2 (50%)')
      expect(
        wrapper.find('[data-testid="paper-review-similar-past-empty-detail"]').exists(),
      ).toBe(false)

      wrapper.unmount()
    })
  })
})
