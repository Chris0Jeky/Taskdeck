#!/usr/bin/env node

/**
 * Demo autopilot: a simulated user that repeatedly performs valid Taskdeck actions.
 *
 * Brains:
 * - heuristic (default): deterministic-ish random actions with no external services.
 * - taskdeck-chat: asks Taskdeck Chat to decide the next action.
 *
 * Loops:
 * - queue: Queue -> Proposal -> Approve -> Execute.
 * - capture: Capture -> Triage -> Proposal -> Approve -> Execute.
 * - mixed: probabilistically chooses queue or capture for each turn.
 */

import { randomUUID } from 'node:crypto'
import { setTimeout as sleep } from 'node:timers/promises'

import {
  TaskdeckApiClient,
  applyStarterPack,
  approveAndExecuteProposal,
  createCaptureItem,
  enqueueAndApplyInstruction,
  ensureUser,
  getDemoConfig,
  summarizeBoardForAgent,
  traceEvent,
  triageCaptureItem,
  waitForCaptureOutcome,
} from './demo-lib.mjs'

function parseArgs(argv) {
  const args = {
    boardId: null,
    boardName: 'DEMO: Autopilot Playground',
    createBoard: true,
    turns: 15,
    intervalMs: 1500,
    brain: 'heuristic',
    loop: 'mixed',
    captureProb: 0.35,
    leaveCaptureUntriagedProb: 0.1,
    triageTimeoutMs: 90_000,
    captureSource: 'Typed',
    captureTitleHint: null,
    seed: null,
    rngSeed: null,
  }

  for (let i = 2; i < argv.length; i++) {
    const value = argv[i]
    if (!value) continue

    if (value === '--no-create-board') args.createBoard = false
    else if (value === '--create-board') args.createBoard = true
    else if (value === '--brain') args.brain = argv[++i] ?? args.brain
    else if (value === '--loop') args.loop = argv[++i] ?? args.loop
    else if (value === '--capture-prob') args.captureProb = Number(argv[++i] ?? args.captureProb)
    else if (value === '--leave-capture-untriaged-prob') {
      args.leaveCaptureUntriagedProb = Number(argv[++i] ?? args.leaveCaptureUntriagedProb)
    } else if (value === '--triage-timeout-ms') args.triageTimeoutMs = Number(argv[++i] ?? args.triageTimeoutMs)
    else if (value === '--capture-source') args.captureSource = argv[++i] ?? args.captureSource
    else if (value === '--capture-title-hint') args.captureTitleHint = argv[++i] ?? args.captureTitleHint
    else if (value === '--board-id') args.boardId = argv[++i] ?? null
    else if (value === '--board') args.boardName = argv[++i] ?? args.boardName
    else if (value === '--turns') args.turns = Number(argv[++i] ?? args.turns)
    else if (value === '--interval-ms') args.intervalMs = Number(argv[++i] ?? args.intervalMs)
    else if (value === '--seed') args.seed = argv[++i] ?? '0'
    else if (value === '--rng-seed') args.rngSeed = argv[++i] ?? args.rngSeed
  }

  if (args.rngSeed !== null && args.rngSeed !== undefined) {
    args.seed = args.rngSeed
  }

  if (!['queue', 'capture', 'mixed'].includes(args.loop)) {
    throw new Error(`Invalid --loop: ${args.loop} (expected queue|capture|mixed)`)
  }

  if (!['heuristic', 'taskdeck-chat'].includes(args.brain)) {
    throw new Error(`Invalid --brain: ${args.brain} (expected heuristic|taskdeck-chat)`)
  }

  if (!Number.isFinite(args.captureProb) || args.captureProb < 0 || args.captureProb > 1) {
    throw new Error(`Invalid --capture-prob: ${args.captureProb} (expected 0..1)`)
  }

  if (
    !Number.isFinite(args.leaveCaptureUntriagedProb) ||
    args.leaveCaptureUntriagedProb < 0 ||
    args.leaveCaptureUntriagedProb > 1
  ) {
    throw new Error(
      `Invalid --leave-capture-untriaged-prob: ${args.leaveCaptureUntriagedProb} (expected 0..1)`,
    )
  }

  if (!Number.isFinite(args.turns) || !Number.isInteger(args.turns) || args.turns <= 0) {
    throw new Error(`Invalid --turns: ${String(args.turns)} (expected positive integer)`)
  }

  if (!Number.isFinite(args.intervalMs) || !Number.isInteger(args.intervalMs) || args.intervalMs < 0) {
    throw new Error(`Invalid --interval-ms: ${String(args.intervalMs)} (expected non-negative integer)`)
  }

  if (!Number.isFinite(args.triageTimeoutMs) || !Number.isInteger(args.triageTimeoutMs) || args.triageTimeoutMs <= 0) {
    throw new Error(`Invalid --triage-timeout-ms: ${String(args.triageTimeoutMs)} (expected positive integer)`)
  }

  return args
}

