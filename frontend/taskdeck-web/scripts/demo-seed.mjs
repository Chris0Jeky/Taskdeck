/**
 * Taskdeck Demo Seeder
 *
 * Goal: populate a local dev instance with enough data to make the UI feel "alive"
 * (Boards, Inbox, Proposals, Queue, Notifications, Activity, Ops).
 *
 * Usage:
 *   cd frontend/taskdeck-web
 *   npm run demo:seed
 *
 * Optional env vars:
 *   TASKDECK_API_BASE_URL       (default: http://localhost:5000/api)
 *   TASKDECK_API_BASE           (legacy; same default)
 *   TASKDECK_DEMO_USERNAME      (default: demo)
 *   TASKDECK_DEMO_EMAIL         (default: demo@taskdeck.local)
 *   TASKDECK_DEMO_PASSWORD      (default: demo123)
 *   TASKDECK_COLLAB_USERNAME    (default: collab)
 *   TASKDECK_COLLAB_EMAIL       (default: collab@taskdeck.local)
 *   TASKDECK_COLLAB_PASSWORD    (default: demo123)
 *   TASKDECK_DEMO_COLLAB_USER   (legacy)
 *   TASKDECK_DEMO_COLLAB_EMAIL  (legacy)
 *   TASKDECK_DEMO_COLLAB_PASS   (legacy)
 *   TASKDECK_DEMO_ALLOW_NON_LOCAL_API (default: false; set true to allow non-local API targets)
 *   TASKDECK_UI_BASE            (default: http://localhost:5173)
 */

import { randomUUID } from 'node:crypto'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import {
  assertSafeLocalApiTarget,
  getHostname,
  isLocalHostname,
  normalizeBaseUrl,
  parseTrueishEnv,
} from './demo-shared.mjs'

const API_BASE = (
  process.env.TASKDECK_API_BASE_URL ||
  process.env.TASKDECK_API_BASE ||
  'http://localhost:5000/api'
)
const NORMALIZED_API_BASE = normalizeBaseUrl(API_BASE, 'http://localhost:5000/api')

const ALLOW_NON_LOCAL_API = parseTrueishEnv(process.env.TASKDECK_DEMO_ALLOW_NON_LOCAL_API)

const DEMO = {
  username: process.env.TASKDECK_DEMO_USERNAME || 'demo',
  email: process.env.TASKDECK_DEMO_EMAIL || 'demo@taskdeck.local',
  password: process.env.TASKDECK_DEMO_PASSWORD || 'demo123',
}

const COLLAB = {
  username: process.env.TASKDECK_COLLAB_USERNAME || process.env.TASKDECK_DEMO_COLLAB_USER || 'collab',
  email: process.env.TASKDECK_COLLAB_EMAIL || process.env.TASKDECK_DEMO_COLLAB_EMAIL || 'collab@taskdeck.local',
  password: process.env.TASKDECK_COLLAB_PASSWORD || process.env.TASKDECK_DEMO_COLLAB_PASS || 'demo123',
}

const DEMO_BOARD_SPECS = {
  capture: {
    canonicalName: 'DEMO: Capture Loop',
    reusableNames: ['DEMO: Capture Loop', 'DEMO: Capture Loop (Demo)'],
    description: 'Seeded demo board (capture -> triage -> proposal -> apply).',
    isArchived: false,
  },
  content: {
    canonicalName: 'DEMO: Content Calendar',
    reusableNames: ['DEMO: Content Calendar'],
    description: 'Seeded via Starter Pack blueprint (content calendar).',
    isArchived: false,
  },
  blank: {
    canonicalName: 'DEMO: Blank Board',
    reusableNames: ['DEMO: Blank Board'],
    description: 'Intentionally empty board to demo Starter Packs UI.',
    isArchived: false,
  },
  archived: {
    canonicalName: 'DEMO: Archived Board',
    reusableNames: ['DEMO: Archived Board'],
    description: 'Used to demo Archive view (archived boards list).',
    isArchived: true,
  },
}

const SEEDED_CAPTURE_TEXT = {
  ignored: 'This item is ignored (demo).',
  triageApplied:
    '- [ ] Draft a 5-minute stakeholder demo script\n' +
    '- [ ] Add onboarding empty-states for scaffolding pages\n' +
    '- [ ] Create demo seeding harness that populates Inbox/Proposals/Notifications\n',
  triagePending:
    '- [ ] Follow up: connect Activity view to real audit queries\n' +
    '- [ ] Follow up: simplify Automation Queue composer\n',
}

