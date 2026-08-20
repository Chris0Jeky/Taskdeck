import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname } from 'node:path'
import type { APIRequestContext, APIResponse, Page } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { attachSessionToPage, type AuthResult } from './support/authSession'
import { expectApplyConfirmDialog } from './support/applyConfirm'

type HttpEvidence = { method: string; path: string; status: number }
type ChatSession = { recentMessages: Array<{ proposalId: string | null }> }
type Proposal = { id: string; status: string | number; summary: string; operations: unknown[] }
type BoardPage = { items: Array<{ id: string; name: string }> }
type ProviderHealth = {
  isAvailable: boolean
  providerName: string
  model: string | null
  isMock: boolean
  isProbed: boolean
  verificationStatus: string
}

const apiBaseUrl = requiredEnv('TASKDECK_E2E_API_BASE_URL')
const evidencePath = requiredEnv('TASKDECK_PACKAGED_EVIDENCE_PATH')
const failurePath = requiredEnv('TASKDECK_PACKAGED_FAILURE_PATH')
const journeyId = validatedJourneyId(requiredEnv('TASKDECK_PACKAGED_JOURNEY_ID'))
const phase = requiredEnv('TASKDECK_PACKAGED_JOURNEY_PHASE')
const liveOpenAi = process.env.TASKDECK_PACKAGED_LIVE_OPENAI === '1'
const liveOpenAiSkipReason = requiredEnv('TASKDECK_PACKAGED_LIVE_OPENAI_SKIP_REASON')
const expectedModel = process.env.TASKDECK_PACKAGED_OPENAI_MODEL?.trim() || 'gpt-5.6-luna'

const username = `packaged-${journeyId}`
const email = `${username}@taskdeck.local`
const password = 'PackagedAcceptance123!'
const boardTitle = `Packaged persistence ${journeyId}`
const cardTitle = `Packaged OpenAI card ${journeyId}`
let checkpoint = 'initializing'
let failureHttpStatus: number | null = null

test('untouched Windows package retains its synthetic journey', async ({ page, request }) => {
  try {
    if (phase === 'create') {
      await runCreatePhase(page, request)
      return
    }
    if (phase === 'restart') {
      await runRestartPhase(page, request)
      return
    }

    throw new Error('[packaged desktop] Journey phase must be create or restart.')
  } catch {
    writeFileSync(
      failurePath,
      `${JSON.stringify({ schemaVersion: 1, phase, outcome: 'failed', checkpoint, httpStatus: failureHttpStatus })}\n`,
      { encoding: 'utf8', flag: 'wx' },
    )
    throw new Error(`[packaged desktop] ${phase} failed at a sanitized checkpoint.`)
  }
})

async function runCreatePhase(page: Page, request: APIRequestContext): Promise<void> {
  const http: HttpEvidence[] = []
  checkpoint = 'register'
  const registerResponse = await request.post(`${apiBaseUrl}/auth/register`, {
    data: { username, email, password },
  })
  requireOk(registerResponse, 'POST', '/api/auth/register', http)
  const auth = await readAuth(registerResponse)
  checkpoint = 'attach_session'
  await attachSessionToPage(page, auth, { theme: 'legacy' })

  checkpoint = 'create_board'
  const boardResponse = await request.post(
    `${apiBaseUrl}/import/boards?userId=${encodeURIComponent(auth.user.id)}`,
    {
      headers: authHeader(auth),
      data: {
        name: boardTitle,
        description: 'Synthetic packaged desktop persistence proof',
        columns: [{ name: 'Inbox', position: 0, wipLimit: null }],
        cards: [],
        labels: [],
      },
    },
  )
  requireOk(boardResponse, 'POST', '/api/import/boards', http)
  const boardResult = await boardResponse.json() as { success?: boolean; boardId?: string | null }
  if (boardResult.success !== true || !boardResult.boardId) {
    throw new Error('[packaged desktop] Synthetic board creation returned an invalid result.')
  }
  const boardId = boardResult.boardId

  checkpoint = liveOpenAi ? 'live_provider' : 'offline_evidence'
  const liveEvidence = liveOpenAi
    ? await runLiveOpenAiJourney(page, request, auth, boardId, http)
    : { outcome: 'skipped', reason: validatedLiveSkipReason(liveOpenAiSkipReason) }

  writeEvidence({
    schemaVersion: 1,
    phase: 'create',
    journeyId,
    board: { id: boardId, title: boardTitle },
    persistence: { registered: true, boardCreated: true },
    http,
    liveOpenAi: liveEvidence,
  })
}

