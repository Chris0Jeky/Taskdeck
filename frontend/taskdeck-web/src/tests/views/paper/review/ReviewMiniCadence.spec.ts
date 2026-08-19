import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewMiniCadence from '../../../../views/paper/review/ReviewMiniCadence.vue'

describe('ReviewMiniCadence', () => {
  it('renders nothing when mounted without cadence data', () => {
    // Mutation guard for the removed `days: () => [4, 3, 5, 2, 4, 1, 3]`
    // default: restoring it would draw seven fabricated bars for an account
    // with no activity history, failing every assertion below.
    const wrapper = mount(ReviewMiniCadence)
    expect(wrapper.find('[data-testid="paper-review-mini-cadence"]').exists()).toBe(false)
    expect(wrapper.find('.paper-review-cadence').exists()).toBe(false)
    expect(wrapper.findAll('.paper-review-cadence__bar')).toHaveLength(0)
    // #1816 item 5: assert USER-VISIBLE absence, not Vue's `<!--v-if-->`
    // placeholder string. The placeholder is a render internal that a Vue
    // upgrade may rename without any change in what a reader sees; "no element
    // and no text" is the property the no-fabrication contract actually needs.
    expect(wrapper.find('div').exists()).toBe(false)
    expect(wrapper.text()).toBe('')
  })

  it('renders nothing when the cadence data is an explicitly empty array', () => {
    const wrapper = mount(ReviewMiniCadence, { props: { days: [] } })
    expect(wrapper.find('.paper-review-cadence').exists()).toBe(false)
    expect(wrapper.findAll('.paper-review-cadence__bar')).toHaveLength(0)
  })

  it('renders one bar per real day value', () => {
    const wrapper = mount(ReviewMiniCadence, { props: { days: [1, 2, 3, 4, 5, 6, 7] } })
    expect(wrapper.find('[data-testid="paper-review-mini-cadence"]').exists()).toBe(true)
    expect(wrapper.findAll('.paper-review-cadence__bar')).toHaveLength(7)
  })

  it('marks only the newest bar as today', () => {
    const wrapper = mount(ReviewMiniCadence, { props: { days: [1, 2, 3] } })
    const bars = wrapper.findAll('.paper-review-cadence__bar')
    expect(bars).toHaveLength(3)
    expect(bars[0].classes()).not.toContain('paper-review-cadence__bar--today')
    expect(bars[1].classes()).not.toContain('paper-review-cadence__bar--today')
    expect(bars[2].classes()).toContain('paper-review-cadence__bar--today')
  })

  it('normalises bar heights against the largest real value', () => {
    const wrapper = mount(ReviewMiniCadence, { props: { days: [0, 2, 4] } })
    const heights = wrapper
      .findAll('.paper-review-cadence__bar')
      .map((bar) => (bar.element as HTMLElement).style.height)
    expect(heights).toEqual(['0%', '50%', '100%'])
  })

  it('describes the number of days actually rendered, not an assumed week', () => {
    const week = mount(ReviewMiniCadence, { props: { days: [1, 1, 1, 1, 1, 1, 1] } })
    expect(week.find('.paper-review-cadence').attributes('aria-label')).toBe(
      'Activity for the last 7 days',
    )

    const partial = mount(ReviewMiniCadence, { props: { days: [3, 1] } })
    expect(partial.find('.paper-review-cadence').attributes('aria-label')).toBe(
      'Activity for the last 2 days',
    )

    const single = mount(ReviewMiniCadence, { props: { days: [3] } })
    expect(single.find('.paper-review-cadence').attributes('aria-label')).toBe(
      'Activity for the last 1 day',
    )
  })
})
