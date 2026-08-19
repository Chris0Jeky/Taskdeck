import type { APIRequestContext, Page } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'
import { expectDialog } from './support/dialogs'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'
import { pollUntil } from './support/polling'

// --- DTO types ---

interface ChatMessageDto {
  proposalId: string | null
}

interface ChatSessionDto {
  recentMessages: ChatMessageDto[]
}

interface ProposalDto {
  id: string
  summary: string
  status: string
}

// --- Helpers ---

async function createBoardScoped(
  request: APIRequestContext,
  auth: AuthResult,
  seed: string,
): Promise<string> {
  return createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'SliceC Proposals',
    description: 'automation proposal validation board',
    columnNamePrefix: 'Todo',
  })
}

async function createChatSessionAndSendProposal(
  page: Page,
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
  instruction: string,
  seed: string,
): Promise<{ sessionId: string; proposalId: string }> {
  await page.goto('/workspace/automations/chat')
  await page.getByPlaceholder('Session title').fill(`Proposal Validation ${seed}`)
  await page.getByPlaceholder('Board context (optional)').fill(boardId)
  await page.getByRole('button', { name: 'Create Session' }).click()

  const sessionId = await page.locator('.paper-chat__meta').first().getAttribute('data-session-id')
  if (!sessionId) throw new Error('Expected chat session header to expose data-session-id')

  await page.getByPlaceholder('Describe an automation instruction...').fill(instruction)
  const requestProposalCheckbox = page.getByRole('checkbox', { name: 'Request proposal generation' })
  await requestProposalCheckbox.check()
  await expect(requestProposalCheckbox).toBeChecked()
  await page.getByRole('button', { name: 'Send Message' }).click()

  const proposalId = await waitForProposalInSession(request, auth.token, sessionId)
  return { sessionId, proposalId }
}

async function waitForProposalInSession(
  request: APIRequestContext,
  token: string,
  sessionId: string,
): Promise<string> {
  const sessionWithProposal = await pollUntil(
    async () => {
      const response = await request.get(
        `${API_BASE_URL}/llm/chat/sessions/${encodeURIComponent(sessionId)}`,
        { headers: { Authorization: `Bearer ${token}` } },
      )
      await assertOk(response, `fetch chat session ${sessionId}`)
      return (await response.json()) as ChatSessionDto
    },
    (session) => session.recentMessages.some((m) => !!m.proposalId),
    { description: 'proposal reference in chat session' },
  )

  const proposalId = sessionWithProposal.recentMessages.find((m) => !!m.proposalId)?.proposalId
  if (!proposalId) throw new Error('Expected a proposal reference in chat session')
  return proposalId
}

async function fetchProposal(
  request: APIRequestContext,
  token: string,
  proposalId: string,
): Promise<ProposalDto> {
  const response = await request.get(
    `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}`,
    { headers: { Authorization: `Bearer ${token}` } },
  )
  await assertOk(response, `fetch proposal ${proposalId}`)
  return (await response.json()) as ProposalDto
}

// --- Tests ---

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'slicec-proposals', { theme: 'legacy' })
})

