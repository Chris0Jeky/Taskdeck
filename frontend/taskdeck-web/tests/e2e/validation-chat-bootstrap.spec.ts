import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

// --- DTO types ---

interface ChatSessionListDto {
  id: string
  title: string
}

// --- Tests ---

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'slicec-chat')
})

test.describe('TST09 Chat Session Behavior', () => {
  test('SC-001: create chat session without board context', async ({ page }) => {
    await page.goto('/workspace/automations/chat')

    const sessionTitle = `General Chat ${Date.now()}`
    await page.getByPlaceholder('Session title').fill(sessionTitle)
    await page.getByRole('button', { name: 'Create Session' }).click()

    // Session should appear and be selectable
    await expect(page.getByText(sessionTitle).first()).toBeVisible()

    // Chat input should be available
    await expect(page.getByPlaceholder('Describe an automation instruction...')).toBeVisible()
  })

  test('SC-002: create board-scoped chat session', async ({ page, request }) => {
    const seed = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const boardId = await createBoardWithColumn(request, auth, seed, {
      boardNamePrefix: 'SliceC Chat',
      description: 'chat bootstrap validation board',
      columnNamePrefix: 'Backlog',
    })

    await page.goto('/workspace/automations/chat')

    const sessionTitle = `Board Scoped ${seed}`
    await page.getByPlaceholder('Session title').fill(sessionTitle)
    await page.getByPlaceholder('Board context (optional)').fill(boardId)
    await page.getByRole('button', { name: 'Create Session' }).click()

    await expect(page.getByText(sessionTitle).first()).toBeVisible()
  })

  test('SC-003: non-actionable greeting returns assistant response, no proposal', async ({ page }) => {
    await page.goto('/workspace/automations/chat')

    await page.getByPlaceholder('Session title').fill(`Greeting ${Date.now()}`)
    await page.getByRole('button', { name: 'Create Session' }).click()

    await page.getByPlaceholder('Describe an automation instruction...').fill('Hello, how are you?')
    await page.getByRole('button', { name: 'Send Message' }).click()

    // Wait for assistant response
    await expect(page.getByText('Assistant').first()).toBeVisible()

    // No proposal reference should appear (no .td-message-proposal visible)
    const proposalBadges = page.locator('.td-message-proposal')
    const count = await proposalBadges.count()
    expect(count).toBe(0)
  })

  test('SC-005: actionable prompt with proposal generation creates proposal without mutating board', async ({
    page,
    request,
  }) => {
    const seed = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const boardId = await createBoardWithColumn(request, auth, seed, {
      boardNamePrefix: 'SliceC Actionable',
      description: 'actionable prompt validation board',
      columnNamePrefix: 'Todo',
    })

    const uniqueCardTitle = `Chat Card ${seed}`

    await page.goto('/workspace/automations/chat')
    await page.getByPlaceholder('Session title').fill(`Actionable ${seed}`)
    await page.getByPlaceholder('Board context (optional)').fill(boardId)
    await page.getByRole('button', { name: 'Create Session' }).click()

    await page.getByPlaceholder('Describe an automation instruction...').fill(`create card "${uniqueCardTitle}"`)
    const requestProposalCheckbox = page.getByRole('checkbox', { name: 'Request proposal generation' })
    await requestProposalCheckbox.check()
    await page.getByRole('button', { name: 'Send Message' }).click()

    // Wait for assistant response
    await expect(page.getByText('Assistant').first()).toBeVisible()

    // Verify board state unchanged (GP-06 compliance)
    const cardsResponse = await request.get(
      `${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/cards`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )
    await assertOk(cardsResponse, 'list cards after proposal')
    const cards = (await cardsResponse.json()) as Array<{ title: string }>
    expect(cards.some((c) => c.title === uniqueCardTitle)).toBeFalsy()
  })

  test('SC-038: LLM health banner displays correct state for mock provider', async ({ page }) => {
    await page.goto('/workspace/automations/chat')

    // Mock provider should show the mock state
    await expect(page.locator('[data-llm-health-state="mock"]')).toBeVisible()
    await expect(page.getByText('Live LLM not active')).toBeVisible()
  })
})