function createRandom(seed) {
  if (seed === null || seed === undefined) {
    return Math.random
  }

  const input = String(seed)
  let state = 2166136261
  for (let i = 0; i < input.length; i++) {
    state ^= input.charCodeAt(i)
    state = Math.imul(state, 16777619)
  }

  let value = state >>> 0
  return () => {
    value += 0x6d2b79f5
    let t = Math.imul(value ^ (value >>> 15), 1 | value)
    t ^= t + Math.imul(t ^ (t >>> 7), 61 | t)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

function pickOne(items, random) {
  return items[Math.floor(random() * items.length)]
}

const INSTRUCTION_PATTERNS = [
  // Keep card-id shape aligned with backend planner expectations (GUID-like token).
  // This prevents chat-mode from accepting obviously invalid ids and skipping heuristic fallback.
  // Pattern is case-insensitive.
  /^create card\s+"[^"]+"(?:\s+in column\s+"[^"]+")?(?:\s+with description\s+"[^"]+")?\s*$/i,
  /^rename board to\s+"[^"]+"\s*$/i,
  /^update board description\s+"[^"]+"\s*$/i,
  /^move column\s+"[^"]+"\s+to position\s+\d+\s*$/i,
  /^update card\s+[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\s+title\s+"[^"]+"\s*$/i,
  /^update card\s+[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\s+description\s+"[^"]+"\s*$/i,
  /^move card\s+[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\s+to column\s+"[^"]+"\s*$/i,
  /^archive card\s+[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\s*$/i,
  /^archive cards matching\s+"[^"]+"\s*$/i,
  /^unarchive board\s*$/i,
  /^archive board\s*$/i,
]

function isValidInstruction(input) {
  const value = (input ?? '').trim()
  if (!value) return false
  return INSTRUCTION_PATTERNS.some((pattern) => pattern.test(value))
}

function normalizeBrainLine(line) {
  const raw = (line || '').trim()
  if (!raw) return null

  const lines = raw
    .split(/\r?\n/)
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0)
  if (lines.length === 0) return null

  const firstLine = lines[0]?.trim() || ''
  const separatorIndex = firstLine.indexOf(':')
  const prefix = (separatorIndex === -1 ? firstLine : firstLine.slice(0, separatorIndex)).trim().toUpperCase()

  if (isValidInstruction(firstLine)) {
    return { kind: 'instruction', instruction: firstLine }
  }

  if (prefix === 'INSTRUCTION') {
    const instruction = separatorIndex === -1 ? '' : firstLine.slice(separatorIndex + 1).trim()
    if (!isValidInstruction(instruction)) return null
    return { kind: 'instruction', instruction }
  }

  if (prefix === 'CAPTURE') {
    const inlineText = separatorIndex === -1 ? '' : firstLine.slice(separatorIndex + 1).trim()
    const captureLines = []
    if (inlineText) captureLines.push(inlineText)
    captureLines.push(...lines.slice(1))
    if (captureLines.length === 0 || captureLines.length > 6) return null
    const text = captureLines.join('\n').trim()
    return { kind: 'capture', text }
  }

  return null
}