test.describe('TST09 Proposal Lifecycle', () => {
  test('SC-015: approve then execute golden path — board state updates', async ({ page, request }) => {
    const seed = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const boardId = await createBoardScoped(request, auth, seed)
    const uniqueCardTitle = `Proposal Card ${seed}`

    const { proposalId } = await createChatSessionAndSendProposal(
      page,
      request,
      auth,
      boardId,
      `create card "${uniqueCardTitle}"`,
      seed,
    )

    // Verify board has no extra cards yet (GP-06: no silent mutation)
    const cardsBeforeApprove = await request.get(
      `${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/cards`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )
    await assertOk(cardsBeforeApprove, 'list cards before approve')
    const cardsBefore = (await cardsBeforeApprove.json()) as Array<{ title: string }>
    expect(cardsBefore.some((c) => c.title === uniqueCardTitle)).toBeFalsy()

    // Navigate to review and approve
    await page.goto('/workspace/review')
    await expect(page.getByRole('heading', { name: 'Review', exact: true })).toBeVisible()

    const proposal = await fetchProposal(request, auth.token, proposalId)
    const proposalCard = page.locator('.td-review-card').filter({ hasText: proposal.summary }).first()
    await expect(proposalCard).toBeVisible()

    await proposalCard.getByRole('button', { name: 'Approve for board' }).click()
    await expect(proposalCard.getByText('Approved, ready to apply')).toBeVisible()

    // Execute
    await expectDialog(page, () => proposalCard.getByRole('button', { name: 'Apply to board' }).click(), {
      type: 'confirm',
      message: 'Apply this approved proposal to the board now?',
    })
    await expect(proposalCard).not.toBeVisible()

    // Verify card now exists on the board
    const cardsAfterExecute = await pollUntil(
      async () => {
        const r = await request.get(
          `${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/cards`,
          { headers: { Authorization: `Bearer ${auth.token}` } },
        )
        await assertOk(r, 'list cards after execute')
        return (await r.json()) as Array<{ title: string }>
      },
      (cards) => cards.some((c) => c.title === uniqueCardTitle),
      { description: `card "${uniqueCardTitle}" to appear on board after proposal execution` },
    )
    expect(cardsAfterExecute.some((c) => c.title === uniqueCardTitle)).toBeTruthy()
  })

  test('SC-016/017: reject proposal — board state unchanged', async ({ page, request }) => {
    const seed = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const boardId = await createBoardScoped(request, auth, seed)
    const uniqueCardTitle = `Rejected Card ${seed}`

    // Get initial card count
    const cardsInitial = await request.get(
      `${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/cards`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )
    await assertOk(cardsInitial, 'list initial cards')
    const initialCards = (await cardsInitial.json()) as Array<{ title: string }>
    const initialCount = initialCards.length

    const { proposalId } = await createChatSessionAndSendProposal(
      page,
      request,
      auth,
      boardId,
      `create card "${uniqueCardTitle}"`,
      seed,
    )

    // Reject via API
    const rejectResponse = await request.post(
      `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}/reject`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { reason: 'Test rejection for validation' },
      },
    )
    await assertOk(rejectResponse, 'reject proposal')

    // Verify proposal is rejected (status may be numeric or string depending on serialization)
    const rejectedProposal = await fetchProposal(request, auth.token, proposalId)
    const status = String(rejectedProposal.status)
    expect(status).toMatch(/rejected|2/i)

    // Verify board state unchanged
    const cardsAfterReject = await request.get(
      `${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/cards`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )
    await assertOk(cardsAfterReject, 'list cards after reject')
    const afterCards = (await cardsAfterReject.json()) as Array<{ title: string }>
    expect(afterCards.length).toBe(initialCount)
    expect(afterCards.some((c) => c.title === uniqueCardTitle)).toBeFalsy()
  })

  test('SC-018: double-approve returns error', async ({ request }) => {
    const seed = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const boardId = await createBoardScoped(request, auth, seed)

    // Create a proposal via API (chat session)
    const sessionResponse = await request.post(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { title: `DoubleApprove ${seed}`, boardId },
    })
    await assertOk(sessionResponse, 'create chat session')
    const session = (await sessionResponse.json()) as { id: string }

    const msgResponse = await request.post(
      `${API_BASE_URL}/llm/chat/sessions/${encodeURIComponent(session.id)}/messages`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { content: `create card "DoubleApprove Card ${seed}"`, requestProposal: true },
      },
    )
    await assertOk(msgResponse, 'send chat message')

    const proposalId = await waitForProposalInSession(request, auth.token, session.id)

    // First approve — should succeed
    const approve1 = await request.post(
      `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}/approve`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )
    await assertOk(approve1, 'first approve')

    // Second approve — should return 409 (InvalidOperation: already approved)
    const approve2 = await request.post(
      `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}/approve`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )
    expect(approve2.status()).toBe(409)
  })

  test('SC-019: double-execute prevention', async ({ request }) => {
    const seed = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const boardId = await createBoardScoped(request, auth, seed)

    // Create, approve, and execute a proposal
    const sessionResponse = await request.post(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { title: `DoubleExec ${seed}`, boardId },
    })
    await assertOk(sessionResponse, 'create chat session')
    const session = (await sessionResponse.json()) as { id: string }

    const msgResponse = await request.post(
      `${API_BASE_URL}/llm/chat/sessions/${encodeURIComponent(session.id)}/messages`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { content: `create card "DoubleExec Card ${seed}"`, requestProposal: true },
      },
    )
    await assertOk(msgResponse, 'send chat message')

    const proposalId = await waitForProposalInSession(request, auth.token, session.id)

    // Approve
    const approveResp = await request.post(
      `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}/approve`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )
    await assertOk(approveResp, 'approve proposal')

    // First execute
    const exec1 = await request.post(
      `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}/execute`,
      {
        headers: {
          Authorization: `Bearer ${auth.token}`,
          'Idempotency-Key': `exec1-${seed}`,
        },
      },
    )
    await assertOk(exec1, 'first execute')

    // Second execute — the endpoint is idempotent by design: re-executing an
    // already-applied proposal returns 200 (not 4xx). Verify it succeeds
    // idempotently and the proposal remains in Applied state.
    const exec2 = await request.post(
      `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}/execute`,
      {
        headers: {
          Authorization: `Bearer ${auth.token}`,
          'Idempotency-Key': `exec2-${seed}`,
        },
      },
    )
    expect(exec2.ok()).toBeTruthy()

    // Confirm proposal is still in Applied status (not re-applied or duplicated)
    const afterSecondExec = await fetchProposal(request, auth.token, proposalId)
    const appliedStatus = String(afterSecondExec.status)
    expect(appliedStatus).toMatch(/applied|3/i)

    // Verify board mutations were not duplicated — card count should match
    // what the first execute created (idempotent re-execute must be a no-op)
    const cardsAfter = await request.get(
      `${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/cards`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )
    await assertOk(cardsAfter, 'list cards after double-execute')
    const cardList = (await cardsAfter.json()) as Array<{ title: string }>
    const matchingCards = cardList.filter((c) => c.title.includes(`DoubleExec Card ${seed}`))
    expect(matchingCards).toHaveLength(1)
  })

  test('SC-020: execute without Idempotency-Key returns 400', async ({ request }) => {
    const seed = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const boardId = await createBoardScoped(request, auth, seed)

    // Create and approve a proposal
    const sessionResponse = await request.post(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { title: `NoIdemKey ${seed}`, boardId },
    })
    await assertOk(sessionResponse, 'create chat session')
    const session = (await sessionResponse.json()) as { id: string }

    const msgResponse = await request.post(
      `${API_BASE_URL}/llm/chat/sessions/${encodeURIComponent(session.id)}/messages`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { content: `create card "NoIdemKey Card ${seed}"`, requestProposal: true },
      },
    )
    await assertOk(msgResponse, 'send chat message')

    const proposalId = await waitForProposalInSession(request, auth.token, session.id)

    await assertOk(
      await request.post(
        `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}/approve`,
        { headers: { Authorization: `Bearer ${auth.token}` } },
      ),
      'approve proposal',
    )

    // Execute without Idempotency-Key header
    const execNoKey = await request.post(
      `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}/execute`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )
    expect(execNoKey.status()).toBe(400)

    const body = (await execNoKey.json()) as { errorCode?: string }
    expect(body.errorCode).toBeTruthy()
  })

  test('SC-043: execute without prior approve returns error', async ({ request }) => {
    const seed = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const boardId = await createBoardScoped(request, auth, seed)

    const sessionResponse = await request.post(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { title: `NoApprove ${seed}`, boardId },
    })
    await assertOk(sessionResponse, 'create chat session')
    const session = (await sessionResponse.json()) as { id: string }

    const msgResponse = await request.post(
      `${API_BASE_URL}/llm/chat/sessions/${encodeURIComponent(session.id)}/messages`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { content: `create card "NoApprove Card ${seed}"`, requestProposal: true },
      },
    )
    await assertOk(msgResponse, 'send chat message')

    const proposalId = await waitForProposalInSession(request, auth.token, session.id)

    // Attempt to execute without approving
    const execResponse = await request.post(
      `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}/execute`,
      {
        headers: {
          Authorization: `Bearer ${auth.token}`,
          'Idempotency-Key': `noapprove-${seed}`,
        },
      },
    )
    expect(execResponse.status()).toBeGreaterThanOrEqual(400)
    expect(execResponse.status()).toBeLessThan(500)
  })
})