async function runRestartPhase(page: Page, request: APIRequestContext): Promise<void> {
  const http: HttpEvidence[] = []
  checkpoint = 'restart_login'
  const loginResponse = await request.post(`${apiBaseUrl}/auth/login`, {
    data: { usernameOrEmail: username, password },
  })
  requireOk(loginResponse, 'POST', '/api/auth/login', http)
  const auth = await readAuth(loginResponse)
  checkpoint = 'restart_attach_session'
  await attachSessionToPage(page, auth, { theme: 'legacy' })

  checkpoint = 'restart_list_boards'
  const boardsResponse = await request.get(`${apiBaseUrl}/boards`, { headers: authHeader(auth) })
  requireOk(boardsResponse, 'GET', '/api/boards', http)
  const boardPage = await boardsResponse.json() as BoardPage
  if (!Array.isArray(boardPage.items)) {
    throw new Error('[packaged desktop] The persisted board listing was invalid after restart.')
  }
  const matchingBoards = boardPage.items.filter(board => board.name === boardTitle)
  if (matchingBoards.length !== 1) {
    throw new Error('[packaged desktop] The persisted synthetic board was not found exactly once after restart.')
  }
  const boardId = matchingBoards[0].id

  let cardCountAfterRestart: number | null = null
  if (liveOpenAi) {
    checkpoint = 'restart_card_persistence'
    cardCountAfterRestart = await matchingCardCount(request, auth, boardId, http)
    if (cardCountAfterRestart !== 1) {
      throw new Error('[packaged desktop] The applied synthetic card was not retained exactly once after restart.')
    }
  }

  writeEvidence({
    schemaVersion: 1,
    phase: 'restart',
    journeyId,
    board: { id: boardId, title: boardTitle },
    persistence: { signedIn: true, boardFound: true },
    http,
    liveOpenAi: liveOpenAi
      ? { outcome: 'passed', cardTitle, cardCountAfterRestart }
      : { outcome: 'skipped', reason: validatedLiveSkipReason(liveOpenAiSkipReason) },
  })
}

