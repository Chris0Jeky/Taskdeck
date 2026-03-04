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
  getOpsRunLogs,
  ignoreCaptureItem,
  isoDaysFromNow,
  runOpsCommand,
  summarizeBoardForAgent,
  traceEvent,
  triageCaptureItem,
  waitForOpsRun,
  waitForCaptureOutcome,
  waitForCaptureProposalId,
} from './demo-lib.mjs'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const SCENARIO_DIR = path.join(__dirname, 'scenarios-json')
const SUPPORTED_STEP_TYPES = new Set([
  'createBoard',
  'applyStarterPack',
  'createCard',
  'updateCard',
  'moveCard',
  'addComment',
  'queueInstruction',
  'createCapture',
  'ignoreCapture',
  'cancelCapture',
  'triageCapture',
  'waitForCaptureProposal',
  'waitForCaptureOutcome',
  'executeProposal',
  'runOps',
])
const DEFAULT_LLM_STEP_TYPES = new Set([
  'queueInstruction',
  'triageCapture',
  'waitForCaptureProposal',
])
const OPS_RUN_STATUS_BY_CODE = {
  0: 'Queued',
  1: 'Running',
  2: 'Completed',
  3: 'Failed',
  4: 'TimedOut',
  5: 'Cancelled',
}
const OPS_RUN_FAILURE_STATUSES = new Set(['failed', 'timedout', 'cancelled'])

function assert(condition, message) {
  if (!condition) throw new Error(message)
}

function stepRequiresLlm(step) {
  return step?.requiresLlm === true || DEFAULT_LLM_STEP_TYPES.has(step?.type)
}

function isNonEmptyString(value) {
  return typeof value === 'string' && value.trim().length > 0
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function parseOptionalPositiveInteger(rawValue, fieldName, fallbackValue) {
  const value = rawValue === undefined || rawValue === null || rawValue === '' ? fallbackValue : Number(rawValue)
  assert(Number.isFinite(value) && Number.isInteger(value) && value > 0, `${fieldName} must be a positive integer`)
  return value
}

function parseOptionalFiniteNumber(rawValue, fieldName) {
  if (rawValue === undefined || rawValue === null) return null

  let valueToParse = rawValue
  if (typeof rawValue === 'string') {
    const trimmed = rawValue.trim()
    if (trimmed === '') return null
    valueToParse = trimmed
  }

  const value = Number(valueToParse)
  assert(Number.isFinite(value), `${fieldName} must be a finite number`)
  return value
}

function normalizeOpsRunStatus(status) {
  if (typeof status === 'number') {
    return OPS_RUN_STATUS_BY_CODE[status] || String(status)
  }

  if (typeof status === 'string') {
    const trimmed = status.trim()
    if (!trimmed) return 'Unknown'

    const numericStatus = Number(trimmed)
    if (Number.isInteger(numericStatus) && OPS_RUN_STATUS_BY_CODE[numericStatus]) {
      return OPS_RUN_STATUS_BY_CODE[numericStatus]
    }

    return trimmed
  }

  return 'Unknown'
}

function isOpsRunFailureStatus(status) {
  return OPS_RUN_FAILURE_STATUSES.has(normalizeOpsRunStatus(status).toLowerCase())
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

  const requestedPath = value.endsWith('.json') ? value : `${value}.json`
  const normalizedRequestedPath = path.normalize(requestedPath)
  assert(!path.isAbsolute(normalizedRequestedPath), 'Absolute scenario paths are not allowed')

  const fullPath = path.resolve(SCENARIO_DIR, normalizedRequestedPath)
  const relativeToScenarioDir = path.relative(SCENARIO_DIR, fullPath)
  const escapesScenarioDir =
    !relativeToScenarioDir || relativeToScenarioDir.startsWith('..') || path.isAbsolute(relativeToScenarioDir)
  assert(!escapesScenarioDir, `Scenario path resolves outside scenarios-json: "${value}"`)

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
    assert(SUPPORTED_STEP_TYPES.has(step.type), `Step[${index}].type "${step.type}" is not supported`)
    validateScenarioStep(index, step)
  }

  return true
}

