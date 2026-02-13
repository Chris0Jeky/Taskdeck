import { describe, expect, it } from 'vitest'
import { buildQueryString } from '../../utils/queryBuilder'

describe('queryBuilder', () => {
  it('returns empty string when filters are missing', () => {
    expect(buildQueryString()).toBe('')
    expect(buildQueryString(null)).toBe('')
  })

  it('serializes supported value types', () => {
    const query = buildQueryString({ status: 'PendingReview', limit: 25, includeArchived: false })

    expect(query).toBe('?status=PendingReview&limit=25&includeArchived=false')
  })

  it('ignores undefined, null, and blank string values', () => {
    const query = buildQueryString({ status: '', source: '   ', boardId: undefined, userId: null, limit: 0 })

    expect(query).toBe('?limit=0')
  })

  it('ignores unsupported value types', () => {
    const query = buildQueryString({ level: 'Error', metadata: { nested: true }, tags: ['ops'], limit: 10 })

    expect(query).toBe('?level=Error&limit=10')
  })
})
