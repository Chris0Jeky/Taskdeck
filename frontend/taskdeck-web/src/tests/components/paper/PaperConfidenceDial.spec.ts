import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperConfidenceDial from '../../../components/paper/PaperConfidenceDial.vue'

const CIRC = 2 * Math.PI * 28

describe('PaperConfidenceDial', () => {
  it('renders an 84px svg and a default CONF caption', () => {
    const wrapper = mount(PaperConfidenceDial, { props: { value: 0.84 } })
    const svg = wrapper.find('svg')
    expect(svg.attributes('width')).toBe('84')
    expect(svg.attributes('height')).toBe('84')
    expect(wrapper.find('.paper-confidence__caption').text()).toBe('CONF')
  })

  it('shows the value as a serif italic label without leading zero', () => {
    const wrapper = mount(PaperConfidenceDial, { props: { value: 0.84 } })
    expect(wrapper.find('.paper-confidence__value').text()).toBe('.84')
  })

  it('drives stroke-dasharray from the value', () => {
    const wrapper = mount(PaperConfidenceDial, { props: { value: 0.5 } })
    const arc = wrapper.find('.paper-confidence__arc')
    const dasharray = arc.attributes('stroke-dasharray') ?? ''
    const [filled, gap] = dasharray.split(' ').map(Number)
    expect(filled).toBeCloseTo(CIRC * 0.5, 1)
    expect(gap).toBeCloseTo(CIRC * 0.5, 1)
  })

  it('clamps values outside [0,1]', () => {
    const low = mount(PaperConfidenceDial, { props: { value: -0.5 } })
    expect(low.attributes('data-value')).toBe('0')
    const high = mount(PaperConfidenceDial, { props: { value: 1.5 } })
    expect(high.attributes('data-value')).toBe('1')
  })

  it('renders the optional caption + subline', () => {
    const wrapper = mount(PaperConfidenceDial, {
      props: { value: 0.7, caption: 'TRUST', subline: 'router · v3' },
    })
    expect(wrapper.find('.paper-confidence__caption').text()).toBe('TRUST')
    expect(wrapper.find('.paper-confidence__sub').text()).toBe('router · v3')
  })

  it('produces a stable structure suitable for snapshot at value=0.84', () => {
    const wrapper = mount(PaperConfidenceDial, { props: { value: 0.84 } })
    expect(wrapper.html()).toMatchSnapshot()
  })
})
