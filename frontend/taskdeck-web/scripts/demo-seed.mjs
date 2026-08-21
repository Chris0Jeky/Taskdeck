/**
 * Taskdeck Demo Seeder
 *
 * Goal: populate a local dev instance with enough data to make the UI feel "alive"
 * (Boards, Inbox, Proposals, Queue, Notifications, Activity, Ops).
 *
 * Usage:
 *   cd frontend/taskdeck-web
 *   npm run demo:seed
 *   npm run demo:seed -- --reset    # retire demo boards to tombstones, then recreate them
 *   npm run demo:seed -- --help     # print usage
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
import { Agent, request as nodeHttpRequest } from 'node:http'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import {
  assertSafeLocalApiTarget,
  extractListItems,
  getHostname,
  hasMoreListItems,
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
const DEV_RUN_ID_HEADER_NAME = 'taskdeck-dev-run-id'
const EMPTY_GUID_D = '00000000-0000-0000-0000-000000000000'
const CANONICAL_GUID_D_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const RUN_BOUND_RESPONSE_DEADLINE_MS = 10_000
// Three 30s tool rounds can precede a supported 600s Ollama fallback; retain 10s delivery.
const RUN_BOUND_LIVE_PROVIDER_RESPONSE_DEADLINE_MS = 700_000

let activeRunBoundTransport = null

export function createRunBoundApiTransport({
  apiBaseUrl,
  expectedRunId,
  allowNonLocal = false,
  responseDeadlineMs = RUN_BOUND_RESPONSE_DEADLINE_MS,
  setTimeoutFn = setTimeout,
  clearTimeoutFn = clearTimeout,
}) {
  if (
    typeof expectedRunId !== 'string' ||
    !CANONICAL_GUID_D_PATTERN.test(expectedRunId) ||
    expectedRunId === EMPTY_GUID_D
  ) {
    throw new Error('TASKDECK_DEV_RUN_ID must be a non-empty canonical lowercase GUID-D value.')
  }
  if (allowNonLocal) {
    throw new Error('Run-bound demo seeding cannot use TASKDECK_DEMO_ALLOW_NON_LOCAL_API.')
  }
  if (!Number.isSafeInteger(responseDeadlineMs) || responseDeadlineMs <= 0) {
    throw new Error('Run-bound demo seeding requires a positive whole-millisecond response deadline.')
  }

  let parsedApiBase
  try {
    parsedApiBase = new URL(apiBaseUrl)
  } catch (err) {
    throw new Error(`Invalid run-bound API base URL "${apiBaseUrl}". ${err?.message || err}`, {
      cause: err,
    })
  }
  if (parsedApiBase.protocol !== 'http:' || !isLocalHostname(parsedApiBase.hostname.toLowerCase())) {
    throw new Error('Run-bound demo seeding requires a direct loopback http:// API base URL.')
  }

  const apiOrigin = parsedApiBase.origin
  const apiPath = parsedApiBase.pathname.replace(/\/+$/, '')
  const readyUrl = new URL('/health/ready', apiOrigin)
  let activeRequest = false
  let closed = false
  let failedError = null
  let physicalConnectionCount = 0
  let refusedConnectionCount = 0
  let pinnedSocket = null
  let readinessVerified = false
  let agent

  const markFailed = (message, cause) => {
    if (!failedError) {
      failedError = new Error(message, cause ? { cause } : undefined)
      agent?.destroy()
    }
    return failedError
  }

  class OneConnectionAgent extends Agent {
    createConnection(options, callback) {
      // A replacement listener requires a new TCP connection, so this transport permits only one.
      if (physicalConnectionCount > 0) {
        refusedConnectionCount += 1
        const error = markFailed('Run-bound demo seeding refused to reconnect to the API listener.')
        queueMicrotask(() => callback(error))
        return undefined
      }

      physicalConnectionCount += 1
      return super.createConnection(options, callback)
    }
  }

  agent = new OneConnectionAgent({
    keepAlive: true,
    maxSockets: 1,
    maxTotalSockets: 1,
    maxFreeSockets: 1,
  })

  const assertUsable = () => {
    if (closed) {
      throw new Error('Run-bound demo seed transport is closed.')
    }
    if (failedError) {
      throw failedError
    }
  }

  const observePinnedSocket = (socket) => {
    if (!pinnedSocket) {
      pinnedSocket = socket
      socket.once('error', (err) => {
        if (!closed) markFailed('Run-bound API socket failed.', err)
      })
      socket.once('close', () => {
        if (!closed) markFailed('Run-bound API socket closed before demo seeding completed.')
      })
    }
  }

  const responseDeadlineFor = (target, method) => {
    const providerMessagePrefix = `${apiPath}/llm/chat/sessions/`
    const providerMessageSuffix = '/messages'
    const sessionId = target.pathname
      .slice(providerMessagePrefix.length, -providerMessageSuffix.length)

    if (
      method.toUpperCase() === 'POST' &&
      target.pathname.startsWith(providerMessagePrefix) &&
      target.pathname.endsWith(providerMessageSuffix) &&
      sessionId.length > 0 &&
      !sessionId.includes('/')
    ) {
      return RUN_BOUND_LIVE_PROVIDER_RESPONSE_DEADLINE_MS
    }

    return responseDeadlineMs
  }

  const performRequest = async (url, init = {}, { readiness = false } = {}) => {
    assertUsable()
    if (activeRequest) {
      throw markFailed('Concurrent run-bound demo seed requests are not allowed.')
    }

    const target = new URL(url)
    if (target.origin !== apiOrigin) {
      throw markFailed('Run-bound demo seed requests must stay on the verified API origin.')
    }
    if (!readiness && !readinessVerified) {
      throw markFailed('Run-bound API readiness must be verified before demo seed requests.')
    }

    const method = init.method || 'GET'
    const requestResponseDeadlineMs = responseDeadlineFor(target, method)
    const requestBody = init.body === undefined ? null : Buffer.from(String(init.body))
    const headers = { ...(init.headers || {}) }
    if (requestBody && !Object.keys(headers).some((name) => name.toLowerCase() === 'content-length')) {
      headers['Content-Length'] = String(requestBody.byteLength)
    }

    activeRequest = true
    try {
      return await new Promise((resolve, reject) => {
        let request
        let settled = false
        let responseDeadline

        const settle = (error, response) => {
          if (settled) return
          settled = true
          if (responseDeadline !== undefined) {
            clearTimeoutFn(responseDeadline)
            responseDeadline = undefined
          }
          if (error) reject(error)
          else resolve(response)
        }

        const failRequest = (message, cause) => {
          const error = markFailed(message, cause)
          if (request && !request.destroyed) request.destroy(error)
          settle(error)
        }

        try {
          request = nodeHttpRequest(
            target,
            {
              agent,
              method,
              headers,
            },
            (response) => {
              const chunks = []
              response.on('data', (chunk) => {
                chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk))
              })
              response.once('aborted', () => {
                failRequest('Run-bound API response was aborted before completion.')
              })
              response.once('error', (err) => {
                failRequest('Run-bound API response failed before completion.', err)
              })
              response.once('close', () => {
                if (!response.complete) {
                  failRequest('Run-bound API response closed before completion.')
                }
              })
              response.once('end', () => {
                if (!response.complete || response.aborted) {
                  failRequest('Run-bound API response was incomplete.')
                  return
                }
                if (failedError) {
                  settle(failedError)
                  return
                }

                const status = response.statusCode || 0
                const text = Buffer.concat(chunks).toString('utf8')
                settle(null, {
                  ok: status >= 200 && status < 300,
                  status,
                  rawHeaders: [...response.rawHeaders],
                  text: async () => text,
                })
              })
            },
          )
          responseDeadline = setTimeoutFn(() => {
            failRequest(`Run-bound API response exceeded the ${requestResponseDeadlineMs}ms deadline.`)
          }, requestResponseDeadlineMs)
        } catch (err) {
          failRequest('Run-bound API request could not be created.', err)
          return
        }

        request.once('socket', (socket) => {
          if (readiness) {
            observePinnedSocket(socket)
            if (socket !== pinnedSocket || request.reusedSocket) {
              failRequest('Run-bound readiness was not assigned its one new API socket.')
              return
            }
          } else if (socket !== pinnedSocket || !request.reusedSocket) {
            failRequest('Run-bound demo seed request was not assigned the verified API socket.')
            return
          }

          if (failedError || socket.destroyed) {
            failRequest('Run-bound API socket became unusable before request transmission.')
            return
          }

          // ClientRequest buffers until end(); validate the assigned socket before releasing bytes.
          request.end(requestBody || undefined)
        })
        request.once('error', (err) => {
          failRequest('Run-bound API request failed.', err)
        })
      })
    } finally {
      activeRequest = false
    }
  }

  return {
    async verifyReady() {
      const response = await performRequest(readyUrl, { method: 'GET' }, { readiness: true })
      const runIdValues = []
      for (let index = 0; index < response.rawHeaders.length; index += 2) {
        if (response.rawHeaders[index].toLowerCase() === DEV_RUN_ID_HEADER_NAME) {
          runIdValues.push(response.rawHeaders[index + 1])
        }
      }
      if (response.status !== 200 || runIdValues.length !== 1 || runIdValues[0] !== expectedRunId) {
        throw markFailed('Run-bound API readiness did not return one exact matching development run ID.')
      }
      readinessVerified = true
    },
    fetch(url, init) {
      return performRequest(url, init)
    },
    assertActive() {
      assertUsable()
      if (!readinessVerified || !pinnedSocket || pinnedSocket.destroyed) {
        throw markFailed('Run-bound API socket was not active after demo seeding.')
      }
    },
    destroy() {
      if (closed) return
      closed = true
      agent.destroy()
    },
    get diagnostics() {
      return { physicalConnectionCount, refusedConnectionCount }
    },
  }
}

export function createRunBoundApiTransportFromEnvironment({
  environment = process.env,
  apiBaseUrl = NORMALIZED_API_BASE,
} = {}) {
  if (!Object.prototype.hasOwnProperty.call(environment, 'TASKDECK_DEV_RUN_ID')) {
    return null
  }

  return createRunBoundApiTransport({
    apiBaseUrl,
    expectedRunId: environment.TASKDECK_DEV_RUN_ID,
    allowNonLocal: parseTrueishEnv(environment.TASKDECK_DEMO_ALLOW_NON_LOCAL_API),
  })
}

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
    canonicalName: 'DEMO: Client Onboarding Demo',
    reusableNames: ['DEMO: Client Onboarding Demo', 'DEMO: Capture Loop', 'DEMO: Capture Loop (Demo)'],
    description: 'Seeded demo board for review-first client onboarding workflows.',
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

const DEMO_RESET_TOMBSTONE_PREFIX = 'RESET: Taskdeck demo board '

const SEEDED_CAPTURE_TEXT = {
  ignored: 'Duplicate onboarding note from a prior client thread (demo).',
  triageApplied:
    'New client onboarding - ACME Ltd\n\n' +
    '- Request director ID documents\n' +
    '- Send engagement letter\n' +
    '- Ask for prior year accounts\n' +
    '- Request bookkeeping / software access\n' +
    '- Schedule onboarding call\n' +
    '- Confirm which records are still missing\n' +
    '- Prepare internal review once documents arrive\n',
  triagePendingAcme:
    'ACME Ltd - year-end checklist\n\n' +
    '- Chase outstanding VAT receipts from Q3\n' +
    '- Confirm payroll submissions are current\n' +
    '- Schedule pre-year-end review call with director\n',
  triagePendingNorthwind:
    'Client onboarding follow-up - Northwind Ltd\n\n' +
    '- Confirm whether bank statements were uploaded\n' +
    '- Ask client to share bookkeeping system access\n',
}

const SEEDED_QUEUE = {
  successCardTitle: 'From queue: confirm onboarding status update',
  successInstruction: 'create card "From queue: confirm onboarding status update"',
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

function toSortableTimestamp(value) {
  const parsed = Date.parse(String(value || ''))
  return Number.isFinite(parsed) ? parsed : Number.NEGATIVE_INFINITY
}

export function buildProposalLookupPath(proposalId) {
  const normalizedProposalId = String(proposalId || '').trim()
  if (!normalizedProposalId) {
    throw new Error('Proposal id is required to build a seeded proposal lookup path.')
  }

  return `/automation/proposals/${encodeURIComponent(normalizedProposalId)}`
}

function findCaptureSummaryByTextFragment(captureSummaries, boardId, text) {
  const fragment = getTextFragment(text)
  const candidates = (captureSummaries || []).filter(
    (item) => idsMatch(item?.boardId, boardId) && String(item?.textExcerpt || '').includes(fragment),
  )
  if (!candidates.length) {
    return undefined
  }

  return candidates
    .slice()
    .sort((left, right) => toSortableTimestamp(right?.createdAt) - toSortableTimestamp(left?.createdAt))[0]
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
  const expectedInstruction = String(renameInstruction || '').trim()
  if (!expectedInstruction) {
    return false
  }

  return Boolean(
    (chatMessages || []).find((message) => String(message?.content || '').trim() === expectedInstruction),
  )
}

export function collectSeededChatProposalIds(chatMessages, renameInstruction) {
  const expectedInstruction = String(renameInstruction || '').trim()
  if (!expectedInstruction) {
    return []
  }

  const proposalIds = []
  for (const message of chatMessages || []) {
    if (String(message?.content || '').trim() !== expectedInstruction) {
      continue
    }
    const proposalId = String(message?.proposalId || '').trim()
    if (!proposalId || proposalIds.includes(proposalId)) {
      continue
    }
    proposalIds.push(proposalId)
  }
  return proposalIds
}

export function mergeSeedPlanChatSessions(chatSessions, sessionId, sessionDetail) {
  if (!sessionId) {
    return chatSessions || []
  }

  return (chatSessions || []).flatMap((session) => {
    if (!idsMatch(session?.id, sessionId)) {
      return [session]
    }

    return sessionDetail ? [sessionDetail] : []
  })
}

export function shouldRecreateCaptureSeed(detail, { ignore = false, applyProposal = false } = {}) {
  const isTriaging = hasStatus(detail?.status, 'Triaging', 1)
  const isTriaged = hasStatus(detail?.status, 'Triaged', 2)
  const isProposalCreated = hasStatus(detail?.status, 'ProposalCreated', 3)
  const isConverted = hasStatus(detail?.status, 'Converted', 4)
  const isIgnored = hasStatus(detail?.status, 'Ignored', 5)
  const isFailed = hasStatus(detail?.status, 'Failed', 6)
  const hasLinkedProposal = Boolean(String(detail?.provenance?.proposalId || '').trim())

  if (ignore) {
    return isTriaging || isTriaged || isProposalCreated || isConverted
  }

  if (isConverted) {
    return !applyProposal || !hasLinkedProposal
  }

  if (hasLinkedProposal) {
    return false
  }

  return isTriaged || isIgnored || isFailed
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
      triagePendingAcme: findCaptureSummaryByTextFragment(captureSummaries, boardId, SEEDED_CAPTURE_TEXT.triagePendingAcme),
      triagePendingNorthwind: findCaptureSummaryByTextFragment(captureSummaries, boardId, SEEDED_CAPTURE_TEXT.triagePendingNorthwind),
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

  const requestInit = {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  }
  const res = activeRunBoundTransport
    ? await activeRunBoundTransport.fetch(url, requestInit)
    : await fetch(url, requestInit)

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

async function listBoards(token) {
  const limit = 200
  let offset = 0
  const boards = []

  while (true) {
    const response = await http(
      'GET',
      `/boards?includeArchived=true&offset=${offset}&limit=${limit}`,
      { token },
    )
    const items = extractListItems(response, 'GET /boards')
    boards.push(...items)

    if (!hasMoreListItems(response) || items.length === 0) {
      return boards
    }

    offset += items.length
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

export function buildDemoResetTombstoneName(boardId) {
  return `${DEMO_RESET_TOMBSTONE_PREFIX}${boardId}`
}

function findDemoBoardSpecKey(name) {
  return Object.entries(DEMO_BOARD_SPECS).find(([, spec]) => spec.reusableNames.includes(name))?.[0] || null
}

export function planDemoBoardReset(boards) {
  if (!Array.isArray(boards)) {
    throw new Error('Clean demo reset requires an unambiguous board list before changing anything.')
  }

  const candidates = []
  const tombstones = []
  const specKeys = new Set()
  const boardIds = new Set()

  for (const board of boards) {
    const id = typeof board?.id === 'string' ? board.id.trim() : ''
    if (!id) continue
    if (boardIds.has(id)) {
      throw new Error('Clean demo reset found duplicate board ids; no boards were changed.')
    }
    boardIds.add(id)
  }

  for (const board of boards) {
    const isCandidate = isDemoBoard(board)
    const isTombstone =
      typeof board?.name === 'string' && board.name.startsWith(DEMO_RESET_TOMBSTONE_PREFIX)
    if (!isCandidate && !isTombstone) continue

    const id = typeof board?.id === 'string' ? board.id.trim() : ''
    const name = typeof board?.name === 'string' ? board.name.trim() : ''
    if (!id || (isCandidate && (name === 'DEMO:' || typeof board.isArchived !== 'boolean'))) {
      throw new Error('Clean demo reset found a malformed DEMO:* board candidate; no boards were changed.')
    }
    if (isTombstone) {
      if (name !== buildDemoResetTombstoneName(id) || board.isArchived !== true) {
        throw new Error(
          'Clean demo reset found a malformed reserved reset tombstone; no boards were changed.',
        )
      }
      tombstones.push({ id, name, isArchived: true })
      continue
    }

    const specKey = findDemoBoardSpecKey(name)
    if (!specKey || specKeys.has(specKey)) {
      throw new Error(
        'Clean demo reset found an unknown, duplicate, or ambiguous DEMO:* board candidate; no boards were changed.',
      )
    }

    const tombstoneName = buildDemoResetTombstoneName(id)
    if (tombstoneName.length > 100) {
      throw new Error('Clean demo reset cannot form a safe tombstone for a malformed board id; no boards were changed.')
    }

    specKeys.add(specKey)
    candidates.push({ id, name, specKey, isArchived: board.isArchived, tombstoneName })
  }

  return { candidates, tombstones }
}

export async function quarantineDemoBoardsForCleanReset(
  candidates,
  token,
  { request = http, onQuarantined = () => {} } = {},
) {
  let quarantinedCount = 0
  for (const board of candidates) {
    try {
      await request('PUT', `/boards/${board.id}`, {
        token,
        body: {
          name: board.tombstoneName || buildDemoResetTombstoneName(board.id),
          isArchived: true,
        },
      })
      quarantinedCount += 1
      onQuarantined(board)
    } catch (err) {
      throw new Error(
        `Clean demo reset stopped after quarantining ${quarantinedCount} of ${candidates.length} DEMO:* board(s); ` +
          'no reseed was attempted. The launcher will clean only its owned process tree, so inspect the remaining demo state before retrying.',
        { cause: err },
      )
    }
  }
}

function verifyDemoBoardQuarantine(resetPlan, boards) {
  const freshPlan = planDemoBoardReset(boards)
  const tombstoneIds = new Set(freshPlan.tombstones.map((board) => board.id))
  const missingTombstone = resetPlan.candidates.find((board) => !tombstoneIds.has(board.id))

  if (freshPlan.candidates.length || missingTombstone) {
    throw new Error(
      'Clean demo reset could not verify the archived tombstone state; no fresh boards or demo artifacts were seeded.',
    )
  }

  return freshPlan
}

function pickReusableBoard(boards, reusableNames) {
  const names = new Set(reusableNames)
  const candidates = (boards || []).filter((b) => names.has(b.name))
  if (!candidates.length) return null

  return candidates.find((b) => !b.isArchived) || candidates[0]
}

async function createDemoBoard(spec, token, request = http) {
  const created = await request('POST', '/boards', {
    token,
    body: {
      name: spec.canonicalName,
      description: spec.description,
    },
  })
  const createdId = typeof created?.id === 'string' ? created.id.trim() : ''
  if (!createdId) {
    throw new Error('Clean demo reset received a malformed created board; no demo artifacts were seeded.')
  }
  if (spec.isArchived) {
    const archived = await request('PUT', `/boards/${createdId}`, {
      token,
      body: { isArchived: true },
    })
    return archived || { ...created, isArchived: true }
  }
  return created
}

async function ensureDemoBoard(spec, boards, token, request = http) {
  const existing = pickReusableBoard(boards, spec.reusableNames)
  if (!existing) {
    return createDemoBoard(spec, token, request)
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

  const updated = await request('PUT', `/boards/${existing.id}`, {
    token,
    body: updateBody,
  })
  return updated || { ...existing, ...updateBody }
}

function validateFreshCanonicalDemoBoards(canonicalBoards, preResetBoardIds, boards) {
  const createdIds = new Set()

  for (const [specKey, spec] of Object.entries(DEMO_BOARD_SPECS)) {
    const board = canonicalBoards[specKey]
    const id = typeof board?.id === 'string' ? board.id.trim() : ''
    if (
      !id ||
      preResetBoardIds.has(id) ||
      createdIds.has(id) ||
      board.name !== spec.canonicalName ||
      board.isArchived !== spec.isArchived
    ) {
      throw new Error(
        'Clean demo reset could not verify fresh canonical board creation; no demo artifacts were seeded.',
      )
    }
    createdIds.add(id)
  }

  const persistedPlan = planDemoBoardReset(boards)
  if (persistedPlan.candidates.length !== Object.keys(DEMO_BOARD_SPECS).length) {
    throw new Error(
      'Clean demo reset could not verify the persisted canonical board set; no demo artifacts were seeded.',
    )
  }

  for (const candidate of persistedPlan.candidates) {
    const expected = canonicalBoards[candidate.specKey]
    const persisted = boards.find((board) => board.id === candidate.id)
    const spec = DEMO_BOARD_SPECS[candidate.specKey]
    if (
      !expected ||
      candidate.id !== expected.id ||
      candidate.name !== spec.canonicalName ||
      persisted?.isArchived !== spec.isArchived
    ) {
      throw new Error(
        'Clean demo reset could not verify the persisted canonical board set; no demo artifacts were seeded.',
      )
    }
  }

  return persistedPlan
}

export async function prepareDemoBoardsForSeed(
  boards,
  token,
  {
    reset = false,
    request = http,
    refreshBoards = () => listBoards(token),
    onQuarantined = () => {},
  } = {},
) {
  let demoBoards = (boards || []).filter(isDemoBoard)
  let resetPlan = null
  let canonicalBoards

  if (reset) {
    const preResetBoardIds = new Set(
      boards
        .map((board) => (typeof board?.id === 'string' ? board.id.trim() : ''))
        .filter(Boolean),
    )
    resetPlan = planDemoBoardReset(boards)
    await quarantineDemoBoardsForCleanReset(resetPlan.candidates, token, { request, onQuarantined })

    let quarantinedBoards
    try {
      quarantinedBoards = await refreshBoards()
      verifyDemoBoardQuarantine(resetPlan, quarantinedBoards)
    } catch (err) {
      throw new Error(
        'Clean demo reset could not verify the archived tombstone state; no fresh boards or demo artifacts were seeded.',
        { cause: err },
      )
    }

    canonicalBoards = {}
    for (const [specKey, spec] of Object.entries(DEMO_BOARD_SPECS)) {
      canonicalBoards[specKey] = await createDemoBoard(spec, token, request)
    }

    let persistedBoards
    try {
      persistedBoards = await refreshBoards()
      validateFreshCanonicalDemoBoards(canonicalBoards, preResetBoardIds, persistedBoards)
    } catch (err) {
      throw new Error(
        'Clean demo reset could not verify fresh canonical board creation; no demo artifacts were seeded.',
        { cause: err },
      )
    }
    demoBoards = persistedBoards.filter(isDemoBoard)
  } else {
    canonicalBoards = {
      capture: await ensureDemoBoard(DEMO_BOARD_SPECS.capture, demoBoards, token, request),
      content: await ensureDemoBoard(DEMO_BOARD_SPECS.content, demoBoards, token, request),
      blank: await ensureDemoBoard(DEMO_BOARD_SPECS.blank, demoBoards, token, request),
      archived: await ensureDemoBoard(DEMO_BOARD_SPECS.archived, demoBoards, token, request),
    }
  }

  return { ...canonicalBoards, demoBoards, resetPlan }
}

async function ensureUser({ username, email, password }) {
  const loginBody = { usernameOrEmail: username, password }
  let loginError

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
        `${err?.message || err}`,
      { cause: err }
    )
  }

  try {
    return await http('POST', '/auth/login', { body: loginBody })
  } catch (err) {
    const registerStatus = getHttpStatus(err)
    throw new Error(
      `Registered "${username}" but follow-up login failed${registerStatus ? ` (${registerStatus})` : ''}.\n\n` +
        `${err?.message || err}`,
      { cause: err }
    )
  }
}

async function waitFor(fn, { timeoutMs = 45000, intervalMs = 750, label = 'condition' } = {}) {
  const start = Date.now()
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

  // Dry-run first to detect conflicts before committing changes.
  const preview = await http('POST', `/boards/${boardId}/starter-packs/apply`, {
    token,
    body: { manifest, dryRun: true },
  })

  const blockingConflicts = (preview?.conflicts || []).filter(
    (c) => !c.severity || c.severity.toLowerCase() === 'blocking',
  )

  // If every action is "skip" and there are no blocking conflicts, the pack
  // is already fully applied — no need to re-apply.
  const allActions = preview?.actions || []
  const allSkipped =
    allActions.length > 0 &&
    allActions.every((a) => (a.operation || '').toLowerCase() === 'skip') &&
    blockingConflicts.length === 0

  if (allSkipped) {
    console.log(`  (starter pack "${packId}" already applied — skipping)`)
    return preview
  }

  if (blockingConflicts.length > 0) {
    const conflictSummary = blockingConflicts
      .map((c, i) => {
        const code = typeof c.code === 'string' && c.code.trim() ? c.code : `#${i + 1}`
        const message = typeof c.message === 'string' && c.message.trim() ? c.message : JSON.stringify(c)
        return `  - ${code}: ${message}`
      })
      .join('\n')
    throw new Error(
      `Starter pack "${packId}" has blocking conflicts on board ${boardId}:\n${conflictSummary}`,
    )
  }

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
        throw new Error('Board access conflict reported but collaborator access entry was not found.', { cause: err })
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
  if (!proposalId) {
    return null
  }

  try {
    return await http('GET', buildProposalLookupPath(proposalId), { token })
  } catch (err) {
    if (getHttpStatus(err) === 404) {
      return null
    }
    throw err
  }
}

async function getChatSessionDetail(sessionId, token) {
  if (!sessionId) {
    return null
  }

  try {
    return await http('GET', `/llm/chat/sessions/${sessionId}`, { token })
  } catch (err) {
    if (getHttpStatus(err) === 404) {
      return null
    }
    throw err
  }
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
  if (shouldRecreateCaptureSeed(detail, { ignore, applyProposal })) {
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
    const isTerminal = shouldRecreateCaptureSeed(detail, { applyProposal })
    if (!isTerminal) {
      await http('POST', `/capture/items/${summary.id}/triage`, { token })
    } else {
      throw new Error(`Seeded capture ${summary.id} is terminal without a proposal and must be recreated.`)
    }

    detail = await waitFor(
      async () => {
        const refreshed = await getCaptureDetail(summary, token)
        if (shouldRecreateCaptureSeed(refreshed, { applyProposal })) {
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
  let seededProposalIds = collectSeededChatProposalIds(sessionDetail?.recentMessages, renameInstruction)

  if (session && !sessionDetail) {
    session = null
    hasSeededMessage = false
    seededProposalIds = []
  }

  if (!session) {
    session = await http('POST', '/llm/chat/sessions', {
      token,
      body: { title: SEEDED_CHAT.sessionTitle, boardId },
    })
    hasSeededMessage = false
    seededProposalIds = []
  }

  let appliedRenameProposal = false
  if (seededProposalIds.length > 0) {
    for (const proposalId of seededProposalIds) {
      await ensureProposalApplied(proposalId, token)
      appliedRenameProposal = true
    }
  } else if (!hasSeededMessage) {
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

async function seedDemo({ reset = false } = {}) {
  ensureSafeApiBaseTarget()

  console.log(`\nTaskdeck demo seeder -> ${NORMALIZED_API_BASE}`)
  if (reset) console.log('  --reset: will quarantine documented demo boards and create fresh replacements')
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

  // 2) Reuse canonical boards normally; protected reset retires them to ID-bound tombstones first.
  const boards = await listBoards(demoToken)
  const preparedBoards = await prepareDemoBoardsForSeed(boards, demoToken, {
    reset,
    refreshBoards: () => listBoards(demoToken),
    onQuarantined: (board) => console.log(`  - quarantined ${board.name}`),
  })
  const {
    capture: captureBoard,
    content: contentBoard,
    blank: blankBoard,
    archived: archivedBoard,
    demoBoards,
    resetPlan,
  } = preparedBoards

  if (reset) {
    console.log(
      `\n--reset: quarantined ${resetPlan.candidates.length} documented DEMO:* board(s) and verified fresh canonical replacements`,
    )
  }

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
  await applyStarterPack(captureBoard.id, demoToken, 'board-blueprint-client-onboarding')
  console.log('- capture board: client onboarding blueprint')

  await applyStarterPack(contentBoard.id, demoToken, 'board-blueprint-content-calendar')
  console.log('- content board: content calendar blueprint')

  // 4) Seed: board access entry (so Access view is not empty)
  await ensureCollaboratorEditorAccess(captureBoard.id, demoToken, collabUser.id)

  const captureSummaries = await listCaptureSummaries(captureBoard.id, demoToken)
  const captureBoardCards = (await http('GET', `/boards/${captureBoard.id}/cards`, { token: demoToken })) || []
  const queueRequests = (await http('GET', '/llm-queue/user?limit=200', { token: demoToken })) || []
  const chatSessions = (await http('GET', '/llm/chat/sessions', { token: demoToken })) || []
  const seededChatSession = findChatSessionByTitle(chatSessions, captureBoard.id, SEEDED_CHAT.sessionTitle)
  const seededChatSessionDetail = seededChatSession?.id ? await getChatSessionDetail(seededChatSession.id, demoToken) : null
  const plannedChatSessions = mergeSeedPlanChatSessions(chatSessions, seededChatSession?.id, seededChatSessionDetail)
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
  const triagePendingAcmeWithProposal = await ensureCaptureSeed(captureBoard.id, demoToken, SEEDED_CAPTURE_TEXT.triagePendingAcme)
  const triagePendingNorthwindWithProposal = await ensureCaptureSeed(captureBoard.id, demoToken, SEEDED_CAPTURE_TEXT.triagePendingNorthwind)

  console.log(
    `- capture items: ignored=${seedPlan.captures.ignored ? 'reused' : 'created'}, ` +
      `triage-applied=${seedPlan.captures.triageApplied ? 'reused' : 'created'}, ` +
      `triage-pending-acme=${seedPlan.captures.triagePendingAcme ? 'reused' : 'created'}, ` +
      `triage-pending-northwind=${seedPlan.captures.triagePendingNorthwind ? 'reused' : 'created'}`,
  )
  console.log(`- triage (applied) proposal: ${triageAppliedWithProposal.provenance.proposalId}`)
  console.log(`- triage (pending/ACME) proposal: ${triagePendingAcmeWithProposal.provenance.proposalId} (left for review)`)
  console.log(`- triage (pending/Northwind) proposal: ${triagePendingNorthwindWithProposal.provenance.proposalId} (left for review)`)

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

export async function main(options = {}) {
  const transport = createRunBoundApiTransportFromEnvironment()
  if (!transport) {
    return seedDemo(options)
  }
  if (activeRunBoundTransport) {
    transport.destroy()
    throw new Error('Concurrent run-bound demo seed runs are not allowed.')
  }

  activeRunBoundTransport = transport
  try {
    await transport.verifyReady()
    const result = await seedDemo(options)
    transport.assertActive()
    return result
  } finally {
    if (activeRunBoundTransport === transport) {
      activeRunBoundTransport = null
    }
    transport.destroy()
  }
}

const isDirectEntry = process.argv[1] ? path.resolve(process.argv[1]) === fileURLToPath(import.meta.url) : false

function printUsage() {
  console.log(`
Usage: npm run demo:seed [-- [options]]

Options:
  --reset      Quarantine documented demo boards and create fresh replacements
  --help, -h   Print this usage information and exit

Environment variables:
  TASKDECK_API_BASE_URL              API base URL (default: http://localhost:5000/api)
  TASKDECK_API_BASE                  Legacy alias for TASKDECK_API_BASE_URL
  TASKDECK_DEMO_USERNAME             Demo user name (default: demo)
  TASKDECK_DEMO_EMAIL                Demo user email (default: demo@taskdeck.local)
  TASKDECK_DEMO_PASSWORD             Demo user password (default: demo123)
  TASKDECK_COLLAB_USERNAME           Collab user name (default: collab)
  TASKDECK_COLLAB_EMAIL              Collab user email (default: collab@taskdeck.local)
  TASKDECK_COLLAB_PASSWORD           Collab user password (default: demo123)
  TASKDECK_DEMO_COLLAB_USER          Legacy alias for TASKDECK_COLLAB_USERNAME
  TASKDECK_DEMO_COLLAB_EMAIL         Legacy alias for TASKDECK_COLLAB_EMAIL
  TASKDECK_DEMO_COLLAB_PASS          Legacy alias for TASKDECK_COLLAB_PASSWORD
  TASKDECK_DEMO_ALLOW_NON_LOCAL_API  Allow non-local API targets (default: false)
  TASKDECK_UI_BASE                   UI base URL (default: http://localhost:5173)
`.trim())
}

export function parseSeedArgs(argv) {
  const args = argv.slice(2)
  return {
    help: args.includes('--help') || args.includes('-h'),
    reset: args.includes('--reset'),
  }
}

if (isDirectEntry) {
  const flags = parseSeedArgs(process.argv)

  if (flags.help) {
    printUsage()
    process.exit(0)
  }

  main({ reset: flags.reset }).catch((err) => {
    console.error('\nDemo seed failed')
    console.error(err?.stack || err)
    process.exitCode = 1
  })
}

