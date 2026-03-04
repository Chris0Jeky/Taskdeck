/**
 * Demo / scenario harness helpers for Taskdeck.
 *
 * Goals:
 * - Seed realistic, multi-surface demo data.
 * - Provide reusable blocks for scripted scenarios + autopilot.
 * - Keep execution local-safe by default.
 */

import { randomUUID } from 'node:crypto'
import { setTimeout as sleep } from 'node:timers/promises'
import { assertSafeLocalApiTarget, isoDaysFromNow, normalizeBaseUrl, parseTrueishEnv } from './demo-shared.mjs'

export { isoDaysFromNow }

const DEFAULT_API_BASE = 'http://localhost:5000/api'
const DEFAULT_UI_BASE = 'http://localhost:5173'
const CANONICAL_DEMO_BOARD_NAMES = new Set([
  'DEMO: Capture Loop',
  'DEMO: Capture Loop (Demo)',
  'DEMO: Blank Board',
  'DEMO: Archived Board',
  'DEMO: Content Calendar',
])

function ensureSafeApiTarget(apiBaseUrl) {
  const allowNonLocal = parseTrueishEnv(process.env.TASKDECK_DEMO_ALLOW_NON_LOCAL_API)
  assertSafeLocalApiTarget(apiBaseUrl, {
    allowNonLocal,
    contextLabel: 'run demo harness',
  })
}

function asApiBaseUrl(value) {
  return normalizeBaseUrl(value, DEFAULT_API_BASE)
}

function asUiBaseUrl(value) {
  return normalizeBaseUrl(value, DEFAULT_UI_BASE)
}

export function getDemoConfig(overrides = {}) {
  const {
    apiBaseUrl: overrideApiBaseUrl,
    uiBaseUrl: overrideUiBaseUrl,
    ...remainingOverrides
  } = overrides || {}

  const envApiBaseUrl =
    process.env.TASKDECK_API_BASE_URL || process.env.TASKDECK_API_BASE || process.env.TASKDECK_E2E_API_BASE_URL
  const envUiBaseUrl =
    process.env.TASKDECK_UI_BASE || process.env.TASKDECK_UI_BASE_URL || process.env.TASKDECK_E2E_FRONTEND_BASE_URL

  const apiBaseUrl = asApiBaseUrl(overrideApiBaseUrl ?? envApiBaseUrl)
  const uiBaseUrl = asUiBaseUrl(overrideUiBaseUrl ?? envUiBaseUrl)

  const config = {
    apiBaseUrl,
    uiBaseUrl,
    demoUser: {
      username: process.env.TASKDECK_DEMO_USERNAME || 'demo',
      email: process.env.TASKDECK_DEMO_EMAIL || 'demo@taskdeck.local',
      password: process.env.TASKDECK_DEMO_PASSWORD || 'demo123',
    },
    collabUser: {
      username: process.env.TASKDECK_COLLAB_USERNAME || process.env.TASKDECK_DEMO_COLLAB_USER || 'collab',
      email: process.env.TASKDECK_COLLAB_EMAIL || process.env.TASKDECK_DEMO_COLLAB_EMAIL || 'collab@taskdeck.local',
      password: process.env.TASKDECK_COLLAB_PASSWORD || process.env.TASKDECK_DEMO_COLLAB_PASS || 'demo123',
    },
    ...remainingOverrides,
  }

  config.apiBaseUrl = asApiBaseUrl(config.apiBaseUrl)
  config.uiBaseUrl = asUiBaseUrl(config.uiBaseUrl)
  ensureSafeApiTarget(config.apiBaseUrl)

  return config
}

export class TaskdeckApiClient {
  constructor({ apiBaseUrl, token } = {}) {
    this.apiBaseUrl = asApiBaseUrl(apiBaseUrl)
    this.token = token || null
  }

  withToken(token) {
    return new TaskdeckApiClient({ apiBaseUrl: this.apiBaseUrl, token })
  }

