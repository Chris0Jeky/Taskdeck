/**
 * JSON scenario runner (schema-driven).
 *
 * Goals:
 * - Let demos/tests reference scenario IDs (not executable JS modules).
 * - Keep scenario data declarative and reviewable (easy to diff).
 * - Provide a single engine that can evolve with new step types.
 */

import fs from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

import {
  applyStarterPack,
  approveAndExecuteProposal,
  cancelCaptureItem,
  createCaptureItem,
  enqueueAndApplyInstruction,
  getCaptureItem,
  ignoreCaptureItem,
  summarizeBoardForAgent,
  triageCaptureItem,
  waitForCaptureOutcome,
  waitForCaptureProposalId,
} from './demo-lib.mjs'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const SCENARIO_DIR = path.join(__dirname, 'scenarios-json')

function assert(condition, message) {
  if (!condition) throw new Error(message)
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function deepClone(value) {
  return value === undefined ? undefined : JSON.parse(JSON.stringify(value))
}

function getByPath(obj, pathExpr) {
  const parts = String(pathExpr)
    .split('.')
    .map((p) => p.trim())
    .filter(Boolean)

  let cur = obj
  for (const part of parts) {
    if (cur == null) return undefined
    cur = cur[part]
  }

  return cur
}

function resolveTemplates(value, ctx) {
  if (typeof value === 'string') {
    return value.replace(/\$\{([^}]+)\}/g, (_match, expr) => {
      const resolved = getByPath(ctx.refs, expr.trim())
      return resolved === undefined ? '' : String(resolved)
    })
  }

  if (Array.isArray(value)) {
    return value.map((entry) => resolveTemplates(entry, ctx))
  }

  if (isObject(value)) {
    const out = {}
    for (const [key, entry] of Object.entries(value)) {
      out[key] = resolveTemplates(entry, ctx)
    }
    return out
  }

  return value
}

export async function listJsonScenarioIds() {
  let files = []
  try {
    files = await fs.readdir(SCENARIO_DIR)
  } catch {
    return []
  }

  return files
    .filter((name) => name.endsWith('.json'))
    .filter((name) => !name.startsWith('schema'))
    .map((name) => path.basename(name, '.json'))
    .sort()
}

export async function loadJsonScenario(scenarioIdOrPath) {
  const value = String(scenarioIdOrPath || '').trim()
  assert(value, 'Scenario id/path is required')

  const fullPath = value.endsWith('.json')
    ? path.isAbsolute(value)
      ? value
      : path.join(SCENARIO_DIR, value)
    : path.join(SCENARIO_DIR, `${value}.json`)

  const raw = await fs.readFile(fullPath, 'utf8')
  return JSON.parse(raw)
}

export function validateScenarioJson(scenario) {
  assert(isObject(scenario), 'Scenario must be an object')
  assert(scenario.version === 1, 'Scenario.version must be 1')
  assert(typeof scenario.id === 'string' && scenario.id.length > 0, 'Scenario.id must be a non-empty string')
  assert(typeof scenario.title === 'string' && scenario.title.length > 0, 'Scenario.title must be a non-empty string')
  assert(Array.isArray(scenario.steps), 'Scenario.steps must be an array')

  for (const [index, step] of scenario.steps.entries()) {
    assert(isObject(step), `Step[${index}] must be an object`)
    assert(typeof step.type === 'string' && step.type.length > 0, `Step[${index}].type must be a string`)
  }

  return true
}

async function getBoardColumns(api, ctx, boardId, { force = false } = {}) {
  if (!force && ctx.cache.columnsByBoardId.has(boardId)) {
    return ctx.cache.columnsByBoardId.get(boardId)
  }

  const columns = await api.get(`/boards/${boardId}/columns`)
  const normalized = columns || []
  ctx.cache.columnsByBoardId.set(boardId, normalized)
  return normalized
}

async function getBoardLabels(api, ctx, boardId, { force = false } = {}) {
  if (!force && ctx.cache.labelsByBoardId.has(boardId)) {
    return ctx.cache.labelsByBoardId.get(boardId)
  }

  const labels = await api.get(`/boards/${boardId}/labels`)
  const normalized = labels || []
  ctx.cache.labelsByBoardId.set(boardId, normalized)
  return normalized
}

