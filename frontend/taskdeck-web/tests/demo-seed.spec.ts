import { createServer, type IncomingMessage, type Server } from 'node:http'
import type { AddressInfo, Socket } from 'node:net'
import { describe, expect, it } from 'vitest'

import {
  buildProposalLookupPath,
  collectSeededChatProposalIds,
  createRunBoundApiTransport,
  createRunBoundApiTransportFromEnvironment,
  hasSeededChatEvidence,
  mergeSeedPlanChatSessions,
  parseSeedArgs,
  planDemoSeedRerunState,
  shouldRecreateCaptureSeed,
} from '../scripts/demo-seed.mjs'
import { extractListItems } from '../scripts/demo-shared.mjs'

const DEV_RUN_ID = '12345678-1234-4234-8234-123456789abc'
const OTHER_DEV_RUN_ID = '87654321-4321-4321-8321-cba987654321'

function trackSockets(server: Server) {
  const sockets = new Set<Socket>()
  server.on('connection', (socket) => {
    sockets.add(socket)
    socket.once('close', () => sockets.delete(socket))
  })
  return sockets
}

async function listenOnLoopback(server: Server, port = 0) {
  await new Promise<void>((resolve, reject) => {
    const onError = (error: Error) => reject(error)
    server.once('error', onError)
    server.listen(port, '127.0.0.1', () => {
      server.off('error', onError)
      resolve()
    })
  })
  return (server.address() as AddressInfo).port
}

async function closeServer(server: Server, sockets: Set<Socket>) {
  for (const socket of sockets) socket.destroy()
  if (!server.listening) return

  await new Promise<void>((resolve, reject) => {
    server.close((error) => {
      if (error) reject(error)
      else resolve()
    })
  })
}

async function readRequestBody(request: IncomingMessage) {
  const chunks = []
  for await (const chunk of request) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk))
  }
  return Buffer.concat(chunks).toString('utf8')
}

function createManualResponseDeadline() {
  const deadlines: Array<{ callback: () => void; cleared: boolean; milliseconds: number }> = []
  let elapsedMilliseconds = 0

  return {
    advanceBy(milliseconds: number) {
      elapsedMilliseconds += milliseconds
      for (const deadline of deadlines) {
        if (!deadline.cleared && deadline.milliseconds <= elapsedMilliseconds) {
          deadline.cleared = true
          deadline.callback()
        }
      }
    },
    clearTimeoutFn(deadline: { cleared: boolean }) {
      deadline.cleared = true
    },
    fireNext() {
      const deadline = deadlines.find((candidate) => !candidate.cleared)
      if (!deadline) throw new Error('No active response deadline was scheduled.')
      deadline.callback()
    },
    setTimeoutFn(callback: () => void, milliseconds: number) {
      const deadline = { callback, cleared: false, milliseconds }
      deadlines.push(deadline)
      return deadline
    },
    get pendingMilliseconds() {
      return deadlines.filter((deadline) => !deadline.cleared).map((deadline) => deadline.milliseconds)
    },
  }
}

