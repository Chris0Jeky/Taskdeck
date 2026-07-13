import { describe, expect, it } from 'vitest'

import { assertSafeLocalApiTarget, collectAllListItems } from '../scripts/demo-shared.mjs'

describe('demo api target safety guard', () => {
  it('accepts local demo api targets', () => {
    expect(() => {
      assertSafeLocalApiTarget('http://localhost:5000/api', {
        contextLabel: 'run demo director',
      })
    }).not.toThrow()
  })

  it('rejects non-local demo api targets by default', () => {
    expect(() => {
      assertSafeLocalApiTarget('http://demo.taskdeck.example/api', {
        contextLabel: 'run demo director',
      })
    }).toThrow('Refusing to run demo director against non-local API target')
  })

  it('allows non-local demo api targets only when explicitly overridden', () => {
    expect(() => {
      assertSafeLocalApiTarget('http://demo.taskdeck.example/api', {
        allowNonLocal: true,
        contextLabel: 'run demo director',
      })
    }).not.toThrow()
  })
})

describe('demo list pagination', () => {
  it('collects every page using the returned item count as the next offset', async () => {
    const calls: Array<{ offset: number; limit: number }> = []
    const result = await collectAllListItems(
      async ({ offset, limit }) => {
        calls.push({ offset, limit })
        return offset === 0
          ? { items: [{ id: 'board-1' }, { id: 'board-2' }], hasMore: true }
          : { items: [{ id: 'board-3' }], hasMore: false }
      },
      { contextLabel: 'boards', limit: 2 },
    )

    expect(result).toEqual([{ id: 'board-1' }, { id: 'board-2' }, { id: 'board-3' }])
    expect(calls).toEqual([
      { offset: 0, limit: 2 },
      { offset: 2, limit: 2 },
    ])
  })

  it('treats an empty API response as an empty list', async () => {
    await expect(collectAllListItems(async () => null, { contextLabel: 'boards' })).resolves.toEqual([])
  })

  it('fails instead of looping forever when pagination makes no progress', async () => {
    await expect(
      collectAllListItems(async () => ({ items: [], hasMore: true }), { contextLabel: 'boards' }),
    ).rejects.toThrow('boards pagination reported more items without returning a page')
  })
})