async function resolveBoardId(_api, ctx, boardRef) {
  const ref = String(boardRef || '').trim()
  assert(ref, 'board reference is required')

  const byAlias = ctx.refs.boards?.[ref]
  if (byAlias?.id) return byAlias.id
  return ref
}

async function resolveCardId(_api, ctx, cardRef) {
  const ref = String(cardRef || '').trim()
  assert(ref, 'card reference is required')

  const byAlias = ctx.refs.cards?.[ref]
  if (byAlias?.id) return byAlias.id
  return ref
}

async function resolveCaptureId(_api, ctx, captureRef) {
  const ref = String(captureRef || '').trim()
  assert(ref, 'capture reference is required')

  const byAlias = ctx.refs.captures?.[ref]
  if (byAlias?.id) return byAlias.id
  return ref
}

async function resolveColumnIdByName(api, ctx, boardId, columnName) {
  const columns = await getBoardColumns(api, ctx, boardId)
  const resolved = (columns || []).find(
    (column) => String(column?.name || '').toLowerCase() === String(columnName || '').toLowerCase(),
  )

  assert(resolved?.id, `Column not found on board ${boardId}: "${columnName}"`)
  return resolved.id
}

async function resolveLabelIdsByNames(api, ctx, boardId, labelNames = []) {
  if (!labelNames || labelNames.length === 0) return []

  const labels = await getBoardLabels(api, ctx, boardId)
  const byName = new Map((labels || []).map((label) => [String(label?.name || '').toLowerCase(), label]))
  const ids = []

  for (const labelName of labelNames) {
    const resolved = byName.get(String(labelName || '').toLowerCase())
    if (resolved?.id) {
      ids.push(resolved.id)
    } else {
      ctx.warnings.push(`Label not found on board ${boardId}: "${labelName}"`)
    }
  }

  return ids
}

function isoDaysFromNow(days) {
  const value = new Date()
  value.setDate(value.getDate() + Number(days || 0))
  return value.toISOString()
}

export async function runJsonScenario({ api, config, scenario, options = {} }) {
  validateScenarioJson(scenario)

  const opts = {
    skipLlm: false,
    continueOnError: false,
    ...options,
  }

  const ctx = {
    api,
    config,
    options: opts,
    warnings: [],
    refs: {
      boards: {},
      cards: {},
      captures: {},
      proposals: {},
      queueRequests: {},
    },
    cache: {
      columnsByBoardId: new Map(),
      labelsByBoardId: new Map(),
    },
    results: {
      steps: [],
    },
  }

  for (const [index, rawStep] of scenario.steps.entries()) {
    const step = resolveTemplates(deepClone(rawStep), ctx)
    const label = step.label || `${index + 1}:${step.type}`

    if (step.requiresLlm && opts.skipLlm) {
      ctx.results.steps.push({
        step: label,
        status: 'skipped',
        reason: 'requiresLlm + --skip-llm',
      })
      continue
    }

    try {
      const result = await executeStep(api, ctx, step)
      ctx.results.steps.push({ step: label, status: 'ok', result: result || null })
    } catch (err) {
      ctx.results.steps.push({ step: label, status: 'error', error: String(err?.message || err) })
      if (!opts.continueOnError) throw err
    }
  }

  const boardsCreated = Object.values(ctx.refs.boards).map((board) => ({ id: board.id, name: board.name }))
  const links = {
    uiBoards: `${config.uiBaseUrl}/workspace/boards`,
    uiInbox: `${config.uiBaseUrl}/workspace/inbox`,
    uiProposals: `${config.uiBaseUrl}/workspace/automations/proposals`,
  }

  if (boardsCreated.length === 1) {
    links.uiBoard = `${config.uiBaseUrl}/workspace/boards/${boardsCreated[0].id}`
  }

  let snapshot = null
  if (boardsCreated.length >= 1) {
    const boardId = boardsCreated[0].id
    const board = await api.get(`/boards/${boardId}`)
    const columns = await api.get(`/boards/${boardId}/columns`)
    const cards = await api.get(`/boards/${boardId}/cards`)
    snapshot = summarizeBoardForAgent({ board, columns, cards })
  }

  return {
    scenario: { id: scenario.id, title: scenario.title },
    boards: boardsCreated,
    links,
    warnings: ctx.warnings,
    results: ctx.results,
    snapshot,
  }
}

