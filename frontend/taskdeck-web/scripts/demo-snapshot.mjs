#!/usr/bin/env node
/**
 * demo-snapshot.mjs
 *
 * Collect a presentation-friendly snapshot of current Taskdeck demo state.
 */

import fs from 'node:fs/promises'
import path from 'node:path'

import { TaskdeckApiClient, ensureUser, getDemoConfig, traceEvent } from './demo-lib.mjs'

function parseArgs(argv) {
  const args = {
    out: null,
    demoPrefix: 'DEMO:',
    includeAllBoards: false,
    limit: 100,
  }

  for (let i = 2; i < argv.length; i++) {
    const value = argv[i]
    if (value === '--out') args.out = argv[++i]
    else if (value === '--demo-prefix') args.demoPrefix = argv[++i] || args.demoPrefix
    else if (value === '--include-all-boards') args.includeAllBoards = true
    else if (value === '--limit') args.limit = Number(argv[++i] || args.limit)
  }

  if (!args.out) {
    throw new Error('Missing --out <path>')
  }

  if (!Number.isFinite(args.limit) || !Number.isInteger(args.limit) || args.limit <= 0) {
    throw new Error(`Invalid --limit value: ${String(args.limit)}`)
  }

  return args
}

async function safeGet(api, requestPath) {
  try {
    return await api.get(requestPath)
  } catch (err) {
    return { __error: String(err?.message || err) }
  }
}

function ensureArray(value) {
  return Array.isArray(value) ? value : []
}

function isErrorResult(value) {
  return value && typeof value === 'object' && !Array.isArray(value) && typeof value.__error === 'string'
}

async function readOpsRunIdsFromTrace(tracePath, limit) {
  if (!tracePath) return []

  try {
    const raw = await fs.readFile(tracePath, 'utf8')
    const runIds = []
    const seen = new Set()

    for (const line of raw.split('\n')) {
      const trimmed = line.trim()
      if (!trimmed) continue

      let event = null
      try {
        event = JSON.parse(trimmed)
      } catch {
        continue
      }

      const runId = typeof event?.runId === 'string' ? event.runId.trim() : ''
      if (!runId || seen.has(runId)) continue

      seen.add(runId)
      runIds.push(runId)
      if (runIds.length >= limit) break
    }

    return runIds
  } catch {
    return []
  }
}

async function collectOpsRuns(authed, runIds) {
  const runs = []

  for (const runId of runIds) {
    const run = await safeGet(authed, `/ops/cli/runs/${encodeURIComponent(runId)}`)
    if (isErrorResult(run)) {
      runs.push({ id: runId, run })
      continue
    }

    const logs = await safeGet(authed, `/ops/cli/runs/${encodeURIComponent(runId)}/logs`)
    const logsSummary = Array.isArray(logs)
      ? {
          count: logs.length,
          sample: logs.slice(0, 20),
        }
      : logs

    runs.push({
      id: runId,
      run,
      logs: logsSummary,
    })
  }

  return runs
}

async function main() {
  const args = parseArgs(process.argv)
  const config = getDemoConfig()

  const api = new TaskdeckApiClient({ apiBaseUrl: config.apiBaseUrl })
  const auth = await ensureUser(api, config.demoUser)
  const authed = api.withToken(auth.token)

  const boardsAllResult = await safeGet(authed, '/boards?includeArchived=true')
  const boardsAll = ensureArray(boardsAllResult)
  const boardsError = isErrorResult(boardsAllResult) ? boardsAllResult.__error : null
  const boards = boardsAll.filter((board) => {
    if (args.includeAllBoards) return true
    return String(board?.name || '').startsWith(args.demoPrefix)
  })

  const boardSnapshots = []
  for (const board of boards) {
    const [columns, cards, labels] = await Promise.all([
      safeGet(authed, `/boards/${board.id}/columns`),
      safeGet(authed, `/boards/${board.id}/cards`),
      safeGet(authed, `/boards/${board.id}/labels`),
    ])

    const columnList = ensureArray(columns)
    const cardList = ensureArray(cards)
    const labelList = ensureArray(labels)
    const errors = {
      columns: isErrorResult(columns) ? columns.__error : null,
      cards: isErrorResult(cards) ? cards.__error : null,
      labels: isErrorResult(labels) ? labels.__error : null,
    }

    boardSnapshots.push({
      id: board.id,
      name: board.name,
      description: board.description,
      isArchived: board.isArchived,
      createdAt: board.createdAt,
      updatedAt: board.updatedAt,
      counts: {
        columns: columnList.length,
        cards: cardList.length,
        labels: labelList.length,
      },
      errors,
      columns: columnList.map((column) => ({
        id: column.id,
        name: column.name,
        position: column.position,
        wipLimit: column.wipLimit ?? null,
      })),
      sampleCards: cardList.slice(0, 10).map((card) => ({
        id: card.id,
        title: card.title,
        columnId: card.columnId,
        labels: card.labels || [],
        dueDate: card.dueDate || null,
        isBlocked: !!card.isBlocked,
      })),
    })
  }

  const queryLimited = (count) => `?limit=${Math.max(1, Math.floor(count))}`

  const proposals = await safeGet(authed, `/automation/proposals${queryLimited(args.limit)}`)
  const queueStats = await safeGet(authed, '/llm-queue/stats')
  const userQueue = await safeGet(authed, `/llm-queue/user${queryLimited(Math.min(args.limit, 200))}`)
  const captureItems = await safeGet(authed, `/capture/items${queryLimited(Math.min(args.limit, 200))}`)
  const notifications = await safeGet(authed, `/notifications${queryLimited(Math.min(args.limit, 200))}`)
  const audit = await safeGet(authed, `/audit/users/me${queryLimited(Math.min(args.limit, 100))}`)
  const opsTemplates = await safeGet(authed, '/ops/cli/templates')
  const tracePath = (process.env.TASKDECK_DEMO_TRACE_PATH || '').trim()
  const opsRunIds = await readOpsRunIdsFromTrace(tracePath, Math.min(args.limit, 50))
  const opsRuns = await collectOpsRuns(authed, opsRunIds)

  const snapshot = {
    generatedAt: new Date().toISOString(),
    apiBaseUrl: config.apiBaseUrl,
    demoUser: {
      username: config.demoUser.username,
      email: config.demoUser.email,
    },
    filters: {
      demoPrefix: args.demoPrefix,
      includeAllBoards: args.includeAllBoards,
      limit: args.limit,
    },
    boardDiscovery: {
      totalBoardsVisible: boardsAll.length,
      error: boardsError,
    },
    boards: boardSnapshots,
    automation: {
      proposals,
      queue: {
        stats: queueStats,
        user: userQueue,
      },
    },
    capture: {
      items: captureItems,
    },
    notifications,
    audit,
    ops: {
      runs: {
        source: tracePath ? 'trace-linked' : 'trace-unavailable',
        tracePath: tracePath || null,
        ids: opsRunIds,
        items: opsRuns,
      },
      templates: Array.isArray(opsTemplates)
        ? {
            count: opsTemplates.length,
            sample: opsTemplates.slice(0, 20),
          }
        : opsTemplates,
    },
  }

  await fs.mkdir(path.dirname(args.out), { recursive: true })
  await fs.writeFile(args.out, JSON.stringify(snapshot, null, 2), 'utf8')

  await traceEvent({
    type: 'snapshot.written',
    out: args.out,
    boards: boards.length,
  })

  console.log(`Wrote demo snapshot to ${args.out}`)
}

main().catch((err) => {
  console.error(err)
  process.exit(1)
})
