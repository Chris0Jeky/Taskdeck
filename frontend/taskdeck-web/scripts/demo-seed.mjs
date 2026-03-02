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
    throw new Error(`${method} ${path} failed (${res.status})\n${msg}`)
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

async function ensureUser({ username, email, password }) {
  try {
    const login = await http('POST', '/auth/login', {
      body: { usernameOrEmail: username, password },
    })
    return login
  } catch {
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

    return await http('POST', '/auth/login', {
      body: { usernameOrEmail: username, password },
    })
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

  // 2) Clean up previous demo boards
  const boards = await http('GET', '/boards?includeArchived=true', { token: demoToken })
  const demoBoards = (boards || []).filter((b) => typeof b.name === 'string' && b.name.startsWith('DEMO:'))
  if (demoBoards.length) {
    console.log(`\nCleaning ${demoBoards.length} existing DEMO:* board(s)...`)
    for (const b of demoBoards) {
      await http('DELETE', `/boards/${b.id}`, { token: demoToken })
      console.log(`- deleted ${b.name}`)
    }
  }

  // 3) Create boards
  console.log('\nCreating demo boards...')
  const captureBoard = await http('POST', '/boards', {
    token: demoToken,
    body: {
      name: 'DEMO: Capture Loop',
      description: 'Seeded demo board (capture -> triage -> proposal -> apply).',
    },
  })

  const contentBoard = await http('POST', '/boards', {
    token: demoToken,
    body: {
      name: 'DEMO: Content Calendar',
      description: 'Seeded via Starter Pack blueprint (content calendar).',
    },
  })

  const blankBoard = await http('POST', '/boards', {
    token: demoToken,
    body: {
      name: 'DEMO: Blank Board',
      description: 'Intentionally empty board to demo Starter Packs UI.',
    },
  })

  const archivedBoard = await http('POST', '/boards', {
    token: demoToken,
    body: {
      name: 'DEMO: Archived Board',
      description: 'Used to demo Archive view (archived boards list).',
    },
  })

  // 4) Seed: starter packs
  console.log('\nApplying starter packs...')
  await applyStarterPack(captureBoard.id, demoToken, 'common-column-flow-kanban')
  await applyStarterPack(captureBoard.id, demoToken, 'common-labels-core')
  console.log('- capture board: kanban columns + common labels')

  await applyStarterPack(contentBoard.id, demoToken, 'board-blueprint-content-calendar')
  console.log('- content board: content calendar blueprint')

  // 5) Seed: archive board
  await http('PUT', `/boards/${archivedBoard.id}`, {
    token: demoToken,
    body: { isArchived: true },
  })
  console.log('- archived board: archived')

  // 6) Seed: board access entry (so Access view is not empty)
  await http('POST', `/boards/${captureBoard.id}/access`, {
    token: demoToken,
    body: {
      userId: collabUser.id,
      role: 2, // Editor
    },
  })
  console.log('- access: granted collab user Editor on capture board')

  // 7) Seed: Inbox items (ignored + triage)
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

  // 8) Seed: Queue requests (1 success + 1 failure)
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

  // 9) Seed: Chat proposal (board rename -> produces board audit log entries)
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

  // 10) Seed: Mention comment (Notification + audit)
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

  // 11) Seed: Ops command runs (populate Ops -> Logs)
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
  console.log(`  Access:        ${uiBase}/workspace/access (use boardId: ${captureBoard.id})`)
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
