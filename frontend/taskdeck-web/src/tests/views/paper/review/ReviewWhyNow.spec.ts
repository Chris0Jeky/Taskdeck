import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewWhyNow from '../../../../views/paper/review/ReviewWhyNow.vue'
import ReviewRightRail from '../../../../views/paper/review/ReviewRightRail.vue'
import type { ConfidenceBreakdown } from '../../../../composables/usePaperReviewSelectors'

const breakdown: ConfidenceBreakdown = {
  overall: 0.96,
  components: [{ key: 'patternMatch', value: 1 }],
  threshold: 0.7,
}

function mountRightRail() {
  return mount(ReviewRightRail, {
    props: {
      authorName: 'Taskdeck',
      authorMeta: 'automation',
      proposedDate: '2026-08-22',
      proposedTime: '18:00',
      proposedNum: '001',
      whyNowBody: 'Created from Inbox capture triage.',
      breakdown,
      similarPast: [],
      similarPastApplyRate: { applied: 0, total: 0, ratio: 0 },
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
  // handler and no route. There is no surface that owns the apply threshold
  // (it is a backend constant), so the card carries no link at all rather
  // than a dead one. Mutation guard: restoring the anchor fails here.
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
  })
})