async function decideHeuristicInstruction({ columns, cards, turn, random, deterministic }) {
  const columnNames = (columns || []).map((column) => column.name).filter(Boolean)
  const columnNameById = new Map((columns || []).map((column) => [column.id, column.name]))
  const allCards = (cards || []).filter((card) => !!card?.id)

  const marker = deterministic ? `turn-${turn + 1}` : new Date().toISOString()
  const actions = []

  if (columnNames.length > 0) {
    actions.push(
      () =>
        `create card "Agent task: ${marker}" in column "${pickOne(columnNames, random)}" ` +
        'with description "generated by demo-autopilot"',
    )
  } else {
    actions.push(() => `create card "Agent task: ${marker}" with description "generated by demo-autopilot"`)
  }

  if (allCards.length > 0 && columnNames.length > 0) {
    const card = pickOne(allCards, random)
    const currentColumnName = columnNameById.get(card.columnId)
    const moveDestinations = columnNames.filter((columnName) => columnName !== currentColumnName)

    if (moveDestinations.length > 0) {
      const destination = pickOne(moveDestinations, random)
      actions.push(() => `move card ${card.id} to column "${destination}"`)
    }

    actions.push(() => `update card ${card.id} title "Autopilot touched ${marker}"`)
  }

  return pickOne(actions, random)()
}

async function decideHeuristicCapture({ board, columns, cards, random }) {
  const columnNames = (columns || []).map((column) => column.name).filter(Boolean)
  const allCards = (cards || []).filter((card) => !!card?.id)
  const themes = [
    'Ship a usable MVP demo flow (Inbox -> Triage -> Proposal -> Execute).',
    'Improve empty states so first-time users understand what to do.',
    'Add a basic CLI workflow for power users.',
    'Improve collaboration: access management + mentions + notifications.',
    'Stabilize automation queue and proposal audit trail.',
  ]

  const lines = []
  lines.push(`Working note for board "${board.name}": ${pickOne(themes, random)}`)

  if (allCards.length > 0) {
    const card = pickOne(allCards, random)
    lines.push(`- Follow up on: "${card.title}"`)
  }

  if (columnNames.length > 0) {
    lines.push(`- Add two small tasks to "${pickOne(columnNames, random)}" to move this forward`)
  }

  lines.push('- Make one task clearly demoable with an obvious next click')

  return lines.join('\n').slice(0, 900)
}

async function createChatBrain({ api, boardId, loop }) {
  const session = await api.post('/llm/chat/sessions', {
    body: {
      title: `Autopilot ${randomUUID().slice(0, 8)}`,
      boardId,
    },
  })

  return async ({ snapshotText }) => {
    const allowInstruction = loop !== 'capture'
    const allowCapture = loop !== 'queue'

    const formats = []
    if (allowInstruction) {
      formats.push(
        'INSTRUCTION: <one valid instruction using one of these patterns>\n' +
          '- create card "title" in column "name" with description "value"\n' +
          '- move card {id} to column "name"\n' +
          '- update card {id} title "value"\n' +
          '- update card {id} description "value"\n' +
          '- rename board to "name"\n' +
          '- update board description "value"',
      )
    }

    if (allowCapture) {
      formats.push(
        'CAPTURE: <a short natural-language note for Inbox triage>' +
          '\n  Optional continuation lines are allowed (up to 6 total lines).',
      )
    }

    const prompt =
      'You are a simulated Taskdeck user. Pick ONE next action to advance work.\n' +
      'Return exactly one decision block using ONE of these formats:\n' +
      '- INSTRUCTION must be a single line.\n' +
      '- CAPTURE must start with "CAPTURE:" and may use up to 6 total lines.\n' +
      formats.map((format) => `- ${format}`).join('\n') +
      '\n\nDo not include explanations, markdown, bullet points, or extra text.\n\n' +
      'Board snapshot:\n' +
      snapshotText

    const message = await api.post(`/llm/chat/sessions/${session.id}/messages`, {
      body: {
        content: prompt,
        requestProposal: false,
      },
    })

    const raw = (message?.assistant?.content || '').trim()
    return raw
  }
}