async function runLiveOpenAiJourney(
  page: Page,
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
  http: HttpEvidence[],
): Promise<Record<string, unknown>> {
  checkpoint = 'live_load_chat'
  failureHttpStatus = null
  let pageErrorKind: 'none' | 'type' | 'reference' | 'other' = 'none'
  let assetFailureKind: 'none' | 'entry' | 'chat' | 'vendor' | 'other' = 'none'
  const classifyAsset = (pathname: string): typeof assetFailureKind => {
    const name = pathname.split('/').pop() ?? ''
    if (name.startsWith('index-')) return 'entry'
    if (name.startsWith('AutomationChatView-')) return 'chat'
    if (name.startsWith('vendor-')) return 'vendor'
    return 'other'
  }
  page.on('pageerror', error => {
    pageErrorKind = error.name === 'TypeError' ? 'type' : error.name === 'ReferenceError' ? 'reference' : 'other'
  })
  page.on('requestfailed', request => {
    const requestUrl = new URL(request.url())
    const failureText = request.failure()?.errorText ?? ''
    if (
      requestUrl.origin === new URL(apiBaseUrl).origin
      && requestUrl.pathname.startsWith('/assets/')
      && !failureText.includes('ERR_ABORTED')
    ) {
      assetFailureKind = classifyAsset(requestUrl.pathname)
    }
  })
  page.on('response', response => {
    const responseUrl = new URL(response.url())
    if (responseUrl.pathname === '/api/llm/chat/health' && !responseUrl.search.includes('probe=true')) {
      failureHttpStatus = response.status()
    }
    if (
      responseUrl.origin === new URL(apiBaseUrl).origin
      && responseUrl.pathname.startsWith('/assets/')
      && response.status() >= 400
    ) {
      assetFailureKind = classifyAsset(responseUrl.pathname)
    }
  })
  const navigationResponse = await page.goto('/workspace/automations/chat')
  failureHttpStatus = navigationResponse?.status() ?? null
  if (!navigationResponse?.ok()) {
    checkpoint = 'live_spa_route_http'
    throw new Error('[packaged desktop] The packaged SPA route did not return success.')
  }
  const navigationContentType = await navigationResponse.headerValue('content-type')
  if (!navigationContentType?.toLowerCase().includes('text/html')) {
    checkpoint = 'live_spa_route_not_html'
    throw new Error('[packaged desktop] The packaged SPA route did not return HTML.')
  }
  checkpoint = 'live_configured_ui'
  const appRoot = page.locator('body > #app')
  if (await appRoot.count() !== 1) {
    checkpoint = (await page.content()).includes('<title>Taskdeck</title>')
      ? 'live_spa_root_missing'
      : 'live_spa_shell_unexpected'
    throw new Error('[packaged desktop] The packaged SPA root was missing.')
  }
  try {
    await expect.poll(
      async () => await appRoot.evaluate(element => element.childElementCount),
      { timeout: 15_000 },
    ).toBeGreaterThan(0)
  } catch {
    checkpoint = assetFailureKind === 'entry'
      ? 'live_spa_entry_asset_failed'
      : assetFailureKind === 'chat'
        ? 'live_spa_chat_asset_failed'
        : assetFailureKind === 'vendor'
          ? 'live_spa_vendor_asset_failed'
          : assetFailureKind === 'other'
            ? 'live_spa_other_asset_failed'
            : pageErrorKind === 'type'
        ? 'live_spa_type_error'
        : pageErrorKind === 'reference'
          ? 'live_spa_reference_error'
          : pageErrorKind === 'other'
            ? 'live_spa_runtime_error'
            : 'live_spa_not_mounted'
    throw new Error('[packaged desktop] The packaged SPA did not mount for the live journey.')
  }
  const healthState = page.locator('[data-llm-health-state]').first()
  if (await healthState.count() !== 1) {
    checkpoint = 'live_health_ui_missing'
    throw new Error('[packaged desktop] The packaged live-provider status control was unavailable.')
  }
  await expect.poll(
    async () => await healthState.getAttribute('data-llm-health-state'),
    { timeout: 15_000 },
  ).not.toBe('loading')
  const initialHealthState = await healthState.getAttribute('data-llm-health-state')
  if (initialHealthState !== 'configured') {
    checkpoint = initialHealthState === 'mock'
      ? 'live_provider_state_mock'
      : initialHealthState === 'unavailable'
        ? 'live_provider_state_unavailable'
        : initialHealthState === 'error'
          ? 'live_provider_state_error'
          : initialHealthState === 'loading'
            ? 'live_provider_state_loading'
            : initialHealthState === 'verified'
              ? 'live_provider_state_verified'
              : 'live_provider_not_configured'
    throw new Error('[packaged desktop] The packaged live provider was not configured.')
  }
  await expect(page.locator('[data-llm-health-state="configured"]')).toBeVisible()

  checkpoint = 'live_probe_provider'
  failureHttpStatus = null
  const healthResponsePromise = page.waitForResponse(response =>
    response.url().includes('/api/llm/chat/health?probe=true'))
  await page.getByRole('button', { name: 'Verify LLM' }).click()
  const healthResponse = await healthResponsePromise
  requireOk(healthResponse, 'GET', '/api/llm/chat/health?probe=true', http)
  const health = await healthResponse.json() as ProviderHealth
  checkpoint = 'live_provider_identity'
  if (
    health.providerName !== 'OpenAI'
    || health.model !== expectedModel
    || health.isMock
    || !health.isAvailable
    || !health.isProbed
    || health.verificationStatus !== 'verified'
  ) {
    throw new Error('[packaged desktop] The live provider did not verify as exact OpenAI/non-mock/verified.')
  }
  checkpoint = 'live_verified_ui'
  failureHttpStatus = null
  await expect(page.locator('[data-llm-health-state="verified"]')).toBeVisible()

  const beforeProposal = await matchingCardCount(request, auth, boardId, http)
  if (beforeProposal !== 0) {
    throw new Error('[packaged desktop] The synthetic card existed before proposal creation.')
  }

  checkpoint = 'live_create_proposal'
  await page.getByPlaceholder('Session title').fill(`Packaged OpenAI ${journeyId}`)
  await page.getByPlaceholder('Board context (optional)').fill(boardId)
  await page.getByRole('button', { name: 'Create Session' }).click()
  const sessionId = await page.locator('.paper-chat__meta').first().getAttribute('data-session-id')
  if (!sessionId) {
    throw new Error('[packaged desktop] The chat session did not expose its synthetic identifier.')
  }

  await page.getByPlaceholder('Describe an automation instruction...').fill(`create card "${cardTitle}"`)
  await page.getByRole('checkbox', { name: 'Request proposal generation' }).check()
  await page.getByRole('button', { name: 'Send Message' }).click()

  const proposalId = await waitForProposal(request, auth, sessionId, http)
  const proposalBefore = await fetchProposal(request, auth, proposalId, http)

  checkpoint = 'live_approve_proposal'
  await page.goto('/workspace/review')
  const proposalCard = page.locator('.td-review-card').filter({ hasText: proposalBefore.summary }).first()
  await expect(proposalCard).toBeVisible()
  await proposalCard.getByRole('button', { name: 'Approve for board' }).click()
  await expect(proposalCard.getByText('Approved, ready to apply')).toBeVisible()

  const afterApproval = await matchingCardCount(request, auth, boardId, http)
  if (afterApproval !== 0) {
    throw new Error('[packaged desktop] Approval mutated the board before explicit Apply confirmation.')
  }
  const proposalAfterApproval = await fetchProposal(request, auth, proposalId, http)

  checkpoint = 'live_apply_proposal'
  await expectApplyConfirmDialog(
    page,
    () => proposalCard.getByRole('button', { name: 'Apply to board' }).click(),
  )
  await expect(proposalCard).not.toBeVisible()

  const afterApply = await waitForMatchingCardCount(request, auth, boardId, http, 1)
  const proposalAfterApply = await fetchProposal(request, auth, proposalId, http)

  return {
    outcome: 'passed',
    provider: health.providerName,
    model: health.model,
    isMock: health.isMock,
    isProbed: health.isProbed,
    verificationStatus: health.verificationStatus,
    cardTitle,
    proposal: {
      id: proposalId,
      statusBeforeApproval: String(proposalBefore.status),
      statusAfterApproval: String(proposalAfterApproval.status),
      statusAfterApply: String(proposalAfterApply.status),
      operationCount: proposalBefore.operations.length,
    },
    cardCounts: { beforeProposal, afterApproval, afterApply },
  }
}

