import { describe, expect, it } from 'vitest'
import { splitQuotedSummary } from '../../utils/paperReviewPresentation'

describe('splitQuotedSummary', () => {
  it('returns an empty segment for an empty summary', () => {
    expect(splitQuotedSummary('')).toEqual([{ text: '' }])
  })

  it('emphasizes every straight-quoted phrase and preserves surrounding text', () => {
    expect(splitQuotedSummary('Split "dark mode" and "QA pass" into cards')).toEqual([
      { text: 'Split ' },
      { text: '“dark mode”', emphasis: true },
      { text: ' and ' },
      { text: '“QA pass”', emphasis: true },
      { text: ' into cards' },
    ])
  })

  it('accepts curly quotes and emits the canonical curly pair', () => {
    expect(splitQuotedSummary('Move “invoice” next')).toEqual([
      { text: 'Move ' },
      { text: '“invoice”', emphasis: true },
      { text: ' next' },
    ])
  })

  it('keeps unquoted or unfinished summaries visible without dropping text', () => {
    expect(splitQuotedSummary('No quoted title')).toEqual([
      { text: 'No quoted title' },
    ])
    expect(splitQuotedSummary('Unfinished "title')).toEqual([
      { text: 'Unfinished "title' },
    ])
  })
})