async function resolveBoard({ api, args }) {
  if (args.boardId) {
    return await api.get(`/boards/${args.boardId}`)
  }

  const boards = await api.get('/boards')
  const existing = (boards || []).find((board) => board.name === args.boardName)
  if (existing) return existing

  if (!args.createBoard) {
    throw new Error(`Board not found: ${args.boardName} (and --no-create-board was set)`)
  }

  const created = await api.post('/boards', {
    body: {
      name: args.boardName,
      description: 'Autopilot playground board (seeded automatically).',
    },
  })

  await applyStarterPack(api, {
    boardId: created.id,
    starterPackId: 'board-blueprint-engineering-sprint',
    dryRun: false,
  })

  return created
}

async function performQueueTurn({ api, boardId, instruction }) {
  await enqueueAndApplyInstruction(api, {
    boardId,
    instruction,
    timeoutMs: 90_000,
  })
}

async function performCaptureTurn({
  api,
  boardId,
  text,
  config,
  captureSource,
  captureTitleHint,
  triageTimeoutMs,
  leaveUntriaged,
}) {
  const capture = await createCaptureItem(api, {
    boardId,
    text,
    source: captureSource,
    titleHint: captureTitleHint,
    externalRef: `autopilot:${Date.now()}`,
  })

  console.log(`Capture created: ${capture.id}`)
  console.log(`Inbox link: ${config.uiBaseUrl}/workspace/inbox#capture-${encodeURIComponent(capture.id)}`)

  if (leaveUntriaged) {
    console.log('leaving capture item un-triaged')
    return
  }

  await triageCaptureItem(api, capture.id)

  const outcome = await waitForCaptureOutcome(api, capture.id, {
    timeoutMs: triageTimeoutMs,
    intervalMs: 1200,
  })

  if (outcome.outcome !== 'proposal') {
    const status = outcome?.item?.status
    console.log(`triage outcome: ${outcome.outcome} (status=${String(status)})`)
    return
  }

  const proposalId = outcome.item.provenance.proposalId
  await approveAndExecuteProposal(api, proposalId)
  console.log(`triaged and applied proposal ${proposalId}`)
}

