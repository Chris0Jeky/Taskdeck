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
  it('describes provenance honestly for both deterministic captures and LLM chat automation (#1273)', () => {
    const wrapper = mount(ReviewProvenance, {
      props: { rows },
    })

    const text = wrapper.text()
    // Captures are triaged offline/deterministically; chat automation uses the configured AI provider.
    expect(text).toContain('deterministic offline extractor for captures')
    expect(text).toContain('configured AI provider for chat-driven automation')
    // Must not hardcode a specific LLM model (was "What haiku read"), and must not over-claim local-only for everything.
    expect(text).not.toContain('haiku')
    expect(text).not.toContain('No data left this device')
    expect(text).not.toContain('ran locally')
  })
})
