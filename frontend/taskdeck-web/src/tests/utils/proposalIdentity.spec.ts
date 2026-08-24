import { describe, expect, it } from 'vitest'
import { proposalIdsEqual } from '../../utils/proposalIdentity'

describe('proposalIdsEqual', () => {
  it('case-folds only proposal-id equality', () => {
    expect(
      proposalIdsEqual(
        '8EFE8562-0B61-4DD3-A67D-6F26350A98BB',
        '8efe8562-0b61-4dd3-a67d-6f26350a98bb',
      ),
    ).toBe(true)
    expect(proposalIdsEqual('proposal-a', 'proposal-b')).toBe(false)
    expect(proposalIdsEqual(' proposal-a', 'proposal-a')).toBe(false)
    expect(proposalIdsEqual('', '')).toBe(false)
    expect(proposalIdsEqual(null, null)).toBe(false)
  })
})
