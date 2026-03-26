import { describe, expect, it } from 'vitest'

import {
  buildProposalLookupPath,
  collectSeededChatProposalIds,
  hasSeededChatEvidence,
  mergeSeedPlanChatSessions,
  planDemoSeedRerunState,
  shouldRecreateCaptureSeed,
} from '../scripts/demo-seed.mjs'

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
        { id: 'capture-ignored', boardId: 'board-1', textExcerpt: 'Duplicate onboarding note from a prior client thread (demo).' },
        {
          id: 'capture-applied',
          boardId: 'board-1',
          textExcerpt: 'New client onboarding - ACME Ltd',
        },
        {
          id: 'capture-pending',
          boardId: 'board-1',
          textExcerpt: 'Client onboarding follow-up - Northwind Ltd',
        },
      ],
      boardCards: [{ id: 'card-1', title: 'From queue: confirm onboarding status update' }],
      queueRequests: [{ id: 'queue-1', boardId: 'board-1', status: 'Failed', errorMessage: 'nope' }],
      chatSessions: [
        {
          id: 'session-1',
          boardId: 'board-1',
          title: 'Stakeholder Demo',
          recentMessages: [
            {
              id: 'msg-1',
              content: 'rename board to "DEMO: Client Onboarding Demo (Chat)"',
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

  it('prefers the newest matching capture summary when duplicate seeded texts exist', () => {
    const plan = planDemoSeedRerunState({
      boardId: 'board-1',
      captureSummaries: [
        {
          id: 'capture-old',
          boardId: 'board-1',
          textExcerpt: 'Client onboarding follow-up - Northwind Ltd',
          createdAt: '2026-03-06T18:00:00.000Z',
        },
        {
          id: 'capture-new',
          boardId: 'board-1',
          textExcerpt: 'Client onboarding follow-up - Northwind Ltd',
          createdAt: '2026-03-06T19:00:00.000Z',
        },
      ],
      boardCards: [],
      queueRequests: [],
      chatSessions: [],
      existingComments: [],
      logEntries: [],
      demoUsername: 'demo',
      collabUsername: 'collab',
    })

    expect(plan.captures.triagePending?.id).toBe('capture-new')
  })

  it('only treats the seeded rename instruction as reusable chat evidence', () => {
    expect(
      hasSeededChatEvidence(
        [
          {
            id: 'msg-1',
            content: 'Here is the follow-up proposal.',
            proposalId: 'proposal-1',
          },
        ],
        'rename board to "DEMO: Client Onboarding Demo (Chat)"',
      ),
    ).toBe(false)
  })

  it('collects only seeded rename proposal ids so reruns do not apply unrelated chat proposals', () => {
    expect(
      collectSeededChatProposalIds([
        { id: 'msg-1', content: 'rename board to "DEMO: Client Onboarding Demo (Chat)"', proposalId: 'proposal-1' },
        { id: 'msg-2', content: 'rename board to "DEMO: Client Onboarding Demo (Chat)"', proposalId: 'proposal-1' },
        { id: 'msg-3', content: 'create another card', proposalId: 'proposal-2' },
        { id: 'msg-4', proposalId: '' },
      ],
      'rename board to "DEMO: Client Onboarding Demo (Chat)"'),
    ).toEqual(['proposal-1'])
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
    expect(
      shouldRecreateCaptureSeed(
        {
          id: 'capture-4',
          status: 'Converted',
          provenance: { proposalId: 'proposal-2' },
        },
        { applyProposal: false },
      ),
    ).toBe(true)
    expect(
      shouldRecreateCaptureSeed(
        {
          id: 'capture-5',
          status: 'Converted',
          provenance: { proposalId: 'proposal-3' },
        },
        { applyProposal: true },
      ),
    ).toBe(false)
    expect(
      shouldRecreateCaptureSeed(
        {
          id: 'capture-6',
          status: 'Converted',
          provenance: { proposalId: null },
        },
        { applyProposal: true },
      ),
    ).toBe(true)
  })

  it('recreates ignored demo samples that can no longer be cancelled back to ignored', () => {
    expect(
      shouldRecreateCaptureSeed(
        {
          id: 'capture-7',
          status: 'ProposalCreated',
          provenance: { proposalId: 'proposal-1' },
        },
        { ignore: true },
      ),
    ).toBe(true)
    expect(
      shouldRecreateCaptureSeed(
        {
          id: 'capture-8',
          status: 'Failed',
          provenance: { proposalId: null },
        },
        { ignore: true },
      ),
    ).toBe(false)
    expect(
      shouldRecreateCaptureSeed(
        {
          id: 'capture-9',
          status: 'Ignored',
          provenance: { proposalId: null },
        },
        { ignore: true },
      ),
    ).toBe(false)
  })

  it('drops stale seeded chat sessions from rerun planning when detail hydration fails', () => {
    expect(
      mergeSeedPlanChatSessions(
        [
          { id: 'session-1', title: 'Stakeholder Demo' },
          { id: 'session-2', title: 'Other Session' },
        ],
        'session-1',
        null,
      ),
    ).toEqual([{ id: 'session-2', title: 'Other Session' }])
  })

  it('builds a direct proposal lookup path for rerun reuse checks', () => {
    expect(buildProposalLookupPath('proposal/with spaces')).toBe('/automation/proposals/proposal%2Fwith%20spaces')
    expect(() => buildProposalLookupPath('')).toThrow('Proposal id is required')
  })
})
