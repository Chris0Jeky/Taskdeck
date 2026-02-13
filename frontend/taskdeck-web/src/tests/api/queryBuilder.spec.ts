import { describe, expect, it } from 'vitest'
import { buildQueryParams } from '../../api/queryBuilder'

describe('queryBuilder', () => {
  it('returns empty params for undefined filters', () => {
    expect(buildQueryParams().toString()).toBe('')
  })

  it('returns empty params when all values are empty', () => {
    expect(buildQueryParams({ search: '', status: undefined, boardId: null }).toString()).toBe('')
  })

  it('builds params from non-empty values', () => {
    expect(buildQueryParams({ search: 'test', limit: 25, includeArchived: true }).toString()).toBe(
      'search=test&limit=25&includeArchived=true'
    )
  })
})