describe('demo seed rerun planning', () => {
  it('normalizes paginated API responses for demo board discovery', () => {
    expect(extractListItems([{ id: 'legacy-board' }], 'boards')).toEqual([{ id: 'legacy-board' }])
    expect(extractListItems({ items: [{ id: 'board-1' }] }, 'boards')).toEqual([{ id: 'board-1' }])
    expect(extractListItems({ Items: [{ id: 'board-2' }] }, 'boards')).toEqual([{ id: 'board-2' }])
    expect(() => extractListItems({ totalCount: 0 }, 'boards')).toThrow(
      'boards did not return a list or paginated items object',
    )
  })

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
    expect(plan.captures.triagePendingAcme).toBeUndefined()
    expect(plan.captures.triagePendingNorthwind).toBeUndefined()
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
          id: 'capture-pending-acme',
          boardId: 'board-1',
          textExcerpt: 'ACME Ltd - year-end checklist',
        },
        {
          id: 'capture-pending-northwind',
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
    expect(plan.captures.triagePendingAcme?.id).toBe('capture-pending-acme')
    expect(plan.captures.triagePendingNorthwind?.id).toBe('capture-pending-northwind')
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

    expect(plan.captures.triagePendingNorthwind?.id).toBe('capture-new')
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

describe('run-bound demo seed transport', () => {
  it('proves readiness and sends every seed request over one socket without exposing the run ID', async () => {
    const requests = []
    const requestSockets = new Set<Socket>()
    let connectionCount = 0
    const server = createServer(async (request, response) => {
      const body = await readRequestBody(request)
      requests.push({
        body,
        method: request.method,
        path: request.url,
        rawHeaders: [...request.rawHeaders],
      })
      requestSockets.add(request.socket)

      if (request.url === '/health/ready') {
        response.setHeader('Taskdeck-Dev-Run-Id', DEV_RUN_ID)
        response.setHeader('Cache-Control', 'no-store')
        response.statusCode = 200
        response.end()
        return
      }

      response.setHeader('Content-Type', 'application/json')
      response.statusCode = 200
      response.end('{"ok":true}')
    })
    const sockets = trackSockets(server)
    server.on('connection', () => {
      connectionCount += 1
    })
    const port = await listenOnLoopback(server)
    const transport = createRunBoundApiTransport({
      apiBaseUrl: `http://127.0.0.1:${port}/api`,
      expectedRunId: DEV_RUN_ID,
    })

    try {
      await transport.verifyReady()
      const first = await transport.fetch(`http://127.0.0.1:${port}/api/first`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: '{"name":"first"}',
      })
      const second = await transport.fetch(`http://127.0.0.1:${port}/api/second`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: '{"name":"second"}',
      })

      expect(first.status).toBe(200)
      expect(await first.text()).toBe('{"ok":true}')
      expect(second.status).toBe(200)
      expect(await second.text()).toBe('{"ok":true}')
      transport.assertActive()
      expect(connectionCount).toBe(1)
      expect(requestSockets.size).toBe(1)
      expect(transport.diagnostics).toEqual({ physicalConnectionCount: 1, refusedConnectionCount: 0 })
      expect(requests.map(({ method, path }) => `${method} ${path}`)).toEqual([
        'GET /health/ready',
        'POST /api/first',
        'POST /api/second',
      ])
      expect(requests.every(({ body, path }) => !`${path}\n${body}`.includes(DEV_RUN_ID))).toBe(true)
      expect(
        requests.every(({ rawHeaders }) =>
          rawHeaders.every(
            (value, index) => index % 2 === 1 || value.toLowerCase() !== 'taskdeck-dev-run-id',
          ),
        ),
      ).toBe(true)
    } finally {
      transport.destroy()
      await closeServer(server, sockets)
    }
  })

  it.each([
    ['missing', undefined],
    ['mismatched', OTHER_DEV_RUN_ID],
    ['duplicate', [DEV_RUN_ID, DEV_RUN_ID]],
  ])('rejects an HTTP 200 readiness response with a %s run-ID header before mutation', async (_label, runIdHeader) => {
    let mutationCount = 0
    const server = createServer((request, response) => {
      if (request.url === '/health/ready') {
        if (runIdHeader !== undefined) {
          response.setHeader('Taskdeck-Dev-Run-Id', runIdHeader)
        }
        response.statusCode = 200
        response.end()
        return
      }

      mutationCount += 1
      response.statusCode = 200
      response.end()
    })
    const sockets = trackSockets(server)
    const port = await listenOnLoopback(server)
    const transport = createRunBoundApiTransport({
      apiBaseUrl: `http://127.0.0.1:${port}/api`,
      expectedRunId: DEV_RUN_ID,
    })

    try {
      await expect(
        (async () => {
          await transport.verifyReady()
          await transport.fetch(`http://127.0.0.1:${port}/api/mutate`, {
            method: 'POST',
            body: '{"mutate":true}',
          })
        })(),
      ).rejects.toThrow('one exact matching development run ID')
      expect(mutationCount).toBe(0)
    } finally {
      transport.destroy()
      await closeServer(server, sockets)
    }
  })

  it('does not follow a readiness redirect', async () => {
    let redirectedRequestCount = 0
    const server = createServer((request, response) => {
      if (request.url === '/health/ready') {
        response.statusCode = 302
        response.setHeader('Location', '/redirected')
        response.end()
        return
      }

      redirectedRequestCount += 1
      response.setHeader('Taskdeck-Dev-Run-Id', DEV_RUN_ID)
      response.statusCode = 200
      response.end()
    })
    const sockets = trackSockets(server)
    const port = await listenOnLoopback(server)
    const transport = createRunBoundApiTransport({
      apiBaseUrl: `http://127.0.0.1:${port}/api`,
      expectedRunId: DEV_RUN_ID,
    })

    try {
      await expect(transport.verifyReady()).rejects.toThrow('one exact matching development run ID')
      expect(redirectedRequestCount).toBe(0)
    } finally {
      transport.destroy()
      await closeServer(server, sockets)
    }
  })

  it('rejects an incomplete readiness response', async () => {
    const server = createServer((_request, response) => {
      response.setHeader('Taskdeck-Dev-Run-Id', DEV_RUN_ID)
      response.setHeader('Content-Length', '10')
      response.setHeader('Connection', 'close')
      response.statusCode = 200
      response.end('short')
    })
    const sockets = trackSockets(server)
    const port = await listenOnLoopback(server)
    const transport = createRunBoundApiTransport({
      apiBaseUrl: `http://127.0.0.1:${port}/api`,
      expectedRunId: DEV_RUN_ID,
    })

    try {
      await expect(transport.verifyReady()).rejects.toThrow(/response|socket/i)
    } finally {
      transport.destroy()
      await closeServer(server, sockets)
    }
  })

  it('bounds a stalled readiness response before any seed mutation without reconnecting', async () => {
    let mutationCount = 0
    let announceReadinessRequest: () => void
    const readinessRequest = new Promise<void>((resolve) => {
      announceReadinessRequest = resolve
    })
    const server = createServer((request, response) => {
      if (request.url === '/health/ready') {
        announceReadinessRequest()
        return
      }

      mutationCount += 1
      response.statusCode = 200
      response.end()
    })
    const sockets = trackSockets(server)
    let connectionCount = 0
    server.on('connection', () => {
      connectionCount += 1
    })
    const port = await listenOnLoopback(server)
    const deadline = createManualResponseDeadline()
    const transport = createRunBoundApiTransport({
      apiBaseUrl: `http://127.0.0.1:${port}/api`,
      expectedRunId: DEV_RUN_ID,
      responseDeadlineMs: 123,
      ...deadline,
    })

    try {
      const readiness = transport.verifyReady()
      await readinessRequest
      deadline.fireNext()

      await expect(readiness).rejects.toThrow('123ms deadline')
      await expect(transport.fetch(`http://127.0.0.1:${port}/api/mutate`, { method: 'POST' })).rejects.toThrow(
        '123ms deadline',
      )
      expect(mutationCount).toBe(0)
      expect(connectionCount).toBe(1)
      expect(transport.diagnostics).toEqual({ physicalConnectionCount: 1, refusedConnectionCount: 0 })
    } finally {
      transport.destroy()
      await closeServer(server, sockets)
    }
  })

  it('fails a stalled post-proof response without sending a replacement-listener request', async () => {
    let announceStalledSeedRequest: () => void
    const stalledSeedRequest = new Promise<void>((resolve) => {
      announceStalledSeedRequest = resolve
    })
    const ownerRequests: string[] = []
    const owner = createServer((request, response) => {
      ownerRequests.push(request.url || '')
      if (request.url === '/health/ready') {
        response.setHeader('Taskdeck-Dev-Run-Id', DEV_RUN_ID)
        response.statusCode = 200
        response.end()
        return
      }

      announceStalledSeedRequest()
    })
    const ownerSockets = trackSockets(owner)
    let ownerConnectionCount = 0
    owner.on('connection', () => {
      ownerConnectionCount += 1
    })
    const port = await listenOnLoopback(owner)
    const deadline = createManualResponseDeadline()
    const transport = createRunBoundApiTransport({
      apiBaseUrl: `http://127.0.0.1:${port}/api`,
      expectedRunId: DEV_RUN_ID,
      responseDeadlineMs: 456,
      ...deadline,
    })

    let foreign: Server | undefined
    let foreignSockets: Set<Socket> | undefined
    try {
      await transport.verifyReady()
      const stalledSeed = transport.fetch(`http://127.0.0.1:${port}/api/mutate`, {
        method: 'POST',
        body: '{"mutate":true}',
      })
      await stalledSeedRequest
      deadline.fireNext()

      await expect(stalledSeed).rejects.toThrow('456ms deadline')
      expect(ownerRequests).toEqual(['/health/ready', '/api/mutate'])
      expect(ownerConnectionCount).toBe(1)
      expect(transport.diagnostics).toEqual({ physicalConnectionCount: 1, refusedConnectionCount: 0 })

      await closeServer(owner, ownerSockets)
      let foreignRequestCount = 0
      foreign = createServer((_request, response) => {
        foreignRequestCount += 1
        response.statusCode = 200
        response.end()
      })
      foreignSockets = trackSockets(foreign)
      await listenOnLoopback(foreign, port)

      await expect(transport.fetch(`http://127.0.0.1:${port}/api/replacement`, { method: 'POST' })).rejects.toThrow(
        '456ms deadline',
      )
      expect(foreignRequestCount).toBe(0)
      expect(transport.diagnostics).toEqual({ physicalConnectionCount: 1, refusedConnectionCount: 0 })
    } finally {
      transport.destroy()
      if (foreign && foreignSockets) await closeServer(foreign, foreignSockets)
      else await closeServer(owner, ownerSockets)
    }
  })

  it('allows only the live-provider chat message to exceed the normal response deadline', async () => {
    let releaseProviderResponse: (() => void) | undefined
    let announceProviderRequest: () => void
    const providerRequest = new Promise<void>((resolve) => {
      announceProviderRequest = resolve
    })
    const server = createServer((request, response) => {
      if (request.url === '/health/ready') {
        response.setHeader('Taskdeck-Dev-Run-Id', DEV_RUN_ID)
        response.statusCode = 200
        response.end()
        return
      }

      if (request.url === '/api/llm/chat/sessions/session-1/messages') {
        announceProviderRequest()
        releaseProviderResponse = () => {
          response.setHeader('Content-Type', 'application/json')
          response.statusCode = 200
          response.end('{"ok":true}')
        }
        return
      }

      response.statusCode = 200
      response.end()
    })
    const sockets = trackSockets(server)
    const port = await listenOnLoopback(server)
    const deadline = createManualResponseDeadline()
    const transport = createRunBoundApiTransport({
      apiBaseUrl: `http://127.0.0.1:${port}/api`,
      expectedRunId: DEV_RUN_ID,
      responseDeadlineMs: 123,
      ...deadline,
    })

    try {
      await transport.verifyReady()
      const response = transport.fetch(`http://127.0.0.1:${port}/api/llm/chat/sessions/session-1/messages`, {
        method: 'POST',
        body: '{"content":"seed chat"}',
      })
      await providerRequest

      expect(deadline.pendingMilliseconds).toEqual([65_000])
      deadline.advanceBy(10_001)
      expect(deadline.pendingMilliseconds).toEqual([65_000])
      expect(transport.diagnostics).toEqual({ physicalConnectionCount: 1, refusedConnectionCount: 0 })

      releaseProviderResponse?.()
      await expect(response).resolves.toMatchObject({ status: 200 })
      expect(deadline.pendingMilliseconds).toEqual([])
    } finally {
      transport.destroy()
      await closeServer(server, sockets)
    }
  })

  it('fails closed when run-bound requests overlap', async () => {
    let releaseReadiness: (() => void) | undefined
    let announceReadinessRequest: () => void
    const readinessRequest = new Promise<void>((resolve) => {
      announceReadinessRequest = resolve
    })
    const server = createServer((_request, response) => {
      announceReadinessRequest()
      releaseReadiness = () => {
        response.setHeader('Taskdeck-Dev-Run-Id', DEV_RUN_ID)
        response.statusCode = 200
        response.end()
      }
    })
    const sockets = trackSockets(server)
    const port = await listenOnLoopback(server)
    const transport = createRunBoundApiTransport({
      apiBaseUrl: `http://127.0.0.1:${port}/api`,
      expectedRunId: DEV_RUN_ID,
    })

    try {
      const firstProof = transport.verifyReady()
      const firstProofRejection = expect(firstProof).rejects.toThrow()
      await readinessRequest
      await expect(transport.verifyReady()).rejects.toThrow('Concurrent run-bound demo seed requests')
      await firstProofRejection
    } finally {
      releaseReadiness?.()
      transport.destroy()
      await closeServer(server, sockets)
    }
  })

  it('never reconnects seed mutations to a foreign listener that takes over the verified port', async () => {
    let ownerConnectionCount = 0
    const owner = createServer((_request, response) => {
      response.setHeader('Taskdeck-Dev-Run-Id', DEV_RUN_ID)
      response.statusCode = 200
      response.end()
    })
    const ownerSockets = trackSockets(owner)
    owner.on('connection', () => {
      ownerConnectionCount += 1
    })
    const port = await listenOnLoopback(owner)
    const transport = createRunBoundApiTransport({
      apiBaseUrl: `http://127.0.0.1:${port}/api`,
      expectedRunId: DEV_RUN_ID,
    })

    await transport.verifyReady()
    await closeServer(owner, ownerSockets)

    let foreignRequestCount = 0
    const foreign = createServer((_request, response) => {
      foreignRequestCount += 1
      response.statusCode = 200
      response.end()
    })
    const foreignSockets = trackSockets(foreign)
    await listenOnLoopback(foreign, port)

    try {
      await expect(
        transport.fetch(`http://127.0.0.1:${port}/api/mutate`, {
          method: 'POST',
          body: '{"mutate":true}',
        }),
      ).rejects.toThrow()
      expect(ownerConnectionCount).toBe(1)
      expect(transport.diagnostics.physicalConnectionCount).toBe(1)
      expect(foreignRequestCount).toBe(0)
    } finally {
      transport.destroy()
      await closeServer(foreign, foreignSockets)
    }
  })

  it.each([
    '',
    '00000000-0000-0000-0000-000000000000',
    DEV_RUN_ID.toUpperCase(),
    `{${DEV_RUN_ID}}`,
    'not-a-guid',
  ])('rejects invalid or noncanonical run ID %j before networking', (expectedRunId) => {
    expect(() =>
      createRunBoundApiTransport({
        apiBaseUrl: 'http://127.0.0.1:1/api',
        expectedRunId,
      }),
    ).toThrow('canonical lowercase GUID-D')
  })

  it.each([
    ['https target', { apiBaseUrl: 'https://127.0.0.1:1/api' }],
    ['non-loopback target', { apiBaseUrl: 'http://example.com/api' }],
    ['non-local override', { apiBaseUrl: 'http://127.0.0.1:1/api', allowNonLocal: true }],
  ])('rejects a %s before networking', (_label, options) => {
    expect(() => createRunBoundApiTransport({ ...options, expectedRunId: DEV_RUN_ID })).toThrow()
  })

  it('keeps the legacy transport disabled when no development run ID is present', () => {
    expect(
      createRunBoundApiTransportFromEnvironment({
        environment: {},
        apiBaseUrl: 'http://127.0.0.1:1/api',
      }),
    ).toBeNull()
  })
})

describe('parseSeedArgs', () => {
  it('detects --help flag', () => {
    expect(parseSeedArgs(['node', 'demo-seed.mjs', '--help'])).toEqual({ help: true, reset: false })
  })

  it('detects -h shorthand', () => {
    expect(parseSeedArgs(['node', 'demo-seed.mjs', '-h'])).toEqual({ help: true, reset: false })
  })

  it('detects --reset flag', () => {
    expect(parseSeedArgs(['node', 'demo-seed.mjs', '--reset'])).toEqual({ help: false, reset: true })
  })

  it('detects both flags together', () => {
    expect(parseSeedArgs(['node', 'demo-seed.mjs', '--reset', '--help'])).toEqual({ help: true, reset: true })
  })

  it('returns defaults when no flags are passed', () => {
    expect(parseSeedArgs(['node', 'demo-seed.mjs'])).toEqual({ help: false, reset: false })
  })

  it('ignores unknown flags', () => {
    expect(parseSeedArgs(['node', 'demo-seed.mjs', '--verbose', '--dry-run'])).toEqual({ help: false, reset: false })
  })
})