  async request(method, path, { token, headers, body } = {}) {
    const url = `${this.apiBaseUrl}${path.startsWith('/') ? '' : '/'}${path}`
    const effectiveToken = token ?? this.token

    const res = await fetch(url, {
      method,
      headers: {
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(effectiveToken ? { Authorization: `Bearer ${effectiveToken}` } : {}),
        ...(headers || {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    })

    const rawBody = await res.text()
    const { ok: hasParsedBody, value: parsedBody } = safeJsonParse(rawBody)
    if (!res.ok) {
      const bodyForMessage = hasParsedBody ? parsedBody : rawBody
      const messageBody =
        typeof bodyForMessage === 'string' ? bodyForMessage : JSON.stringify(bodyForMessage, null, 2)
      const suffix = messageBody ? `\n${messageBody}` : ''
      const error = new Error(`[${method}] ${url} -> ${res.status} ${res.statusText}${suffix}`)
      error.status = res.status
      error.details = hasParsedBody ? parsedBody : rawBody
      throw error
    }

    if (res.status === 204) return null
    if (!rawBody) return null

    return hasParsedBody ? parsedBody : rawBody
  }

  get(path, opts) {
    return this.request('GET', path, opts)
  }

  post(path, opts) {
    return this.request('POST', path, opts)
  }

  put(path, opts) {
    return this.request('PUT', path, opts)
  }

  patch(path, opts) {
    return this.request('PATCH', path, opts)
  }

  del(path, opts) {
    return this.request('DELETE', path, opts)
  }
}

function safeJsonParse(text) {
  if (!text) return { ok: false, value: undefined }
  try {
    return { ok: true, value: JSON.parse(text) }
  } catch {
    return { ok: false, value: undefined }
  }
}

function getErrorStatus(err) {
  const status = Number(err?.status)
  return Number.isInteger(status) ? status : null
}

export async function waitFor(fn, { label = 'condition', timeoutMs = 30_000, intervalMs = 600 } = {}) {
  const start = Date.now()
  let lastError = null

  while (Date.now() - start < timeoutMs) {
    try {
      const result = await fn()
      if (result) return result
    } catch (err) {
      lastError = err
    }

    await sleep(intervalMs)
  }

  const errMsg = lastError ? ` Last error: ${String(lastError)}` : ''
  throw new Error(`Timeout while waiting for ${label}.${errMsg}`)
}

export async function ensureUser(api, { username, email, password }) {
  const loginBody = { usernameOrEmail: username, password }
  let loginError = null
  try {
    return await api.post('/auth/login', { body: loginBody })
  } catch (err) {
    loginError = err
  }

  const status = getErrorStatus(loginError)
  if (status !== 401 && status !== 404) {
    throw new Error(
      `Failed to login as "${username}" due to non-auth error.\n` +
        `${loginError?.message || loginError}`,
    )
  }

  await api.post('/auth/register', { body: { username, email, password } })
  return await api.post('/auth/login', { body: loginBody })
}

export async function cleanupDemoBoards(
  apiAuthed,
  { prefix = 'DEMO:', dryRun = false, keepCanonical = true, includeArchived = false } = {},
) {
  const boards = await apiAuthed.get(`/boards?includeArchived=${includeArchived ? 'true' : 'false'}`)
  const candidates = (boards || []).filter((board) => {
    if (typeof board?.name !== 'string' || !board.name.startsWith(prefix)) {
      return false
    }
    if (keepCanonical && CANONICAL_DEMO_BOARD_NAMES.has(board.name)) {
      return false
    }
    return true
  })

  if (dryRun) {
    return {
      archived: 0,
      skipped: [],
      candidates: candidates.map((board) => ({ id: board.id, name: board.name })),
    }
  }

  let archived = 0
  const skipped = []
  for (const board of candidates) {
    try {
      await apiAuthed.del(`/boards/${board.id}`)
      archived++
    } catch (err) {
      if (getErrorStatus(err) === 403) {
        skipped.push({ id: board.id, name: board.name, reason: '403 forbidden' })
        continue
      }
      throw err
    }
  }

  return {
    archived,
    skipped,
    candidates: candidates.map((board) => ({ id: board.id, name: board.name })),
  }
}

export async function applyStarterPack(apiAuthed, { boardId, starterPackId, dryRun = false }) {
  const catalog = await apiAuthed.get(`/boards/${boardId}/starter-packs/catalog`)
  const pack = (catalog || []).find((entry) => entry.id === starterPackId)
  if (!pack) {
    throw new Error(`Starter pack not found: ${starterPackId}`)
  }

  await apiAuthed.post(`/boards/${boardId}/starter-packs/apply`, {
    body: {
      manifest: pack.manifest,
      dryRun,
    },
  })

  return pack
}

export async function approveAndExecuteProposal(apiAuthed, proposalId) {
  await apiAuthed.post(`/automation/proposals/${proposalId}/approve`)
  await apiAuthed.post(`/automation/proposals/${proposalId}/execute`, {
    headers: { 'Idempotency-Key': randomUUID() },
  })
}

export async function findProposalBySourceRef(apiAuthed, { sourceReferenceId, limit = 200 }) {
  const proposals = await apiAuthed.get(`/automation/proposals?limit=${limit}`)
  return (proposals || []).find((proposal) => proposal.sourceReferenceId === sourceReferenceId) || null
}

export async function waitForQueueRequest(apiAuthed, requestId, { label = 'queue request', timeoutMs = 45_000 } = {}) {
  return await waitFor(
    async () => {
      const items = await apiAuthed.get('/llm-queue/user?limit=200')
      const req = (items || []).find((entry) => entry.id === requestId)
      if (!req) return null

      const status = req.status
      const statusText = typeof status === 'string' ? status.toLowerCase() : null
      const done =
        status === 2 ||
        status === 3 ||
        status === 4 ||
        statusText === 'completed' ||
        statusText === 'failed' ||
        statusText === 'cancelled' ||
        req.errorMessage
      return done ? req : null
    },
    { label, timeoutMs, intervalMs: 700 },
  )
}

export async function enqueueInstruction(apiAuthed, { boardId, instruction }) {
  return await apiAuthed.post('/llm-queue', {
    body: {
      requestType: 'instruction',
      payload: instruction,
      boardId,
    },
  })
}

export async function enqueueAndApplyInstruction(apiAuthed, { boardId, instruction, timeoutMs = 60_000 }) {
  const request = await enqueueInstruction(apiAuthed, { boardId, instruction })
  const queueRequest = await waitForQueueRequest(apiAuthed, request.id, { label: `queue request ${request.id}`, timeoutMs })

  const queueStatusRaw = queueRequest?.status
  const queueStatus = typeof queueStatusRaw === 'string' ? queueStatusRaw.toLowerCase() : queueStatusRaw
  const failed = queueStatus === 3 || queueStatus === 'failed'
  const cancelled = queueStatus === 4 || queueStatus === 'cancelled'

  if (failed || cancelled || queueRequest?.errorMessage) {
    const reason =
      queueRequest?.errorMessage || `queue request ended as ${String(queueRequest?.status ?? 'unknown')}`
    throw new Error(`Queue request ${request.id} did not produce a proposal: ${reason}`)
  }

  const proposal = await waitFor(
    async () => await findProposalBySourceRef(apiAuthed, { sourceReferenceId: request.id }),
    { label: `proposal for queue request ${request.id}`, timeoutMs, intervalMs: 800 },
  )

  await approveAndExecuteProposal(apiAuthed, proposal.id)
  return { request, proposal }
}

export async function createCaptureItem(
  apiAuthed,
  { boardId = null, text, source = 'Typed', titleHint = null, externalRef = null } = {},
) {
  if (!text || typeof text !== 'string') {
    throw new Error('createCaptureItem requires { text: string }')
  }

  return await apiAuthed.post('/capture/items', {
    body: { boardId, text, source, titleHint, externalRef },
  })
}

export async function getCaptureItem(apiAuthed, captureItemId) {
  return await apiAuthed.get(`/capture/items/${encodeURIComponent(captureItemId)}`)
}

export async function ignoreCaptureItem(apiAuthed, captureItemId) {
  await apiAuthed.post(`/capture/items/${encodeURIComponent(captureItemId)}/ignore`)
}

export async function cancelCaptureItem(apiAuthed, captureItemId) {
  await apiAuthed.post(`/capture/items/${encodeURIComponent(captureItemId)}/cancel`)
}

export async function triageCaptureItem(apiAuthed, captureItemId) {
  return await apiAuthed.post(`/capture/items/${encodeURIComponent(captureItemId)}/triage`)
}

export async function waitForCaptureOutcome(
  apiAuthed,
  captureItemId,
  { timeoutMs = 90_000, intervalMs = 900 } = {},
) {
  const encodedId = encodeURIComponent(captureItemId)

  return await waitFor(
    async () => {
      const item = await apiAuthed.get(`/capture/items/${encodedId}`)
      if (!item) return null

      const proposalId = item?.provenance?.proposalId
      if (proposalId) return { outcome: 'proposal', item }

      const status = item?.status
      const statusText = typeof status === 'string' ? status.toLowerCase() : null
      const triaged = status === 2 || statusText === 'triaged'
      const ignored = status === 5 || statusText === 'ignored'
      const failed = status === 6 || statusText === 'failed'
      if (triaged) return { outcome: 'triaged', item }
      if (ignored) return { outcome: 'ignored', item }
      if (failed) return { outcome: 'failed', item }

      return null
    },
    { label: `capture(${captureItemId}) outcome`, timeoutMs, intervalMs },
  )
}

export async function waitForCaptureProposalId(
  apiAuthed,
  captureItemId,
  { timeoutMs = 90_000, intervalMs = 900 } = {},
) {
  const result = await waitForCaptureOutcome(apiAuthed, captureItemId, { timeoutMs, intervalMs })
  if (result.outcome !== 'proposal') {
    const status = result?.item?.status
    throw new Error(`Capture did not produce a proposal (outcome=${result.outcome}, status=${String(status)})`)
  }

  return result.item.provenance.proposalId
}

export function summarizeBoardForAgent({ board, columns, cards, maxCards = 40 } = {}) {
  const name = board?.name || '(unknown board)'
  const colNameById = new Map((columns || []).map((column) => [column.id, column.name]))

  const grouped = new Map()
  for (const card of cards || []) {
    const colName = colNameById.get(card.columnId) || 'Unknown'
    if (!grouped.has(colName)) grouped.set(colName, [])
    grouped.get(colName).push(card)
  }

  const lines = []
  lines.push(`Board: ${name}`)
  lines.push(`Columns: ${(columns || []).map((column) => column.name).join(', ')}`)
  lines.push('')

  let emitted = 0
  for (const [colName, colCards] of grouped.entries()) {
    if (emitted >= maxCards) break
    lines.push(`${colName}:`)
    for (const card of colCards) {
      if (emitted >= maxCards) break
      const due = card.dueDate ? ` (due ${card.dueDate})` : ''
      const blocked = card.isBlocked ? ' [BLOCKED]' : ''
      lines.push(`- ${card.id}: ${card.title}${due}${blocked}`)
      emitted++
    }
    lines.push('')
  }

  return lines.join('\n').trim()
}
