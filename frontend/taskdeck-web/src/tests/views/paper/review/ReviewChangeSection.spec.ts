import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { i18n } from '../../../../i18n'
import ReviewChangeSection from '../../../../views/paper/review/ReviewChangeSection.vue'

const props = {
  before: {
    serial: 'C-1',
    title: 'Recorded title',
    body: 'Recorded body.',
    meta: 'Board · Queue',
  },
  after: [{ serial: 'op-1', title: 'Create · Card', body: 'Recorded operation.', status: 'new' as const }],
  fields: [{ key: 'title', before: 'Old', after: 'New' }],
  subTitle: '1 operation · Board',
}

describe('ReviewChangeSection historical copy', () => {
  it('keeps prospective copy for a pending proposal', () => {
    i18n.global.locale.value = 'en'
    const wrapper = mount(ReviewChangeSection, { props })

    expect(wrapper.find('.paper-review-change__col--before .paper-review-change__eyebrow').text()).toBe(
      'Before · today',
    )
    expect(wrapper.find('.paper-review-change__col--after .paper-review-change__eyebrow').text()).toBe(
      'After · on apply',
    )
  })

  it('uses historical labels in every shipped locale for an applied record', () => {
    const expected = {
      en: ['Before · recorded', 'After · applied'],
      it: ['Prima · registrata', 'Dopo · applicata'],
      es: ['Antes · registrado', 'Después · aplicado'],
    } as const

    for (const [locale, [before, after]] of Object.entries(expected)) {
      i18n.global.locale.value = locale as keyof typeof expected
      const wrapper = mount(ReviewChangeSection, { props: { ...props, applied: true } })

      expect(wrapper.find('.paper-review-change__col--before .paper-review-change__eyebrow').text()).toBe(before)
      expect(wrapper.find('.paper-review-change__col--after .paper-review-change__eyebrow').text()).toBe(after)
    }
  })
})