test.describe('TST09 Cross-User Proposal Isolation', () => {
  test('SC-028/029: UserB cannot see or approve UserA proposals', async ({ request }) => {
    const seed = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const boardId = await createBoardScoped(request, auth, seed)

    // Create a proposal as UserA
    const sessionResponse = await request.post(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { title: `Isolation ${seed}`, boardId },
    })
    await assertOk(sessionResponse, 'create chat session as UserA')
    const session = (await sessionResponse.json()) as { id: string }

    const msgResponse = await request.post(
      `${API_BASE_URL}/llm/chat/sessions/${encodeURIComponent(session.id)}/messages`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { content: `create card "Isolated Card ${seed}"`, requestProposal: true },
      },
    )
    await assertOk(msgResponse, 'send chat message as UserA')

    const proposalId = await waitForProposalInSession(request, auth.token, session.id)

    // Register UserB
    const unique = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const userBRegResponse = await request.post(`${API_BASE_URL}/auth/register`, {
      data: {
        username: `e2e-slicec-userb-${unique}`,
        email: `e2e-slicec-userb-${unique}@taskdeck.local`,
        password: 'E2ePassword123!',
      },
    })
    expect(userBRegResponse.ok()).toBeTruthy()
    const userB = (await userBRegResponse.json()) as AuthResult

    // UserB lists proposals — should not see UserA's
    const userBProposals = await request.get(`${API_BASE_URL}/automation/proposals`, {
      headers: { Authorization: `Bearer ${userB.token}` },
    })
    await assertOk(userBProposals, 'list proposals as UserB')
    const proposalList = (await userBProposals.json()) as ProposalDto[]
    expect(proposalList.find((p) => p.id === proposalId)).toBeUndefined()

    // UserB attempts to approve UserA's proposal — should get 403 (authenticated but unauthorized)
    const approveAsB = await request.post(
      `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}/approve`,
      { headers: { Authorization: `Bearer ${userB.token}` } },
    )
    expect(approveAsB.status()).toBe(403)
  })

  test('SC-030: UserB cannot see UserA chat sessions', async ({ request }) => {
    const seed = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`

    // Create a session as UserA
    const sessionResponse = await request.post(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { title: `UserA Only ${seed}` },
    })
    await assertOk(sessionResponse, 'create chat session as UserA')
    const session = (await sessionResponse.json()) as { id: string }

    // Register UserB
    const unique = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const userBRegResponse = await request.post(`${API_BASE_URL}/auth/register`, {
      data: {
        username: `e2e-slicec-chat-${unique}`,
        email: `e2e-slicec-chat-${unique}@taskdeck.local`,
        password: 'E2ePassword123!',
      },
    })
    expect(userBRegResponse.ok()).toBeTruthy()
    const userB = (await userBRegResponse.json()) as AuthResult

    // UserB lists sessions — should not see UserA's
    const userBSessions = await request.get(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${userB.token}` },
    })
    await assertOk(userBSessions, 'list sessions as UserB')
    const sessionList = (await userBSessions.json()) as Array<{ id: string }>
    expect(sessionList.find((s) => s.id === session.id)).toBeUndefined()
  })
})

test.describe('TST09 Execution Safety', () => {
  test('SC-042: cannot re-approve a rejected proposal', async ({ request }) => {
    const seed = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const boardId = await createBoardScoped(request, auth, seed)

    const sessionResponse = await request.post(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { title: `ReApprove ${seed}`, boardId },
    })
    await assertOk(sessionResponse, 'create chat session')
    const session = (await sessionResponse.json()) as { id: string }

    const msgResponse = await request.post(
      `${API_BASE_URL}/llm/chat/sessions/${encodeURIComponent(session.id)}/messages`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { content: `create card "ReApprove Card ${seed}"`, requestProposal: true },
      },
    )
    await assertOk(msgResponse, 'send chat message')

    const proposalId = await waitForProposalInSession(request, auth.token, session.id)

    // Reject the proposal
    const rejectResp = await request.post(
      `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}/reject`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { reason: 'Deliberate test rejection' },
      },
    )
    await assertOk(rejectResp, 'reject proposal')

    // Attempt to approve after rejection
    const reApprove = await request.post(
      `${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}/approve`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )
    expect(reApprove.status()).toBeGreaterThanOrEqual(400)
    expect(reApprove.status()).toBeLessThan(500)
  })
})