async function main() {
  const args = parseArgs(process.argv)
  const config = getDemoConfig()
  const random = createRandom(args.seed)
  const deterministic = args.seed !== null && args.seed !== undefined

  const api = new TaskdeckApiClient({ apiBaseUrl: config.apiBaseUrl })
  const login = await ensureUser(api, config.demoUser)
  const authed = api.withToken(login.token)

  const board = await resolveBoard({ api: authed, args })
  let decideChat = null
  if (args.brain === 'taskdeck-chat') {
    try {
      decideChat = await createChatBrain({ api: authed, boardId: board.id, loop: args.loop })
    } catch (err) {
      console.log(`[chat-init-fail] ${String(err?.message || err)}; falling back to heuristic decisions`)
    }
  }
  const activeBrainLabel =
    args.brain === 'taskdeck-chat' && !decideChat ? 'heuristic (taskdeck-chat init failed)' : args.brain

  console.log(`Autopilot running on board: ${board.name} (${board.id})`)
  console.log(`Loop: ${args.loop} | Brain: ${activeBrainLabel}${deterministic ? ` | Seed: ${String(args.seed)}` : ''}`)
  console.log(`UI: ${config.uiBaseUrl}/workspace/boards/${board.id}`)
  console.log(`Inbox: ${config.uiBaseUrl}/workspace/inbox`)
  console.log(`Proposals: ${config.uiBaseUrl}/workspace/automations/proposals`)

  await traceEvent({
    type: 'autopilot.start',
    boardId: board.id,
    boardName: board.name,
    args: {
      turns: args.turns,
      intervalMs: args.intervalMs,
      loop: args.loop,
      brain: args.brain,
      captureProb: args.captureProb,
      leaveCaptureUntriagedProb: args.leaveCaptureUntriagedProb,
      triageTimeoutMs: args.triageTimeoutMs,
      captureSource: args.captureSource,
      captureTitleHint: args.captureTitleHint,
      rngSeed: args.seed ?? null,
    },
  })

  for (let turn = 0; turn < args.turns; turn++) {
    const liveBoard = await authed.get(`/boards/${board.id}`)
    const columns = await authed.get(`/boards/${board.id}/columns`)
    const cards = await authed.get(`/boards/${board.id}/cards`)
    const snapshotText = summarizeBoardForAgent({ board: liveBoard, columns, cards })

    let decision = null
    if (decideChat) {
      try {
        const response = await decideChat({ snapshotText })
        decision = normalizeBrainLine(response)
      } catch (err) {
        console.log(`[chat-fail] ${String(err?.message || err)}`)
      }
    }

    if (!decision) {
      const shouldCapture = args.loop === 'capture' ? true : args.loop === 'queue' ? false : random() < args.captureProb

      if (shouldCapture) {
        decision = {
          kind: 'capture',
          text: await decideHeuristicCapture({ board: liveBoard, columns, cards, random }),
        }
      } else {
        decision = {
          kind: 'instruction',
          instruction: await decideHeuristicInstruction({ columns, cards, turn, random, deterministic }),
        }
      }
    }

    if (args.loop === 'queue' && decision.kind !== 'instruction') {
      decision = {
        kind: 'instruction',
        instruction: await decideHeuristicInstruction({ columns, cards, turn, random, deterministic }),
      }
    }

    if (args.loop === 'capture' && decision.kind !== 'capture') {
      decision = {
        kind: 'capture',
        text: await decideHeuristicCapture({ board: liveBoard, columns, cards, random }),
      }
    }

    await traceEvent({
      type: 'autopilot.turn.start',
      boardId: board.id,
      turn: turn + 1,
      decision,
    })

    console.log(`\n[Turn ${turn + 1}/${args.turns}]`)
    if (decision.kind === 'instruction') {
      console.log(`Instruction: ${decision.instruction}`)
    } else {
      console.log(`Capture:\n${decision.text}`)
    }

    try {
      if (decision.kind === 'instruction') {
        await performQueueTurn({
          api: authed,
          boardId: board.id,
          instruction: decision.instruction,
        })
        console.log('queue instruction applied')
        await traceEvent({
          type: 'autopilot.turn.ok',
          boardId: board.id,
          turn: turn + 1,
          kind: 'instruction',
        })
      } else {
        const leaveUntriaged =
          args.leaveCaptureUntriagedProb > 0 && random() < args.leaveCaptureUntriagedProb

        await performCaptureTurn({
          api: authed,
          boardId: board.id,
          text: decision.text,
          config,
          captureSource: args.captureSource,
          captureTitleHint: args.captureTitleHint,
          triageTimeoutMs: args.triageTimeoutMs,
          leaveUntriaged,
        })
        await traceEvent({
          type: 'autopilot.turn.ok',
          boardId: board.id,
          turn: turn + 1,
          kind: 'capture',
          leaveUntriaged: !!leaveUntriaged,
        })
      }
    } catch (err) {
      console.log(`turn failed: ${String(err?.message || err)}`)
      await traceEvent({
        type: 'autopilot.turn.error',
        boardId: board.id,
        turn: turn + 1,
        error: String(err?.message || err),
      })
    }

    await sleep(args.intervalMs)
  }

  console.log('\nAutopilot finished.')
  await traceEvent({
    type: 'autopilot.end',
    boardId: board.id,
    boardName: board.name,
  })
}

main().catch((err) => {
  console.error(String(err?.stack || err))
  process.exit(1)
})
