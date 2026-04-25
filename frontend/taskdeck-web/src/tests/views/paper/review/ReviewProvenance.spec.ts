import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewProvenance from '../../../../views/paper/review/ReviewProvenance.vue'
import type { ProvenanceRow } from '../../../../composables/usePaperReviewSelectors'

const rows: ProvenanceRow[] = [
  {
    icon: 'doc',
    key: 'source note',
    value: 'Captured inbox note',
    weight: 'primary',
  },
]

describe('ReviewProvenance', () => {
  it('uses neutral provider routing copy instead of claiming local-only processing', () => {
    const wrapper = mount(ReviewProvenance, {
      props: { rows },
    })

    const text = wrapper.text()
    expect(text).toContain("Provider routing follows this workspace's AI settings and policy.")
    expect(text).not.toContain('No data left this device')
    expect(text).not.toContain('ran locally')
  })
})