const SEEDED_QUEUE = {
  successCardTitle: 'From queue: demo seeded item',
  successInstruction: 'create card "From queue: demo seeded item"',
  failureInstruction: 'create board named joji',
}

const SEEDED_CHAT = {
  sessionTitle: 'Stakeholder Demo',
}

const SEEDED_COMMENT = {
  demoMention: (collabUsername) =>
    `Heads up @${collabUsername} - this is a seeded mention for the Notifications view.`,
  collabReply: (demoUsername) => `@${demoUsername} ack - I will take a look after lunch. (seeded)`,
}

const SEEDED_OPS_LOG_MESSAGES = {
  healthCheck: "Starting template 'health.check'",
  boardsList: "Starting template 'boards.list'",
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

const BOARD_ROLE_NAMES = {
  0: 'Owner',
  1: 'Admin',
  2: 'Editor',
  3: 'Viewer',
}

function ensureSafeApiBaseTarget() {
  assertSafeLocalApiTarget(NORMALIZED_API_BASE, {
    allowNonLocal: ALLOW_NON_LOCAL_API,
    contextLabel: 'seed demo data',
  })

  if (ALLOW_NON_LOCAL_API && !isLocalHostname(getHostname(NORMALIZED_API_BASE))) {
    console.warn(`WARN: non-local API target override enabled for ${NORMALIZED_API_BASE}`)
    return
  }
}

function toRoleRank(role) {
  if (typeof role === 'number' && Number.isInteger(role)) return role
  if (typeof role === 'string') {
    const numericRole = Number(role)
    if (Number.isInteger(numericRole)) return numericRole
    const normalized = role.trim().toLowerCase()
    if (normalized === 'owner') return 0
    if (normalized === 'admin') return 1
    if (normalized === 'editor') return 2
    if (normalized === 'viewer') return 3
  }
  return Number.POSITIVE_INFINITY
}

function formatBoardRole(role) {
  const rank = toRoleRank(role)
  if (Object.prototype.hasOwnProperty.call(BOARD_ROLE_NAMES, rank)) {
    return BOARD_ROLE_NAMES[rank]
  }
  if (role === undefined || role === null || role === '') return 'unknown'
  return `unknown(${String(role)})`
}

function hasAtLeastEditorAccess(role) {
  return toRoleRank(role) <= 2
}

function idsMatch(left, right) {
  if (left === undefined || left === null || right === undefined || right === null) return false
  return String(left).trim().toLowerCase() === String(right).trim().toLowerCase()
}

function hasStatus(status, expectedStatusName, expectedNumericStatus) {
  if (typeof status === 'number') {
    return status === expectedNumericStatus
  }

  return typeof status === 'string' && status.trim().toLowerCase() === expectedStatusName.toLowerCase()
}

function getTextFragment(value) {
  return String(value || '')
    .trim()
    .split('\n')[0]
}

function findCaptureSummaryByTextFragment(captureSummaries, boardId, text) {
  const fragment = getTextFragment(text)
  return (captureSummaries || []).find(
    (item) => idsMatch(item?.boardId, boardId) && String(item?.textExcerpt || '').includes(fragment),
  )
}

function findBoardCardByTitle(cards, title) {
  return (cards || []).find((card) => String(card?.title || '').trim() === title) || null
}

function findChatSessionByTitle(chatSessions, boardId, title) {
  return (
    (chatSessions || []).find(
      (session) => idsMatch(session?.boardId, boardId) && String(session?.title || '').trim() === title,
    ) || null
  )
}

function hasCommentWithContent(comments, content) {
  return (comments || []).some((comment) => String(comment?.content || '').trim() === content)
}

function hasOpsLogMessage(logEntries, messageFragment) {
  return (logEntries || []).some((entry) => String(entry?.message || '').includes(messageFragment))
}

export function hasSeededChatEvidence(chatMessages, renameInstruction) {
  return (
    Boolean(
      (chatMessages || []).find((message) => String(message?.content || '').trim() === String(renameInstruction || '').trim()),
    ) || Boolean((chatMessages || []).find((message) => message?.proposalId))
  )
}

export function shouldRecreateCaptureSeed(detail, { ignore = false } = {}) {
  if (ignore || detail?.provenance?.proposalId) {
    return false
  }

  return (
    hasStatus(detail?.status, 'Ignored', 5) ||
    hasStatus(detail?.status, 'Converted', 4) ||
    hasStatus(detail?.status, 'Failed', 6)
  )
}

export function planDemoSeedRerunState({
  boardId,
  captureSummaries,
  boardCards,
  queueRequests,
  chatSessions,
  existingComments,
  logEntries,
  demoUsername,
  collabUsername,
}) {
  const failedQueueRequest = (queueRequests || []).find(
    (request) =>
      idsMatch(request?.boardId, boardId) &&
      (hasStatus(request?.status, 'Failed', 3) || Boolean(request?.errorMessage)),
  )
  const seededChatSession = findChatSessionByTitle(chatSessions, boardId, SEEDED_CHAT.sessionTitle)
  const demoMentionContent = SEEDED_COMMENT.demoMention(collabUsername)
  const collabReplyContent = SEEDED_COMMENT.collabReply(demoUsername)

  return {
    captures: {
      ignored: findCaptureSummaryByTextFragment(captureSummaries, boardId, SEEDED_CAPTURE_TEXT.ignored),
      triageApplied: findCaptureSummaryByTextFragment(captureSummaries, boardId, SEEDED_CAPTURE_TEXT.triageApplied),
      triagePending: findCaptureSummaryByTextFragment(captureSummaries, boardId, SEEDED_CAPTURE_TEXT.triagePending),
    },
    queue: {
      seededCard: findBoardCardByTitle(boardCards, SEEDED_QUEUE.successCardTitle),
      hasFailedRequest: Boolean(failedQueueRequest),
    },
    chat: {
      seededSession: seededChatSession,
      hasSeededMessage: hasSeededChatEvidence(
        seededChatSession?.recentMessages,
        `rename board to "${DEMO_BOARD_SPECS.capture.canonicalName} (Chat)"`,
      ),
    },
    comments: {
      hasDemoMention: hasCommentWithContent(existingComments, demoMentionContent),
      hasCollabReply: hasCommentWithContent(existingComments, collabReplyContent),
      demoMentionContent,
      collabReplyContent,
    },
    ops: {
      hasHealthCheckLog: hasOpsLogMessage(logEntries, SEEDED_OPS_LOG_MESSAGES.healthCheck),
      hasBoardsListLog: hasOpsLogMessage(logEntries, SEEDED_OPS_LOG_MESSAGES.boardsList),
    },
  }
}

async function http(method, path, { token, body, headers: extraHeaders } = {}) {
  const url = `${NORMALIZED_API_BASE}${path.startsWith('/') ? '' : '/'}${path}`

  const headers = {
    'Content-Type': 'application/json',
  }
  if (token) headers.Authorization = `Bearer ${token}`
  if (extraHeaders) {
    for (const [k, v] of Object.entries(extraHeaders)) {
      if (v !== undefined && v !== null) headers[k] = String(v)
    }
  }

  const res = await fetch(url, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  const text = await res.text()
  const maybeJson = text ? safeJsonParse(text) : null
  if (!res.ok) {
    const details = maybeJson || text
    const msg = typeof details === 'string' ? details : JSON.stringify(details, null, 2)
    const error = new Error(`${method} ${path} failed (${res.status})\n${msg}`)
    error.status = res.status
    error.details = details
    throw error
  }
  return maybeJson
}

function safeJsonParse(text) {
  try {
    return JSON.parse(text)
  } catch {
    return null
  }
}

function getHttpStatus(error) {
  if (!error || typeof error !== 'object') return null
  const status = Number(error.status)
  return Number.isInteger(status) ? status : null
}

function isDemoBoard(board) {
  return typeof board?.name === 'string' && board.name.startsWith('DEMO:')
}

function pickReusableBoard(boards, reusableNames) {
  const names = new Set(reusableNames)
  const candidates = (boards || []).filter((b) => names.has(b.name))
  if (!candidates.length) return null

  return candidates.find((b) => !b.isArchived) || candidates[0]
}

async function ensureDemoBoard(spec, boards, token) {
  const existing = pickReusableBoard(boards, spec.reusableNames)
  if (!existing) {
    const created = await http('POST', '/boards', {
      token,
      body: {
        name: spec.canonicalName,
        description: spec.description,
      },
    })
    if (spec.isArchived) {
      await http('PUT', `/boards/${created.id}`, {
        token,
        body: { isArchived: true },
      })
      return { ...created, isArchived: true }
    }
    return created
  }

  const updateBody = {}
  if (existing.name !== spec.canonicalName) {
    updateBody.name = spec.canonicalName
  }
  if (existing.description !== spec.description) {
    updateBody.description = spec.description
  }
  if (Boolean(existing.isArchived) !== spec.isArchived) {
    updateBody.isArchived = spec.isArchived
  }

  if (!Object.keys(updateBody).length) {
    return existing
  }

  const updated = await http('PUT', `/boards/${existing.id}`, {
    token,
    body: updateBody,
  })
  return updated || { ...existing, ...updateBody }
}

async function ensureUser({ username, email, password }) {
  const loginBody = { usernameOrEmail: username, password }
  let loginError = null

  try {
    return await http('POST', '/auth/login', { body: loginBody })
  } catch (err) {
    loginError = err
  }

  const loginStatus = getHttpStatus(loginError)
  if (loginStatus !== 401 && loginStatus !== 404) {
    throw new Error(
      `Failed to login as "${username}" due to a non-auth error.\n` +
        `Only 401/404 responses trigger auto-registration.\n\n` +
        `${loginError?.message || loginError}`
    )
  }

  try {
    await http('POST', '/auth/register', {
      body: { username, email, password },
    })
  } catch (err) {
    throw new Error(
      `Failed to login as "${username}" and could not register it. ` +
        `The user may already exist with a different password.\n` +
        `Set TASKDECK_DEMO_PASSWORD / TASKDECK_COLLAB_PASSWORD (or legacy TASKDECK_DEMO_COLLAB_PASS), or delete the user from the dev DB.\n\n` +
        `${err?.message || err}`
    )
  }

  try {
    return await http('POST', '/auth/login', { body: loginBody })
  } catch (err) {
    const registerStatus = getHttpStatus(err)
    throw new Error(
      `Registered "${username}" but follow-up login failed${registerStatus ? ` (${registerStatus})` : ''}.\n\n` +
        `${err?.message || err}`
    )
  }
}

async function waitFor(fn, { timeoutMs = 45000, intervalMs = 750, label = 'condition' } = {}) {
  const start = Date.now()
  // eslint-disable-next-line no-constant-condition
  while (true) {
    const val = await fn()
    if (val) return val
    if (Date.now() - start > timeoutMs) {
      throw new Error(`Timed out waiting for ${label} after ${timeoutMs}ms`)
    }
    await sleep(intervalMs)
  }
}

async function getStarterPackManifest(boardId, token, packId) {
  const catalog = await http('GET', `/boards/${boardId}/starter-packs/catalog`, { token })
  const pack = (catalog || []).find((p) => p.id === packId)
  if (!pack) {
    throw new Error(`Starter pack not found in catalog: ${packId}`)
  }
  return pack.manifest
}

async function applyStarterPack(boardId, token, packId) {
  const manifest = await getStarterPackManifest(boardId, token, packId)
  return await http('POST', `/boards/${boardId}/starter-packs/apply`, {
    token,
    body: { manifest, dryRun: false },
  })
}

async function ensureCollaboratorEditorAccess(boardId, token, collaboratorUserId) {
  const editorRole = 2
  let accesses = await http('GET', `/boards/${boardId}/access`, { token })
  let existingAccess = (accesses || []).find((entry) => idsMatch(entry?.userId, collaboratorUserId))

  if (!existingAccess) {
    try {
      await http('POST', `/boards/${boardId}/access`, {
        token,
        body: {
          userId: collaboratorUserId,
          role: editorRole,
        },
      })
      console.log('- access: granted collab user Editor on capture board')
      return
    } catch (err) {
      if (getHttpStatus(err) !== 409) {
        throw err
      }
      accesses = await http('GET', `/boards/${boardId}/access`, { token })
      existingAccess = (accesses || []).find((entry) => idsMatch(entry?.userId, collaboratorUserId))
      if (!existingAccess) {
        throw new Error('Board access conflict reported but collaborator access entry was not found.')
      }
    }
  }

  if (hasAtLeastEditorAccess(existingAccess.role)) {
    console.log(`- access: collab user already has ${formatBoardRole(existingAccess.role)} on capture board`)
    return
  }

  if (!existingAccess.id) {
    throw new Error('Collaborator board access entry is missing an id; cannot upgrade role to Editor.')
  }

  await http('PUT', `/boards/${boardId}/access/${existingAccess.id}`, {
    token,
    body: {
      role: editorRole,
    },
  })
  console.log(`- access: upgraded collab user role from ${formatBoardRole(existingAccess.role)} to Editor on capture board`)
}

async function listCaptureSummaries(boardId, token) {
  return (await http('GET', `/capture/items?boardId=${encodeURIComponent(boardId)}&limit=200`, { token })) || []
}

async function getCaptureDetail(summary, token) {
  if (!summary?.id) {
    return null
  }

  return await http('GET', `/capture/items/${summary.id}`, { token })
}

async function getProposalById(proposalId, token) {
  const proposals = (await http('GET', '/automation/proposals?includeOperations=true&limit=200', { token })) || []
  return proposals.find((proposal) => idsMatch(proposal?.id, proposalId)) || null
}

async function getChatSessionDetail(sessionId, token) {
  if (!sessionId) {
    return null
  }

  return await http('GET', `/llm/chat/sessions/${sessionId}`, { token })
}

async function ensureProposalApplied(proposalId, token) {
  const proposal = await getProposalById(proposalId, token)
  if (!proposal) {
    throw new Error(`Expected proposal ${proposalId} to exist for seeded demo state.`)
  }

  if (hasStatus(proposal.status, 'Applied', 3)) {
    return proposal
  }

  if (hasStatus(proposal.status, 'PendingReview', 0)) {
    await http('POST', `/automation/proposals/${proposalId}/approve`, { token })
    await http('POST', `/automation/proposals/${proposalId}/execute`, {
      token,
      headers: { 'Idempotency-Key': randomUUID() },
    })
    return await getProposalById(proposalId, token)
  }

  if (hasStatus(proposal.status, 'Approved', 1)) {
    await http('POST', `/automation/proposals/${proposalId}/execute`, {
      token,
      headers: { 'Idempotency-Key': randomUUID() },
    })
    return await getProposalById(proposalId, token)
  }

  throw new Error(`Seeded demo proposal ${proposalId} is in unexpected status ${String(proposal.status)}.`)
}

async function ensureCaptureSeed(boardId, token, text, { ignore = false, applyProposal = false } = {}) {
  const captureSummaries = await listCaptureSummaries(boardId, token)
  let summary = findCaptureSummaryByTextFragment(captureSummaries, boardId, text)
  if (!summary) {
    summary = await http('POST', '/capture/items', {
      token,
      body: {
        boardId,
        text,
        source: 'Typed',
      },
    })
  }

  let detail = await getCaptureDetail(summary, token)
  if (shouldRecreateCaptureSeed(detail, { ignore })) {
    summary = await http('POST', '/capture/items', {
      token,
      body: {
        boardId,
        text,
        source: 'Typed',
      },
    })
    detail = await getCaptureDetail(summary, token)
  }

  if (ignore) {
    if (!hasStatus(detail?.status, 'Ignored', 5)) {
      await http('POST', `/capture/items/${summary.id}/ignore`, { token })
      detail = await getCaptureDetail(summary, token)
    }
    return detail
  }

  if (!detail?.provenance?.proposalId) {
    const isTerminal = shouldRecreateCaptureSeed(detail)
    if (!isTerminal) {
      await http('POST', `/capture/items/${summary.id}/triage`, { token })
    } else {
      throw new Error(`Seeded capture ${summary.id} is terminal without a proposal and must be recreated.`)
    }

    detail = await waitFor(
      async () => {
        const refreshed = await getCaptureDetail(summary, token)
        if (shouldRecreateCaptureSeed(refreshed)) {
          throw new Error(`Seeded capture ${summary.id} reached a terminal state before producing a proposal.`)
        }
        return refreshed?.provenance?.proposalId ? refreshed : null
      },
      { label: `capture seed ${summary.id} to produce a proposalId` },
    )
  }

  if (applyProposal) {
    await ensureProposalApplied(detail.provenance.proposalId, token)
  }

  return detail
}

async function ensureQueueSeed(boardId, token, existingBoardCards) {
  let createdGoodRequest = null
  const seededCard = findBoardCardByTitle(existingBoardCards, SEEDED_QUEUE.successCardTitle)
  if (!seededCard) {
    createdGoodRequest = await http('POST', '/llm-queue', {
      token,
      body: {
        requestType: 'instruction',
        payload: SEEDED_QUEUE.successInstruction,
        boardId,
      },
    })

    await waitFor(
      async () => {
        const items = (await http('GET', '/llm-queue/user?limit=200', { token })) || []
        const request = items.find((item) => idsMatch(item?.id, createdGoodRequest?.id))
        const done =
          hasStatus(request?.status, 'Completed', 2) ||
          hasStatus(request?.status, 'Failed', 3) ||
          hasStatus(request?.status, 'Cancelled', 4) ||
          Boolean(request?.errorMessage)
        return done ? request : null
      },
      { label: 'seeded queue success request to finish processing' },
    )

    const proposals = (await http('GET', '/automation/proposals?includeOperations=true&limit=200', { token })) || []
    const queueProposal = proposals.find((proposal) => idsMatch(proposal?.sourceReferenceId, createdGoodRequest?.id))
    if (queueProposal) {
      await ensureProposalApplied(queueProposal.id, token)
    }
  }

  const queueRequests = (await http('GET', '/llm-queue/user?limit=200', { token })) || []
  const failedRequestExists = queueRequests.some(
    (request) =>
      idsMatch(request?.boardId, boardId) &&
      (hasStatus(request?.status, 'Failed', 3) || Boolean(request?.errorMessage)),
  )

  if (!failedRequestExists) {
    const badRequest = await http('POST', '/llm-queue', {
      token,
      body: {
        requestType: 'instruction',
        payload: SEEDED_QUEUE.failureInstruction,
        boardId,
      },
    })

    await waitFor(
      async () => {
        const items = (await http('GET', '/llm-queue/user?limit=200', { token })) || []
        const request = items.find((item) => idsMatch(item?.id, badRequest?.id))
        const done = hasStatus(request?.status, 'Failed', 3) || Boolean(request?.errorMessage)
        return done ? request : null
      },
      { label: 'seeded queue failure request to finish processing' },
    )
  }
}

async function ensureChatSeed(boardId, token) {
  const temporaryCaptureName = `${DEMO_BOARD_SPECS.capture.canonicalName} (Chat)`
  const renameInstruction = `rename board to "${temporaryCaptureName}"`
  const sessions = (await http('GET', '/llm/chat/sessions', { token })) || []
  let session = findChatSessionByTitle(sessions, boardId, SEEDED_CHAT.sessionTitle)
  let sessionDetail = session?.id ? await getChatSessionDetail(session.id, token) : null
  let hasSeededMessage = hasSeededChatEvidence(sessionDetail?.recentMessages, renameInstruction)

  if (!session) {
    session = await http('POST', '/llm/chat/sessions', {
      token,
      body: { title: SEEDED_CHAT.sessionTitle, boardId },
    })
    sessionDetail = session
    hasSeededMessage = false
  }

  let appliedRenameProposal = false
  if (!hasSeededMessage) {
    const chatMessage = await http('POST', `/llm/chat/sessions/${session.id}/messages`, {
      token,
      body: { content: renameInstruction, requestProposal: true },
    })

    if (chatMessage?.proposalId) {
      await ensureProposalApplied(chatMessage.proposalId, token)
      appliedRenameProposal = true
    }
  }

  if (appliedRenameProposal) {
    await http('PUT', `/boards/${boardId}`, {
      token,
      body: { name: DEMO_BOARD_SPECS.capture.canonicalName },
    })
  }
}

async function ensureSeededComments(boardId, token, collabToken, demoUser, collabUser) {
  const cards = (await http('GET', `/boards/${boardId}/cards`, { token })) || []
  const firstCard = cards[0]
  if (!firstCard) {
    return null
  }

  const comments = (await http('GET', `/boards/${boardId}/cards/${firstCard.id}/comments`, { token })) || []
  const demoMentionContent = SEEDED_COMMENT.demoMention(collabUser.username)
  const collabReplyContent = SEEDED_COMMENT.collabReply(demoUser.username)

  if (!hasCommentWithContent(comments, demoMentionContent)) {
    await http('POST', `/boards/${boardId}/cards/${firstCard.id}/comments`, {
      token,
      body: { content: demoMentionContent },
    })
  }

  if (!hasCommentWithContent(comments, collabReplyContent)) {
    await http('POST', `/boards/${boardId}/cards/${firstCard.id}/comments`, {
      token: collabToken,
      body: { content: collabReplyContent },
    })
  }

  return firstCard
}

async function ensureOpsSeed(token) {
  const logEntries = (await http('GET', '/logs?source=OpsCliService&limit=200', { token })) || []

  if (!hasOpsLogMessage(logEntries, SEEDED_OPS_LOG_MESSAGES.healthCheck)) {
    await http('POST', '/ops/cli/run', {
      token,
      body: { templateName: 'health.check', parameters: {} },
    })
  }

  if (!hasOpsLogMessage(logEntries, SEEDED_OPS_LOG_MESSAGES.boardsList)) {
    try {
      await http('POST', '/ops/cli/run', {
        token,
        body: { templateName: 'boards.list', parameters: {} },
      })
    } catch (err) {
      if (getHttpStatus(err) !== 403) {
        throw err
      }
    }
  }
}

export async function main() {
  ensureSafeApiBaseTarget()

  console.log(`\nTaskdeck demo seeder -> ${NORMALIZED_API_BASE}`)
  console.log('----------------------------------------')

  // 1) Ensure demo users exist
  const demoLogin = await ensureUser(DEMO)
  const demoToken = demoLogin.token
  const demoUser = demoLogin.user

  const collabLogin = await ensureUser(COLLAB)
  const collabToken = collabLogin.token
  const collabUser = collabLogin.user

  console.log(`Demo user:   ${demoUser.username} (${demoUser.email})`)
  console.log(`Collab user: ${collabUser.username} (${collabUser.email})`)

  // 2) Reuse/create canonical demo boards and archive extra active DEMO boards.
  const boards = await http('GET', '/boards?includeArchived=true', { token: demoToken })
  const demoBoards = (boards || []).filter(isDemoBoard)

  const captureBoard = await ensureDemoBoard(DEMO_BOARD_SPECS.capture, demoBoards, demoToken)
  const contentBoard = await ensureDemoBoard(DEMO_BOARD_SPECS.content, demoBoards, demoToken)
  const blankBoard = await ensureDemoBoard(DEMO_BOARD_SPECS.blank, demoBoards, demoToken)
  const archivedBoard = await ensureDemoBoard(DEMO_BOARD_SPECS.archived, demoBoards, demoToken)

  const canonicalBoardIds = new Set([captureBoard.id, contentBoard.id, blankBoard.id, archivedBoard.id])
  const extraActiveDemoBoards = demoBoards.filter((b) => !b.isArchived && !canonicalBoardIds.has(b.id))
  if (extraActiveDemoBoards.length) {
    console.log(`\nArchiving ${extraActiveDemoBoards.length} extra active DEMO:* board(s)...`)
    for (const b of extraActiveDemoBoards) {
      try {
        await http('DELETE', `/boards/${b.id}`, { token: demoToken })
        console.log(`- archived ${b.name}`)
      } catch (err) {
        if (getHttpStatus(err) === 403) {
          console.log(`- skipped ${b.name} (readable but not deletable: 403 Forbidden)`)
          continue
        }
        throw err
      }
    }
  }

  console.log('\nUsing canonical demo boards...')
  console.log(`- capture board: ${captureBoard.name}`)
  console.log(`- content board: ${contentBoard.name}`)
  console.log(`- blank board: ${blankBoard.name}`)
  console.log(`- archived board: ${archivedBoard.name}`)

  // 3) Seed: starter packs
  console.log('\nApplying starter packs...')
  await applyStarterPack(captureBoard.id, demoToken, 'common-column-flow-kanban')
  await applyStarterPack(captureBoard.id, demoToken, 'common-labels-core')
  console.log('- capture board: kanban columns + common labels')

  await applyStarterPack(contentBoard.id, demoToken, 'board-blueprint-content-calendar')
  console.log('- content board: content calendar blueprint')

  // 4) Seed: board access entry (so Access view is not empty)
  await ensureCollaboratorEditorAccess(captureBoard.id, demoToken, collabUser.id)

  const captureSummaries = await listCaptureSummaries(captureBoard.id, demoToken)
  const captureBoardCards = (await http('GET', `/boards/${captureBoard.id}/cards`, { token: demoToken })) || []
  const queueRequests = (await http('GET', '/llm-queue/user?limit=200', { token: demoToken })) || []
  const chatSessions = (await http('GET', '/llm/chat/sessions', { token: demoToken })) || []
  const seededChatSession = findChatSessionByTitle(chatSessions, captureBoard.id, SEEDED_CHAT.sessionTitle)
  const plannedChatSessions = seededChatSession?.id
    ? await Promise.all(
        chatSessions.map(async (session) =>
          idsMatch(session?.id, seededChatSession.id) ? (await getChatSessionDetail(seededChatSession.id, demoToken)) || session : session,
        ),
      )
    : chatSessions
  const firstExistingCard = captureBoardCards[0]
  const existingComments = firstExistingCard
    ? ((await http('GET', `/boards/${captureBoard.id}/cards/${firstExistingCard.id}/comments`, { token: demoToken })) || [])
    : []
  const logEntries = (await http('GET', '/logs?source=OpsCliService&limit=200', { token: demoToken })) || []
  const seedPlan = planDemoSeedRerunState({
    boardId: captureBoard.id,
    captureSummaries,
    boardCards: captureBoardCards,
    queueRequests,
    chatSessions: plannedChatSessions,
    existingComments,
    logEntries,
    demoUsername: demoUser.username,
    collabUsername: collabUser.username,
  })

  // 5) Seed: Inbox items (ignored + triage)
  console.log('\nCreating Inbox items...')
  await ensureCaptureSeed(captureBoard.id, demoToken, SEEDED_CAPTURE_TEXT.ignored, { ignore: true })
  const triageAppliedWithProposal = await ensureCaptureSeed(captureBoard.id, demoToken, SEEDED_CAPTURE_TEXT.triageApplied, {
    applyProposal: true,
  })
  const triagePendingWithProposal = await ensureCaptureSeed(captureBoard.id, demoToken, SEEDED_CAPTURE_TEXT.triagePending)

  console.log(
    `- capture items: ignored=${seedPlan.captures.ignored ? 'reused' : 'created'}, ` +
      `triage-applied=${seedPlan.captures.triageApplied ? 'reused' : 'created'}, ` +
      `triage-pending=${seedPlan.captures.triagePending ? 'reused' : 'created'}`,
  )
  console.log(`- triage (applied) proposal: ${triageAppliedWithProposal.provenance.proposalId}`)
  console.log(`- triage (pending) proposal: ${triagePendingWithProposal.provenance.proposalId} (left for review)`)

  // 6) Seed: Queue requests (1 success + 1 failure)
  console.log('\nCreating Automation Queue items...')
  await ensureQueueSeed(captureBoard.id, demoToken, captureBoardCards)
  console.log(
    `- queue examples: success=${seedPlan.queue.seededCard ? 'reused' : 'created'}, ` +
      `failure=${seedPlan.queue.hasFailedRequest ? 'reused' : 'created'}`,
  )

  // 7) Seed: Chat proposal (temporary board rename -> produces board audit log entries).
  // Restore canonical naming afterwards so final board references stay consistent.
  console.log('\nCreating a Chat session + temporary board rename proposal...')
  await ensureChatSeed(captureBoard.id, demoToken)
  console.log(`- chat seed: ${seedPlan.chat.seededSession ? 'reused existing session' : 'created seeded session'}`)

  // 8) Seed: Mention comment (Notification + audit)
  console.log('\nCreating a mention comment (generates notification)...')
  const seededCommentCard = await ensureSeededComments(captureBoard.id, demoToken, collabToken, demoUser, collabUser)
  if (seededCommentCard) {
    console.log(
      `- comments on card "${seededCommentCard.title}": demo=${seedPlan.comments.hasDemoMention ? 'reused' : 'created'}, ` +
        `collab=${seedPlan.comments.hasCollabReply ? 'reused' : 'created'}`,
    )
  } else {
    console.log('- no cards found on capture board (unexpected)')
  }

  // 9) Seed: Ops command runs (populate Ops -> Logs)
  console.log('\nRunning Ops CLI templates (generates Ops logs)...')
  await ensureOpsSeed(demoToken)
  console.log(
    `- ops evidence: health.check=${seedPlan.ops.hasHealthCheckLog ? 'reused' : 'created'}, ` +
      `boards.list=${seedPlan.ops.hasBoardsListLog ? 'reused' : 'attempted'}`,
  )

  // Summary
  const uiBase = process.env.TASKDECK_UI_BASE || 'http://localhost:5173'

  console.log('\nDemo data ready')
  console.log('----------------------------------------')
  console.log(`Login:`)
  console.log(`  username: ${DEMO.username}`)
  console.log(`  password: ${DEMO.password}`)
  console.log('\nKey URLs:')
  console.log(`  Boards:        ${uiBase}/workspace/boards`)
  console.log(`  Capture board: ${uiBase}/workspace/boards/${captureBoard.id}`)
  console.log(`  Inbox:         ${uiBase}/workspace/inbox`)
  console.log(`  Automations:   ${uiBase}/workspace/automations/proposals`)
  console.log(`  Notifications: ${uiBase}/workspace/notifications`)
  console.log(`  Activity:      ${uiBase}/workspace/activity`)
  console.log(`  Ops:           ${uiBase}/workspace/ops/cli`)
  console.log(`  Access:        ${uiBase}/workspace/settings/access/${captureBoard.id}`)
  console.log(`  Archive:       ${uiBase}/workspace/archive`)
  console.log('\nTips:')
  console.log('- If you do not see Activity/Ops/Access/Archive in the left nav, enable them in Settings -> Feature Flags.')
  console.log('- If queue/proposals look empty, confirm backend setting EnableAutoQueueProcessing=true (Development).')
}

const isDirectEntry = process.argv[1] ? path.resolve(process.argv[1]) === fileURLToPath(import.meta.url) : false

if (isDirectEntry) {
  main().catch((err) => {
    console.error('\nDemo seed failed')
    console.error(err?.stack || err)
    process.exitCode = 1
  })
}

