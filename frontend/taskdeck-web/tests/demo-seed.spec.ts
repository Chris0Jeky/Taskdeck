import { describe, expect, it } from 'vitest'

import { hasSeededChatEvidence, planDemoSeedRerunState, shouldRecreateCaptureSeed } from '../scripts/demo-seed.mjs'

describe('demo seed rerun planning', () => {
  it('marks all seeded artifacts for creation when the demo account is empty', () => {
    const plan = planDemoSeedRerunState({
      boardId: 'board-1',
      captureSummaries: [],
      boardCards: [],
      queueRequests: [],
      chatSessions: [],
      existingComments: [],
      logEntries: [],
      demoUsername: 'demo',
      collabUsername: 'collab',
    })

    expect(plan.captures.ignored).toBeUndefined()
    expect(plan.captures.triageApplied).toBeUndefined()
    expect(plan.captures.triagePending).toBeUndefined()
    expect(plan.queue.seededCard).toBeNull()
    expect(plan.queue.hasFailedRequest).toBe(false)
    expect(plan.chat.seededSession).toBeNull()
    expect(plan.chat.hasSeededMessage).toBe(false)
    expect(plan.comments.hasDemoMention).toBe(false)
    expect(plan.comments.hasCollabReply).toBe(false)
    expect(plan.ops.hasHealthCheckLog).toBe(false)
    expect(plan.ops.hasBoardsListLog).toBe(false)
  })

  it('reuses seeded captures, queue examples, chat evidence, comments, and ops logs on rerun', () => {
    const plan = planDemoSeedRerunState({
      boardId: 'board-1',
      captureSummaries: [
        { id: 'capture-ignored', boardId: 'board-1', textExcerpt: 'This item is ignored (demo).' },
        {
          id: 'capture-applied',
          boardId: 'board-1',
          textExcerpt: '- [ ] Draft a 5-minute stakeholder demo script',
        },
        {
          id: 'capture-pending',
          boardId: 'board-1',
          textExcerpt: '- [ ] Follow up: connect Activity view to real audit queries',
        },
      ],
      boardCards: [{ id: 'card-1', title: 'From queue: demo seeded item' }],
      queueRequests: [{ id: 'queue-1', boardId: 'board-1', status: 'Failed', errorMessage: 'nope' }],
      chatSessions: [
        {
          id: 'session-1',
          boardId: 'board-1',
          title: 'Stakeholder Demo',
          recentMessages: [
            {
              id: 'msg-1',
              content: 'rename board to "DEMO: Capture Loop (Chat)"',
              proposalId: 'proposal-1',
            },
          ],
        },
      ],
      existingComments: [
        {
          id: 'comment-1',
          content: 'Heads up @collab - this is a seeded mention for the Notifications view.',
        },
        {
          id: 'comment-2',
          content: '@demo ack - I will take a look after lunch. (seeded)',
        },
      ],
      logEntries: [
        { id: 'log-1', message: "Starting template 'health.check'" },
        { id: 'log-2', message: "Starting template 'boards.list'" },
      ],
      demoUsername: 'demo',
      collabUsername: 'collab',
    })

    expect(plan.captures.ignored?.id).toBe('capture-ignored')
    expect(plan.captures.triageApplied?.id).toBe('capture-applied')
    expect(plan.captures.triagePending?.id).toBe('capture-pending')
    expect(plan.queue.seededCard?.id).toBe('card-1')
    expect(plan.queue.hasFailedRequest).toBe(true)
    expect(plan.chat.seededSession?.id).toBe('session-1')
    expect(plan.chat.hasSeededMessage).toBe(true)
    expect(plan.comments.hasDemoMention).toBe(true)
    expect(plan.comments.hasCollabReply).toBe(true)
    expect(plan.ops.hasHealthCheckLog).toBe(true)
    expect(plan.ops.hasBoardsListLog).toBe(true)
  })

  it('treats proposal-bearing chat history as seeded evidence even without the exact rename instruction', () => {
    expect(
      hasSeededChatEvidence(
        [
          {
            id: 'msg-1',
            content: 'Here is the follow-up proposal.',
            proposalId: 'proposal-1',
          },
        ],
        'rename board to "DEMO: Capture Loop (Chat)"',
      ),
    ).toBe(true)
  })

  it('recreates terminal capture items that have no proposal to reuse on rerun', () => {
    expect(
      shouldRecreateCaptureSeed({
        id: 'capture-1',
        status: 'Converted',
        provenance: { proposalId: null },
      }),
    ).toBe(true)
    expect(
      shouldRecreateCaptureSeed({
        id: 'capture-2',
        status: 'Ignored',
        provenance: { proposalId: null },
      }),
    ).toBe(true)
    expect(
      shouldRecreateCaptureSeed({
        id: 'capture-3',
        status: 'ProposalCreated',
        provenance: { proposalId: 'proposal-1' },
      }),
    ).toBe(false)
  })
})