test.describe('TST09 Adversarial Chat Inputs', () => {
  test('SC-011: XSS payload is escaped, not executed', async ({ page }) => {
    // Register dialog handler BEFORE any navigation to catch all dialogs
    let dialogTriggered = false
    page.on('dialog', async (dialog: { dismiss: () => Promise<void> }) => {
      dialogTriggered = true
      await dialog.dismiss()
    })

    await page.goto('/workspace/automations/chat')

    await page.getByPlaceholder('Session title').fill(`XSS Test ${Date.now()}`)
    await page.getByRole('button', { name: 'Create Session' }).click()

    const xssPayload = '<script>alert("xss")</script>'
    await page.getByPlaceholder('Describe an automation instruction...').fill(xssPayload)
    await page.getByRole('button', { name: 'Send Message' }).click()

    // Wait for assistant response (also gives time for any deferred script execution)
    await expect(page.getByText('Assistant').first()).toBeVisible()
    expect(dialogTriggered).toBeFalsy()

    // The XSS payload should be rendered as literal text, not executed as HTML
    await expect(page.getByText(xssPayload)).toBeVisible()
  })

  test('SC-012: SQL injection payload stored as plain text', async ({ page, request }) => {
    await page.goto('/workspace/automations/chat')

    await page.getByPlaceholder('Session title').fill(`SQL Injection ${Date.now()}`)
    await page.getByRole('button', { name: 'Create Session' }).click()

    const sqlPayload = "'; DROP TABLE ChatMessages; --"
    await page.getByPlaceholder('Describe an automation instruction...').fill(sqlPayload)
    await page.getByRole('button', { name: 'Send Message' }).click()

    // Wait for assistant response — system should not crash
    await expect(page.getByText('Assistant').first()).toBeVisible()

    // Verify sessions still work (database not corrupted)
    const sessionsResponse = await request.get(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    await assertOk(sessionsResponse, 'list sessions after SQL injection attempt')
    const sessions = (await sessionsResponse.json()) as ChatSessionListDto[]
    expect(sessions.length).toBeGreaterThan(0)
  })

  test('SC-013: HTML injection in session title is escaped', async ({ page }) => {
    // Register dialog handler BEFORE navigation to catch onerror-triggered alerts
    let dialogTriggered = false
    page.on('dialog', async (dialog: { dismiss: () => Promise<void> }) => {
      dialogTriggered = true
      await dialog.dismiss()
    })

    await page.goto('/workspace/automations/chat')

    const htmlPayload = '<img src=x onerror=alert(1)>'
    await page.getByPlaceholder('Session title').fill(htmlPayload)
    await page.getByRole('button', { name: 'Create Session' }).click()

    // Wait for the session title to appear in the UI (confirms rendering is complete).
    // Use .first() because the title renders in both the sidebar and the header.
    await expect(page.getByText(htmlPayload).first()).toBeVisible()
    expect(dialogTriggered).toBeFalsy()

    // Verify via API that the session title was stored as plain text
    const sessionsResponse = await page.request.get(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    await assertOk(sessionsResponse, 'list sessions after HTML injection title')
    const sessions = (await sessionsResponse.json()) as ChatSessionListDto[]
    const matchingSession = sessions.find((s) => s.title === htmlPayload)
    expect(matchingSession).toBeTruthy()
  })

  test('SC-010: extremely long message does not crash the system', async ({ request }) => {
    // Create session via API (avoids UI rendering timeout for large payloads)
    const sessionResponse = await request.post(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { title: `Long Message ${Date.now()}` },
    })
    await assertOk(sessionResponse, 'create chat session')
    const session = (await sessionResponse.json()) as { id: string }

    // Send a 10,000+ character message — the system should handle it
    // gracefully. The backend may accept it (2xx) or reject it with a
    // validation error (400) if a max message length is enforced.
    // Either outcome is correct; only a 5xx would indicate a crash.
    const longMessage = 'A'.repeat(10_500)
    const msgResponse = await request.post(
      `${API_BASE_URL}/llm/chat/sessions/${encodeURIComponent(session.id)}/messages`,
      {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { content: longMessage },
      },
    )
    expect(msgResponse.status()).toBeLessThan(500)

    // Verify system is still functional — sessions endpoint responds
    const sessionsResponse = await request.get(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    expect(sessionsResponse.ok()).toBeTruthy()
  })
})

test.describe('TST09 Chat Session Cross-User Isolation', () => {
  test('SC-030: different users see only their own sessions', async ({ request }) => {
    // UserA creates a session
    const sessionATitle = `UserA Session ${Date.now()}`
    const sessionAResponse = await request.post(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { title: sessionATitle },
    })
    await assertOk(sessionAResponse, 'create session as UserA')

    // Register UserB
    const unique = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
    const userBRegResponse = await request.post(`${API_BASE_URL}/auth/register`, {
      data: {
        username: `e2e-slicec-chatiso-${unique}`,
        email: `e2e-slicec-chatiso-${unique}@taskdeck.local`,
        password: 'E2ePassword123!',
      },
    })
    expect(userBRegResponse.ok()).toBeTruthy()
    const userB = (await userBRegResponse.json()) as AuthResult

    // UserB lists sessions — should not see UserA's session
    const userBSessions = await request.get(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${userB.token}` },
    })
    await assertOk(userBSessions, 'list sessions as UserB')
    const sessionList = (await userBSessions.json()) as ChatSessionListDto[]
    expect(sessionList.find((s) => s.title === sessionATitle)).toBeUndefined()

    // UserA lists sessions — should see their own session
    const userASessions = await request.get(`${API_BASE_URL}/llm/chat/sessions`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    await assertOk(userASessions, 'list sessions as UserA')
    const userASessionList = (await userASessions.json()) as ChatSessionListDto[]
    expect(userASessionList.find((s) => s.title === sessionATitle)).toBeTruthy()
  })
})
