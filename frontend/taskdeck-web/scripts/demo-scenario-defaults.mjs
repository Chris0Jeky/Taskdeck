import fs from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const SCENARIO_DIR = path.join(__dirname, 'scenarios-json')
const LEGACY_SCENARIO_DEFAULT_BOARD_NAMES = {
  'engineering-sprint': 'DEMO: Engineering Sprint',
  'support-triage': 'DEMO: Support Triage',
  'content-calendar': 'DEMO: Content Calendar Scenario',
}
const FALLBACK_SCENARIO_ID = 'engineering-sprint'

function assert(condition, message) {
  if (!condition) throw new Error(message)
}

function normalizeScenarioReference(scenarioIdOrPath) {
  const value = String(scenarioIdOrPath || '').trim()
  assert(value, 'Scenario id/path is required')
  return value.endsWith('.json') ? value : `${value}.json`
}

async function tryLoadScenarioDefinition(scenarioIdOrPath) {
  const requestedPath = normalizeScenarioReference(scenarioIdOrPath)
  const normalizedRequestedPath = path.normalize(requestedPath)
  assert(!path.isAbsolute(normalizedRequestedPath), 'Absolute scenario paths are not allowed')

  const fullPath = path.resolve(SCENARIO_DIR, normalizedRequestedPath)
  const relativeToScenarioDir = path.relative(SCENARIO_DIR, fullPath)
  const escapesScenarioDir =
    !relativeToScenarioDir || relativeToScenarioDir.startsWith('..') || path.isAbsolute(relativeToScenarioDir)
  assert(!escapesScenarioDir, `Scenario path resolves outside scenarios-json: "${scenarioIdOrPath}"`)

  try {
    const raw = await fs.readFile(fullPath, 'utf8')
    return JSON.parse(raw)
  } catch (err) {
    if (err?.code === 'ENOENT') {
      return null
    }
    throw err
  }
}

export function getScenarioDefaultBoardNameFromDefinition(scenario) {
  const steps = Array.isArray(scenario?.steps) ? scenario.steps : []
  const createBoardStep = steps.find(
    (step) => step && typeof step === 'object' && step.type === 'createBoard' && typeof step.name === 'string',
  )
  const boardName = createBoardStep?.name?.trim()
  return boardName ? boardName : null
}

export async function resolveScenarioDefaultBoardName(scenarioIdOrPath) {
  const rawScenarioReference = String(scenarioIdOrPath || '').trim()
  const normalizedScenarioId = rawScenarioReference.toLowerCase()
  const scenarioReference = rawScenarioReference || FALLBACK_SCENARIO_ID
  const scenario = await tryLoadScenarioDefinition(scenarioReference)
  const scenarioBoardName = getScenarioDefaultBoardNameFromDefinition(scenario)
  if (scenarioBoardName) {
    return scenarioBoardName
  }

  if (!rawScenarioReference) {
    return LEGACY_SCENARIO_DEFAULT_BOARD_NAMES[FALLBACK_SCENARIO_ID]
  }

  const legacyBoardName = LEGACY_SCENARIO_DEFAULT_BOARD_NAMES[normalizedScenarioId]
  assert(legacyBoardName, `Unknown scenario "${scenarioIdOrPath}"`)
  return legacyBoardName
}

export async function resolveScenarioSelectedBoardName({ scenarioIdOrPath, explicitBoardName } = {}) {
  const normalizedExplicitBoardName = String(explicitBoardName || '').trim()
  if (normalizedExplicitBoardName) {
    return normalizedExplicitBoardName
  }

  return await resolveScenarioDefaultBoardName(scenarioIdOrPath)
}