async function waitForProposal(
  request: APIRequestContext,
  auth: AuthResult,
  sessionId: string,
  http: HttpEvidence[],
): Promise<string> {
  for (let attempt = 0; attempt < 80; attempt++) {
    const path = `/api/llm/chat/sessions/${encodeURIComponent(sessionId)}`
    const response = await request.get(`${apiBaseUrl}/llm/chat/sessions/${encodeURIComponent(sessionId)}`, {
      headers: authHeader(auth),
    })
    requireOk(response, 'GET', path, http, attempt === 0)
    const session = await response.json() as ChatSession
    const proposalId = session.recentMessages.find(message => message.proposalId)?.proposalId
    if (proposalId) return proposalId
    await new Promise(resolve => setTimeout(resolve, 500))
  }

  throw new Error('[packaged desktop] Timed out waiting for the synthetic proposal.')
}

async function fetchProposal(
  request: APIRequestContext,
  auth: AuthResult,
  proposalId: string,
  http: HttpEvidence[],
): Promise<Proposal> {
  const path = `/api/automation/proposals/${encodeURIComponent(proposalId)}`
  const response = await request.get(`${apiBaseUrl}/automation/proposals/${encodeURIComponent(proposalId)}`, {
    headers: authHeader(auth),
  })
  requireOk(response, 'GET', path, http)
  const proposal = await response.json() as Proposal
  if (!Array.isArray(proposal.operations)) {
    throw new Error('[packaged desktop] The synthetic proposal operation list was invalid.')
  }
  return proposal
}

