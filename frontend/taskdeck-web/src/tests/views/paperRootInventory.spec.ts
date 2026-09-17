import { describe, expect, it } from 'vitest'
import { PAPER_VIEW_ROOTS } from './paperRootInventory'

describe('Paper root inventory shape', () => {
  it('makes eyebrow absence explicit on every root entry', () => {
    const missingProperty = PAPER_VIEW_ROOTS
      .filter((root) => !Object.prototype.hasOwnProperty.call(root, 'eyebrow'))
      .map((root) => root.view)

    expect(missingProperty).toEqual([])
  })
})
