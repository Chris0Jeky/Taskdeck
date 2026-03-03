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
 *   TASKDECK_UI_BASE            (default: http://localhost:4173)
 */

import { randomUUID } from 'node:crypto'

const API_BASE = (
  process.env.TASKDECK_API_BASE_URL ||
  process.env.TASKDECK_API_BASE ||
  'http://localhost:5000/api'
).replace(/\/$/, '')

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

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

async function http(method, path, { token, body, headers: extraHeaders } = {}) {
  const url = `${API_BASE}${path.startsWith('/') ? '' : '/'}${path}`

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

async function main() {
  console.log(`\nTaskdeck demo seeder -> ${API_BASE}`)
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
      await http('DELETE', `/boards/${b.id}`, { token: demoToken })
      console.log(`- archived ${b.name}`)
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
  try {
    await http('POST', `/boards/${captureBoard.id}/access`, {
      token: demoToken,
      body: {
        userId: collabUser.id,
        role: 2, // Editor
      },
    })
    console.log('- access: granted collab user Editor on capture board')
  } catch (err) {
    if (getHttpStatus(err) === 409) {
      console.log('- access: collab user already has board access (kept existing entry)')
    } else {
      throw err
    }
  }

  // 5) Seed: Inbox items (ignored + triage)
  console.log('\nCreating Inbox items...')
  const ignored = await http('POST', '/capture/items', {
    token: demoToken,
    body: {
      boardId: captureBoard.id,
      text: 'This item is ignored (demo).',
      source: 'Typed',
    },
  })
  await http('POST', `/capture/items/${ignored.id}/ignore`, { token: demoToken })

  const triageApplied = await http('POST', '/capture/items', {
    token: demoToken,
    body: {
      boardId: captureBoard.id,
      text:
        '- [ ] Draft a 5-minute stakeholder demo script\n' +
        '- [ ] Add onboarding empty-states for scaffolding pages\n' +
        '- [ ] Create demo seeding harness that populates Inbox/Proposals/Notifications\n',
      source: 'Typed',
    },
  })
  await http('POST', `/capture/items/${triageApplied.id}/triage`, { token: demoToken })

  const triagePending = await http('POST', '/capture/items', {
    token: demoToken,
    body: {
      boardId: captureBoard.id,
      text:
        '- [ ] Follow up: connect Activity view to real audit queries\n' +
        '- [ ] Follow up: simplify Automation Queue composer\n',
      source: 'Typed',
    },
  })
  await http('POST', `/capture/items/${triagePending.id}/triage`, { token: demoToken })

  console.log('- created 2 triage items (one will be applied, one left pending) + 1 ignored')

  // Wait for triage 1 proposal
  const triageAppliedWithProposal = await waitFor(
    async () => {
      const item = await http('GET', `/capture/items/${triageApplied.id}`, { token: demoToken })
      const proposalId = item?.provenance?.proposalId
      return proposalId ? item : null
    },
    { label: 'capture triage (applied) to produce a proposalId' }
  )

  const triageProposalId = triageAppliedWithProposal.provenance.proposalId
  console.log(`- triage (applied) proposal: ${triageProposalId}`)

  // Approve + execute triage proposal so the board has cards
  await http('POST', `/automation/proposals/${triageProposalId}/approve`, { token: demoToken })
  await http('POST', `/automation/proposals/${triageProposalId}/execute`, {
    token: demoToken,
    headers: { 'Idempotency-Key': randomUUID() },
  })
  console.log('- triage (applied) proposal approved + executed (creates cards)')

  // Wait for triage 2 proposal (leave it pending)
  const triagePendingWithProposal = await waitFor(
    async () => {
      const item = await http('GET', `/capture/items/${triagePending.id}`, { token: demoToken })
      const proposalId = item?.provenance?.proposalId
      return proposalId ? item : null
    },
    { label: 'capture triage (pending) to produce a proposalId' }
  )
  console.log(`- triage (pending) proposal: ${triagePendingWithProposal.provenance.proposalId} (left for review)`)

  // 6) Seed: Queue requests (1 success + 1 failure)
  console.log('\nCreating Automation Queue items...')
  const okInstruction = `create card "From queue: demo item (${new Date().toISOString()})"`
  const okReq = await http('POST', '/llm-queue', {
    token: demoToken,
    body: {
      requestType: 'instruction',
      payload: okInstruction,
      boardId: captureBoard.id,
    },
  })

  const badReq = await http('POST', '/llm-queue', {
    token: demoToken,
    body: {
      requestType: 'instruction',
      payload: 'create board named joji',
      boardId: captureBoard.id,
    },
  })

  // Wait until both requests are no longer pending
  await waitFor(
    async () => {
      const items = await http('GET', `/llm-queue/user?limit=200`, { token: demoToken })
      const ok = (items || []).find((r) => r.id === okReq.id)
      const bad = (items || []).find((r) => r.id === badReq.id)
      const okDone = ok?.status === 2 || ok?.status === 'Completed' || ok?.errorMessage
      const badDone = bad?.status === 3 || bad?.status === 'Failed' || bad?.errorMessage
      return okDone && badDone
    },
    { label: 'queue items to finish processing' }
  )
  console.log('- submitted 1 valid + 1 invalid queue instruction')

  // Find and apply the proposal created from the OK request (sourceReferenceId == request id)
  const proposals = await http('GET', '/automation/proposals?includeOperations=true&limit=200', { token: demoToken })
  const queueProposal = (proposals || []).find((p) => p.sourceReferenceId === okReq.id)
  if (queueProposal) {
    await http('POST', `/automation/proposals/${queueProposal.id}/approve`, { token: demoToken })
    await http('POST', `/automation/proposals/${queueProposal.id}/execute`, {
      token: demoToken,
      headers: { 'Idempotency-Key': randomUUID() },
    })
    console.log(`- queue proposal approved + executed: ${queueProposal.id}`)
  } else {
    console.log('- queue proposal not found (skipping execute)')
  }

  // 7) Seed: Chat proposal (board rename -> produces board audit log entries)
  console.log('\nCreating a Chat session + board rename proposal...')
  const session = await http('POST', '/llm/chat/sessions', {
    token: demoToken,
    body: { title: 'Stakeholder Demo', boardId: captureBoard.id },
  })

  const renameInstruction = 'rename board to "DEMO: Capture Loop (Demo)"'
  const chatMsg = await http('POST', `/llm/chat/sessions/${session.id}/messages`, {
    token: demoToken,
    body: { content: renameInstruction, requestProposal: true },
  })

  if (chatMsg?.proposalId) {
    await http('POST', `/automation/proposals/${chatMsg.proposalId}/approve`, { token: demoToken })
    await http('POST', `/automation/proposals/${chatMsg.proposalId}/execute`, {
      token: demoToken,
      headers: { 'Idempotency-Key': randomUUID() },
    })
    console.log(`- board rename proposal approved + executed: ${chatMsg.proposalId}`)
  } else {
    console.log('- chat message did not return a proposalId (unexpected)')
  }

  // 8) Seed: Mention comment (Notification + audit)
  console.log('\nCreating a mention comment (generates notification)...')
  const cards = await http('GET', `/boards/${captureBoard.id}/cards`, { token: demoToken })
  const firstCard = (cards || [])[0]
  if (firstCard) {
    await http('POST', `/boards/${captureBoard.id}/cards/${firstCard.id}/comments`, {
      token: demoToken,
      body: { content: `Heads up @${collabUser.username} - this is a seeded mention for the Notifications view.` },
    })

    // Reply from collaborator mentioning the demo user, so the demo account also sees a notification.
    await http('POST', `/boards/${captureBoard.id}/cards/${firstCard.id}/comments`, {
      token: collabToken,
      body: { content: `@${demoUser.username} ack - I will take a look after lunch. (seeded)` },
    })

    console.log(`- comment added on card: ${firstCard.title}`)
  } else {
    console.log('- no cards found on capture board (unexpected)')
  }

  // 9) Seed: Ops command runs (populate Ops -> Logs)
  console.log('\nRunning Ops CLI templates (generates Ops logs)...')
  await http('POST', '/ops/cli/run', {
    token: demoToken,
    body: { commandName: 'health.check', parameters: {} },
  })
  try {
    await http('POST', '/ops/cli/run', {
      token: demoToken,
      body: { commandName: 'boards.list', parameters: {} },
    })
    console.log('- ran health.check + boards.list')
  } catch {
    console.log('- ran health.check (boards.list requires admin role - skipped)')
  }

  // Summary
  const uiBase = process.env.TASKDECK_UI_BASE || 'http://localhost:4173'

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

main().catch((err) => {
  console.error('\nDemo seed failed')
  console.error(err?.stack || err)
  process.exitCode = 1
})