async function waitForMatchingCardCount(
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
  http: HttpEvidence[],
  expected: number,
): Promise<number> {
  for (let attempt = 0; attempt < 40; attempt++) {
    const count = await matchingCardCount(request, auth, boardId, http, attempt === 0)
    if (count === expected) return count
    await new Promise(resolve => setTimeout(resolve, 250))
  }
  throw new Error('[packaged desktop] Timed out waiting for the exact synthetic card count.')
}

async function matchingCardCount(
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
  http: HttpEvidence[],
  record = true,
): Promise<number> {
  const path = `/api/boards/${encodeURIComponent(boardId)}/cards`
  const response = await request.get(`${apiBaseUrl}/boards/${encodeURIComponent(boardId)}/cards`, {
    headers: authHeader(auth),
  })
  requireOk(response, 'GET', path, http, record)
  const cards = await response.json() as Array<{ title: string }>
  if (!Array.isArray(cards)) {
    throw new Error('[packaged desktop] The synthetic board card listing was invalid.')
  }
  const matching = cards.filter(card => card.title === cardTitle).length
  if (cards.length !== matching) {
    throw new Error('[packaged desktop] The synthetic board contained an unexpected card.')
  }
  return matching
}

function requireOk(
  response: APIResponse | import('@playwright/test').Response,
  method: string,
  path: string,
  evidence: HttpEvidence[],
  record = true,
): void {
  failureHttpStatus = response.status()
  if (record) evidence.push({ method, path, status: response.status() })
  if (!response.ok()) {
    throw new Error(`[packaged desktop] ${method} ${path} failed with HTTP ${response.status()}.`)
  }
}

async function readAuth(response: APIResponse): Promise<AuthResult> {
  const auth = await response.json() as AuthResult
  if (!auth.token || !auth.user?.id || !auth.user.username || !auth.user.email) {
    throw new Error('[packaged desktop] Authentication returned an invalid synthetic session.')
  }
  return auth
}

function authHeader(auth: AuthResult): { Authorization: string } {
  return { Authorization: `Bearer ${auth.token}` }
}

function writeEvidence(value: unknown): void {
  mkdirSync(dirname(evidencePath), { recursive: true })
  writeFileSync(evidencePath, `${JSON.stringify(value, null, 2)}\n`, { encoding: 'utf8', flag: 'wx' })
}

function requiredEnv(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`[packaged desktop] ${name} is required.`)
  return value
}

function validatedJourneyId(value: string): string {
  if (!/^[a-z0-9][a-z0-9-]{5,40}$/.test(value)) {
    throw new Error('[packaged desktop] Journey identifier is invalid.')
  }
  return value
}

function validatedLiveSkipReason(value: string): 'not_requested' | 'credential_unavailable' {
  if (value !== 'not_requested' && value !== 'credential_unavailable') {
    throw new Error('[packaged desktop] Live OpenAI skip reason is invalid.')
  }
  return value
}