function validateScenarioStep(index, step) {
  const stepLabel = `Step[${index}] (${step.type})`

  switch (step.type) {
    case 'createBoard':
      assert(isNonEmptyString(step.name), `${stepLabel}: name is required`)
      break
    case 'applyStarterPack':
      assert(isNonEmptyString(step.board), `${stepLabel}: board is required`)
      assert(isNonEmptyString(step.starterPackId), `${stepLabel}: starterPackId is required`)
      break
    case 'createCard':
      assert(isNonEmptyString(step.board), `${stepLabel}: board is required`)
      assert(isNonEmptyString(step.column), `${stepLabel}: column is required`)
      assert(isNonEmptyString(step.title), `${stepLabel}: title is required`)
      break
    case 'updateCard':
      assert(isNonEmptyString(step.board), `${stepLabel}: board is required`)
      assert(isNonEmptyString(step.card), `${stepLabel}: card is required`)
      assert(isObject(step.patch), `${stepLabel}: patch must be an object`)
      break
    case 'moveCard':
      assert(isNonEmptyString(step.board), `${stepLabel}: board is required`)
      assert(isNonEmptyString(step.card), `${stepLabel}: card is required`)
      assert(isNonEmptyString(step.toColumn), `${stepLabel}: toColumn is required`)
      break
    case 'addComment':
      assert(isNonEmptyString(step.board), `${stepLabel}: board is required`)
      assert(isNonEmptyString(step.card), `${stepLabel}: card is required`)
      assert(isNonEmptyString(step.content), `${stepLabel}: content is required`)
      break
    case 'queueInstruction':
      assert(isNonEmptyString(step.board), `${stepLabel}: board is required`)
      assert(isNonEmptyString(step.instruction), `${stepLabel}: instruction is required`)
      break
    case 'createCapture':
      if (Object.prototype.hasOwnProperty.call(step, 'board')) {
        assert(isNonEmptyString(step.board), `${stepLabel}: board is present but empty`)
      }
      assert(isNonEmptyString(step.text), `${stepLabel}: text is required`)
      break
    case 'ignoreCapture':
    case 'cancelCapture':
    case 'triageCapture':
    case 'waitForCaptureProposal':
    case 'waitForCaptureOutcome':
      assert(isNonEmptyString(step.capture), `${stepLabel}: capture is required`)
      break
    case 'executeProposal':
      assert(isNonEmptyString(step.proposal), `${stepLabel}: proposal is required`)
      break
    case 'runOps':
      assert(isNonEmptyString(step.templateName), `${stepLabel}: templateName is required`)
      if (Object.prototype.hasOwnProperty.call(step, 'parameters')) {
        assert(
          step.parameters === null || isObject(step.parameters),
          `${stepLabel}: parameters must be an object or null`,
        )
      }
      break
    default:
      break
  }
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
      opsRuns: {},
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

    await traceEvent({
      type: 'scenario.step.start',
      scenarioId: scenario.id,
      scenarioTitle: scenario.title,
      stepIndex: index,
      stepLabel: label,
      stepType: step.type,
    })

    if (stepRequiresLlm(step) && opts.skipLlm) {
      ctx.results.steps.push({
        step: label,
        status: 'skipped',
        reason: '--skip-llm',
      })
      await traceEvent({
        type: 'scenario.step.skipped',
        scenarioId: scenario.id,
        stepIndex: index,
        stepLabel: label,
        stepType: step.type,
        reason: '--skip-llm',
      })
      continue
    }

    try {
      const result = await executeStep(api, ctx, step)
      ctx.results.steps.push({ step: label, status: 'ok', result: result || null })
      await traceEvent({
        type: 'scenario.step.ok',
        scenarioId: scenario.id,
        stepIndex: index,
        stepLabel: label,
        stepType: step.type,
        result: result || null,
      })
    } catch (err) {
      ctx.results.steps.push({ step: label, status: 'error', error: String(err?.message || err) })
      await traceEvent({
        type: 'scenario.step.error',
        scenarioId: scenario.id,
        stepIndex: index,
        stepLabel: label,
        stepType: step.type,
        error: String(err?.message || err),
      })
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
    try {
      const boardId = boardsCreated[0].id
      const board = await api.get(`/boards/${boardId}`)
      const columns = await api.get(`/boards/${boardId}/columns`)
      const cards = await api.get(`/boards/${boardId}/cards`)
      snapshot = summarizeBoardForAgent({ board, columns, cards })
    } catch (err) {
      if (!opts.continueOnError) throw err
      ctx.warnings.push(`Snapshot generation failed: ${String(err?.message || err)}`)
    }
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
      const dueInDays = parseOptionalFiniteNumber(step.dueInDays, 'createCard.dueInDays')
      const dueDate = step.dueDate ? step.dueDate : dueInDays != null ? isoDaysFromNow(dueInDays) : null
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
        timeoutMs: parseOptionalPositiveInteger(step.timeoutMs, 'queueInstruction.timeoutMs', 90_000),
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
      const timeoutMs = parseOptionalPositiveInteger(step.timeoutMs, 'waitForCaptureProposal.timeoutMs', 90_000)
      const intervalMs = parseOptionalPositiveInteger(step.intervalMs, 'waitForCaptureProposal.intervalMs', 1200)

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
        timeoutMs: parseOptionalPositiveInteger(step.timeoutMs, 'waitForCaptureOutcome.timeoutMs', 90_000),
        intervalMs: parseOptionalPositiveInteger(step.intervalMs, 'waitForCaptureOutcome.intervalMs', 1200),
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

    case 'runOps': {
      assert(isNonEmptyString(step.templateName), 'runOps.templateName is required')
      const run = await runOpsCommand(api, {
        templateName: step.templateName,
        parameters: step.parameters || null,
      })

      const alias = isNonEmptyString(step.alias) ? step.alias.trim() : null
      if (alias) ctx.refs.opsRuns[alias] = run

      if (step.wait === false) {
        return { runId: run?.id || null, status: run?.status ?? null }
      }

      const runId = run?.id
      assert(isNonEmptyString(runId), 'runOps returned an invalid run id')
      const done = await waitForOpsRun(api, runId, {
        timeoutMs: parseOptionalPositiveInteger(step.timeoutMs, 'runOps.timeoutMs', 60_000),
        intervalMs: parseOptionalPositiveInteger(step.intervalMs, 'runOps.intervalMs', 700),
      })
      if (alias) ctx.refs.opsRuns[alias] = done

      const finalStatus = normalizeOpsRunStatus(done?.status)
      if (isOpsRunFailureStatus(finalStatus)) {
        const detail = isNonEmptyString(done?.errorMessage) ? `: ${done.errorMessage}` : ''
        throw new Error(`Ops run ${runId} finished with non-success status ${finalStatus}${detail}`)
      }

      const logs = step.includeLogs ? await getOpsRunLogs(api, runId) : null
      return {
        runId,
        status: finalStatus,
        exitCode: done?.exitCode ?? null,
        logsCount: logs ? logs.length || 0 : null,
      }
    }

    default:
      throw new Error(`Unknown scenario step type: ${step.type}`)
  }
}