async function executeStep(api, ctx, step) {
  switch (step.type) {
    case 'createBoard': {
      assert(typeof step.name === 'string' && step.name.length > 0, 'createBoard.name is required')
      const board = await api.post('/boards', {
        body: {
          name: step.name,
          description: step.description || null,
        },
      })
      if (step.alias) ctx.refs.boards[step.alias] = board
      return { boardId: board.id }
    }

    case 'applyStarterPack': {
      const boardId = await resolveBoardId(api, ctx, step.board)
      assert(
        typeof step.starterPackId === 'string' && step.starterPackId.length > 0,
        'applyStarterPack.starterPackId is required',
      )

      await applyStarterPack(api, {
        boardId,
        starterPackId: step.starterPackId,
        dryRun: !!step.dryRun,
      })

      ctx.cache.columnsByBoardId.delete(boardId)
      ctx.cache.labelsByBoardId.delete(boardId)
      return { boardId, starterPackId: step.starterPackId }
    }

    case 'createCard': {
      assert(typeof step.board === 'string' && step.board.trim().length > 0, 'createCard.board is required')
      assert(typeof step.column === 'string' && step.column.trim().length > 0, 'createCard.column is required')
      assert(typeof step.title === 'string' && step.title.trim().length > 0, 'createCard.title is required')
      const boardRef = step.board.trim()
      const columnName = step.column.trim()
      const boardId = await resolveBoardId(api, ctx, boardRef)
      const columnId = await resolveColumnIdByName(api, ctx, boardId, columnName)
      const labelIds = await resolveLabelIdsByNames(api, ctx, boardId, step.labels || [])
      const dueDate = step.dueDate ? step.dueDate : step.dueInDays != null ? isoDaysFromNow(step.dueInDays) : null
      const title = step.title.trim()

      const card = await api.post(`/boards/${boardId}/cards`, {
        body: {
          columnId,
          title,
          description: step.description || null,
          dueDate,
          labelIds,
        },
      })

      if (step.alias) ctx.refs.cards[step.alias] = card
      return { cardId: card.id }
    }

    case 'updateCard': {
      const boardId = await resolveBoardId(api, ctx, step.board)
      const cardId = await resolveCardId(api, ctx, step.card)
      assert(isObject(step.patch), 'updateCard.patch must be an object')

      const updated = await api.patch(`/boards/${boardId}/cards/${cardId}`, {
        body: step.patch,
      })

      if (step.alias) ctx.refs.cards[step.alias] = updated
      return { cardId }
    }

    case 'moveCard': {
      const boardId = await resolveBoardId(api, ctx, step.board)
      const cardId = await resolveCardId(api, ctx, step.card)
      const columnId = await resolveColumnIdByName(api, ctx, boardId, step.toColumn)

      const moved = await api.post(`/boards/${boardId}/cards/${cardId}/move`, {
        body: { columnId },
      })

      if (step.alias) ctx.refs.cards[step.alias] = moved
      return { cardId, columnId }
    }

    case 'addComment': {
      const boardId = await resolveBoardId(api, ctx, step.board)
      const cardId = await resolveCardId(api, ctx, step.card)
      assert(typeof step.content === 'string' && step.content.length > 0, 'addComment.content is required')

      const comment = await api.post(`/boards/${boardId}/cards/${cardId}/comments`, {
        body: { content: step.content },
      })

      return { commentId: comment.id }
    }

    case 'queueInstruction': {
      const boardId = await resolveBoardId(api, ctx, step.board)
      assert(
        typeof step.instruction === 'string' && step.instruction.length > 0,
        'queueInstruction.instruction is required',
      )

      const { request, proposal } = await enqueueAndApplyInstruction(api, {
        boardId,
        instruction: step.instruction,
        timeoutMs: step.timeoutMs ? Number(step.timeoutMs) : 90_000,
      })

      if (step.requestAlias) ctx.refs.queueRequests[step.requestAlias] = request
      if (step.proposalAlias) ctx.refs.proposals[step.proposalAlias] = proposal
      return { requestId: request.id, proposalId: proposal.id }
    }

    case 'createCapture': {
      let boardId = null
      if (Object.prototype.hasOwnProperty.call(step, 'board')) {
        assert(
          step.board !== null && step.board !== undefined && String(step.board).trim().length > 0,
          'createCapture.board is present but resolved to empty; check your refs',
        )
        boardId = await resolveBoardId(api, ctx, step.board)
        assert(
          boardId !== null && boardId !== undefined && String(boardId).trim().length > 0,
          'createCapture.board resolved to an invalid boardId; check your refs',
        )
      }

      assert(typeof step.text === 'string' && step.text.length > 0, 'createCapture.text is required')

      const captureItem = await createCaptureItem(api, {
        boardId,
        text: step.text,
        source: step.source || 'Typed',
        titleHint: step.titleHint || null,
        externalRef: step.externalRef || null,
      })

      if (step.alias) ctx.refs.captures[step.alias] = captureItem
      return { captureItemId: captureItem.id }
    }

    case 'ignoreCapture': {
      const captureItemId = await resolveCaptureId(api, ctx, step.capture)
      await ignoreCaptureItem(api, captureItemId)
      return { captureItemId }
    }

    case 'cancelCapture': {
      const captureItemId = await resolveCaptureId(api, ctx, step.capture)
      await cancelCaptureItem(api, captureItemId)
      return { captureItemId }
    }

    case 'triageCapture': {
      const captureItemId = await resolveCaptureId(api, ctx, step.capture)
      await triageCaptureItem(api, captureItemId)

      const item = await getCaptureItem(api, captureItemId)
      const captureAlias = String(step.capture || '').trim()
      if (captureAlias) ctx.refs.captures[captureAlias] = item
      if (step.alias) ctx.refs.captures[step.alias] = item

      return {
        captureItemId,
        status: item?.status,
      }
    }

    case 'waitForCaptureProposal': {
      const captureAlias = String(step.capture || '').trim()
      const captureItemId = await resolveCaptureId(api, ctx, captureAlias)
      const timeoutMs = step.timeoutMs ? Number(step.timeoutMs) : 90_000
      const intervalMs = step.intervalMs ? Number(step.intervalMs) : 1200

      const proposalId = await waitForCaptureProposalId(api, captureItemId, { timeoutMs, intervalMs })
      const item = await getCaptureItem(api, captureItemId)
      if (captureAlias) ctx.refs.captures[captureAlias] = item

      ctx.refs.proposals[step.proposalAlias || `${captureAlias}:proposal`] = { id: proposalId }
      return { captureItemId, proposalId }
    }

    case 'waitForCaptureOutcome': {
      const captureAlias = String(step.capture || '').trim()
      const captureItemId = await resolveCaptureId(api, ctx, captureAlias)

      const outcome = await waitForCaptureOutcome(api, captureItemId, {
        timeoutMs: step.timeoutMs ? Number(step.timeoutMs) : 90_000,
        intervalMs: step.intervalMs ? Number(step.intervalMs) : 1200,
      })

      if (captureAlias) ctx.refs.captures[captureAlias] = outcome.item
      if (step.alias) ctx.refs.captures[step.alias] = outcome.item
      return {
        captureItemId,
        outcome: outcome.outcome,
        status: outcome?.item?.status,
      }
    }

    case 'executeProposal': {
      assert(typeof step.proposal === 'string' && step.proposal.length > 0, 'executeProposal.proposal is required')
      const proposal = ctx.refs.proposals?.[step.proposal]
      const proposalId = proposal?.id || step.proposal

      await approveAndExecuteProposal(api, proposalId)
      return { proposalId }
    }

    default:
      throw new Error(`Unknown scenario step type: ${step.type}`)
  }
}
