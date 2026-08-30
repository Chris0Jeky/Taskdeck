import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewWhyNow from '../../../../views/paper/review/ReviewWhyNow.vue'
import ReviewRightRail from '../../../../views/paper/review/ReviewRightRail.vue'
import type { ConfidenceBreakdown } from '../../../../composables/usePaperReviewSelectors'

const breakdown: ConfidenceBreakdown = {
  overall: 0.96,
  components: [{ key: 'Operation 1: create card', value: 0.96 }],
  threshold: null,
  source: 'model-reported',
}

function mountRightRail() {
  return mount(ReviewRightRail, {
    attachTo: document.body,
    props: {
      authorName: 'Taskdeck',
      authorMeta: '0.96 model-reported average',
      proposedDate: '2026-08-22',
      proposedTime: '18:00',
      proposedNum: '001',
      whyNowBody: 'Created from Inbox capture triage.',
      breakdown: { ...breakdown, note: 'Reported by the model for the proposed operation.' },
      similarPast: [
        {
          serial: '#PAST',
          title: 'A prior comparable decision',
          verdict: 'applied',
          date: '2026-08-20',
        },
      ],
      similarPastApplyRate: { applied: 1, total: 1, ratio: 1 },
    },
    global: {
      stubs: {
        RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' },
      },
    },
  })
}

describe('ReviewWhyNow', () => {
  it('renders the provenance body it is given', () => {
    const wrapper = mount(ReviewWhyNow, {
      props: { body: 'Created from Inbox capture triage.' },
    })

    expect(wrapper.find('.paper-review-whynow__body').text()).toBe(
      'Created from Inbox capture triage.',
    )
    expect(wrapper.find('.paper-review-whynow__eyebrow').text()).toBe('Why now')
  })

  // #1941 — the card shipped `<a href="#">Tune heuristics →</a>` with no
  // handler and no route. Confidence evidence has no tuning surface here, so
  // the card carries no link at all rather than a dead one. Mutation guard:
  // restoring the anchor fails here.
  it('renders no link at all — never a dead one', () => {
    const wrapper = mount(ReviewWhyNow, { props: { body: 'Any body.' } })

    expect(wrapper.findAll('a')).toHaveLength(0)
    expect(wrapper.html()).not.toContain('href="#"')
    expect(wrapper.text()).not.toContain('Tune heuristics')
  })

  it('carries no dead affordance through the right rail either', () => {
    const wrapper = mountRightRail()
    const whyNow = wrapper.find('.paper-review-whynow')

    expect(whyNow.exists()).toBe(true)
    expect(whyNow.findAll('a')).toHaveLength(0)
    expect(whyNow.findAll('button')).toHaveLength(0)
    expect(wrapper.text()).not.toContain('Tune heuristics')
    wrapper.unmount()
  })

  it('keeps author identity visible while confidence and similar decisions start independently collapsed', async () => {
    const wrapper = mountRightRail()
    const confidenceButton = wrapper.get('[data-testid="paper-review-confidence-disclosure"]')
    const confidenceDetails = wrapper.get('[data-testid="paper-review-confidence-details"]')
    const similarButton = wrapper.get('[data-testid="paper-review-similar-past-disclosure"]')
    const similarDetails = wrapper.get('[data-testid="paper-review-similar-past-details"]')

    expect(wrapper.get('.paper-review-author__name').text()).toBe('Taskdeck')
    expect(wrapper.get('.paper-review-author__meta').text()).toContain('model-reported')
    expect(confidenceButton.element.tagName).toBe('BUTTON')
    expect(confidenceButton.attributes('type')).toBe('button')
    expect(confidenceButton.attributes('aria-expanded')).toBe('false')
    expect(confidenceButton.attributes('aria-controls')).toBe(confidenceDetails.attributes('id'))
    expect(confidenceDetails.attributes('aria-labelledby')).toBe(confidenceButton.attributes('id'))
    expect(confidenceDetails.isVisible()).toBe(false)
    expect(similarButton.attributes('aria-expanded')).toBe('false')
    expect(similarButton.attributes('aria-controls')).toBe(similarDetails.attributes('id'))
    expect(similarDetails.attributes('aria-labelledby')).toBe(similarButton.attributes('id'))
    expect(similarDetails.isVisible()).toBe(false)

    ;(confidenceButton.element as HTMLButtonElement).focus()
    await confidenceButton.trigger('click')

    expect(confidenceButton.attributes('aria-expanded')).toBe('true')
    expect(confidenceButton.text()).toContain('Hide confidence details')
    expect(confidenceDetails.isVisible()).toBe(true)
    expect(confidenceDetails.text()).toContain('Reported by the model')
    expect(document.activeElement).toBe(confidenceButton.element)
    expect(similarButton.attributes('aria-expanded')).toBe('false')

    ;(similarButton.element as HTMLButtonElement).focus()
    await similarButton.trigger('click')

    expect(similarButton.attributes('aria-expanded')).toBe('true')
    expect(similarDetails.isVisible()).toBe(true)
    expect(similarDetails.text()).toContain('A prior comparable decision')
    expect(document.activeElement).toBe(similarButton.element)
    expect(confidenceButton.attributes('aria-expanded')).toBe('true')

    wrapper.unmount()
  })
})
