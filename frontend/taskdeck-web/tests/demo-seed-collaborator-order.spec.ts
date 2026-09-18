import { describe, expect, it } from 'vitest'

import { seedDemo } from '../scripts/demo-seed.mjs'

describe('ordinary demo seed collaborator preflight', () => {
  it('authenticates the collaborator before board preparation can change product state', async () => {
    const boards = [
      {
        id: 'existing-board',
        name: 'Existing customer board',
        description: 'Must survive a stale collaborator password unchanged.',
        isArchived: false,
      },
    ]
    const originalBoards = structuredClone(boards)
    const authAttempts: string[] = []
    const productWrites: string[] = []
    let preparationAttempts = 0

    await expect(
      seedDemo(
        { reset: false },
        {
          ensureUser: async (account) => {
            authAttempts.push(account.username)
            if (account.username === 'collab') {
              throw new Error('Existing collaborator rejected the configured password.')
            }
            return {
              token: `${account.username}-token`,
              user: {
                id: `${account.username}-id`,
                username: account.username,
                email: account.email,
              },
            }
          },
          listBoards: async () => structuredClone(boards),
          prepareBoardsForSeed: async () => {
            preparationAttempts += 1
            productWrites.push('prepare canonical boards')
            boards.push({
              id: 'unexpected-demo-board',
              name: 'DEMO: Client Onboarding Demo',
              description: 'This write must never happen before collaborator authentication.',
              isArchived: false,
            })
            return {
              capture: { id: 'capture' },
              content: { id: 'content' },
              blank: { id: 'blank' },
              archived: { id: 'archived' },
              demoBoards: [],
              resetPlan: null,
            }
          },
        },
      ),
    ).rejects.toThrow(/collaborator rejected/i)

    expect(authAttempts).toEqual(['demo', 'collab'])
    expect(preparationAttempts).toBe(0)
    expect(productWrites).toEqual([])
    expect(boards).toEqual(originalBoards)
  })
})
