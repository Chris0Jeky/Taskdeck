/**
 * Scenario JSON schema and validation for the scenario authoring aid.
 * Internal tooling — makes building scenario JSON safer than hand-editing.
 */

export type ScenarioStepType =
  | 'navigate'
  | 'click'
  | 'fill'
  | 'wait'
  | 'assert'
  | 'api-seed'
  | 'store-dispatch'

export interface ScenarioStep {
  /** Unique step identifier within the scenario. */
  id: string
  /** The type of action this step performs. */
  type: ScenarioStepType
  /** Human-readable description of what this step does. */
  description: string
  /** Step-specific parameters. */
  params: ScenarioStepParams
  /** Optional delay before executing this step (milliseconds). */
  delayMs?: number
}

export interface NavigateParams {
  path: string
}

export interface ClickParams {
  selector: string
}

export interface FillParams {
  selector: string
  value: string
}

export interface WaitParams {
  durationMs: number
}

export interface AssertParams {
  selector: string
  expectation: 'visible' | 'hidden' | 'text-contains' | 'text-equals'
  value?: string
}

export interface ApiSeedParams {
  method: 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH'
  endpoint: string
  body?: unknown
}

export interface StoreDispatchParams {
  store: string
  action: string
  args?: unknown[]
}

export type ScenarioStepParams =
  | NavigateParams
  | ClickParams
  | FillParams
  | WaitParams
  | AssertParams
  | ApiSeedParams
  | StoreDispatchParams

export interface Scenario {
  /** Unique scenario identifier. */
  id: string
  /** Human-readable name. */
  name: string
  /** Description of what this scenario demonstrates or tests. */
  description: string
  /** Ordered list of steps. */
  steps: ScenarioStep[]
  /** Optional tags for categorization. */
  tags?: string[]
  /** ISO timestamp when the scenario was created. */
  createdAt: string
  /** ISO timestamp when the scenario was last modified. */
  updatedAt: string
}

export interface ValidationError {
  path: string
  message: string
}

const STEP_TYPES: ReadonlySet<string> = new Set<ScenarioStepType>([
  'navigate', 'click', 'fill', 'wait', 'assert', 'api-seed', 'store-dispatch',
])

const ASSERT_EXPECTATIONS: ReadonlySet<string> = new Set([
  'visible', 'hidden', 'text-contains', 'text-equals',
])

const API_METHODS: ReadonlySet<string> = new Set([
  'GET', 'POST', 'PUT', 'DELETE', 'PATCH',
])

/**
 * Validate a scenario object against the expected schema.
 * Returns an array of validation errors (empty = valid).
 */
export function validateScenario(scenario: unknown): ValidationError[] {
  const errors: ValidationError[] = []

  if (scenario == null || typeof scenario !== 'object') {
    errors.push({ path: '', message: 'Scenario must be a non-null object.' })
    return errors
  }

  const s = scenario as Record<string, unknown>

  if (typeof s.id !== 'string' || s.id.trim() === '') {
    errors.push({ path: 'id', message: 'Scenario id must be a non-empty string.' })
  }

  if (typeof s.name !== 'string' || s.name.trim() === '') {
    errors.push({ path: 'name', message: 'Scenario name must be a non-empty string.' })
  }

  if (typeof s.description !== 'string') {
    errors.push({ path: 'description', message: 'Scenario description must be a string.' })
  }

  if (!Array.isArray(s.steps)) {
    errors.push({ path: 'steps', message: 'Scenario steps must be an array.' })
    return errors
  }

  if (s.steps.length === 0) {
    errors.push({ path: 'steps', message: 'Scenario must have at least one step.' })
  }

  const seenIds = new Set<string>()
  for (let i = 0; i < s.steps.length; i++) {
    const stepErrors = validateStep(s.steps[i], i, seenIds)
    errors.push(...stepErrors)
  }

  if (s.tags !== undefined) {
    if (!Array.isArray(s.tags)) {
      errors.push({ path: 'tags', message: 'Scenario tags must be an array of strings.' })
    } else {
      for (let i = 0; i < s.tags.length; i++) {
        if (typeof s.tags[i] !== 'string') {
          errors.push({ path: `tags[${i}]`, message: 'Each tag must be a string.' })
        }
      }
    }
  }

  return errors
}

function validateStep(step: unknown, index: number, seenIds: Set<string>): ValidationError[] {
  const errors: ValidationError[] = []
  const prefix = `steps[${index}]`

  if (step == null || typeof step !== 'object') {
    errors.push({ path: prefix, message: 'Step must be a non-null object.' })
    return errors
  }

  const st = step as Record<string, unknown>

  if (typeof st.id !== 'string' || st.id.trim() === '') {
    errors.push({ path: `${prefix}.id`, message: 'Step id must be a non-empty string.' })
  } else if (seenIds.has(st.id as string)) {
    errors.push({ path: `${prefix}.id`, message: `Duplicate step id: "${st.id}".` })
  } else {
    seenIds.add(st.id as string)
  }

  if (typeof st.type !== 'string' || !STEP_TYPES.has(st.type)) {
    errors.push({
      path: `${prefix}.type`,
      message: `Step type must be one of: ${[...STEP_TYPES].join(', ')}.`,
    })
    return errors
  }

  if (typeof st.description !== 'string') {
    errors.push({ path: `${prefix}.description`, message: 'Step description must be a string.' })
  }

  if (st.delayMs !== undefined && (typeof st.delayMs !== 'number' || st.delayMs < 0)) {
    errors.push({ path: `${prefix}.delayMs`, message: 'Step delayMs must be a non-negative number.' })
  }

  if (st.params == null || typeof st.params !== 'object') {
    errors.push({ path: `${prefix}.params`, message: 'Step params must be a non-null object.' })
    return errors
  }

  const params = st.params as Record<string, unknown>
  const type = st.type as ScenarioStepType

  switch (type) {
    case 'navigate':
      if (typeof params.path !== 'string' || params.path.trim() === '') {
        errors.push({ path: `${prefix}.params.path`, message: 'Navigate path must be a non-empty string.' })
      }
      break
    case 'click':
      if (typeof params.selector !== 'string' || params.selector.trim() === '') {
        errors.push({ path: `${prefix}.params.selector`, message: 'Click selector must be a non-empty string.' })
      }
      break
    case 'fill':
      if (typeof params.selector !== 'string' || params.selector.trim() === '') {
        errors.push({ path: `${prefix}.params.selector`, message: 'Fill selector must be a non-empty string.' })
      }
      if (typeof params.value !== 'string') {
        errors.push({ path: `${prefix}.params.value`, message: 'Fill value must be a string.' })
      }
      break
    case 'wait':
      if (typeof params.durationMs !== 'number' || params.durationMs < 0) {
        errors.push({ path: `${prefix}.params.durationMs`, message: 'Wait durationMs must be a non-negative number.' })
      }
      break
    case 'assert':
      if (typeof params.selector !== 'string' || params.selector.trim() === '') {
        errors.push({ path: `${prefix}.params.selector`, message: 'Assert selector must be a non-empty string.' })
      }
      if (typeof params.expectation !== 'string' || !ASSERT_EXPECTATIONS.has(params.expectation)) {
        errors.push({
          path: `${prefix}.params.expectation`,
          message: `Assert expectation must be one of: ${[...ASSERT_EXPECTATIONS].join(', ')}.`,
        })
      }
      if ((params.expectation === 'text-contains' || params.expectation === 'text-equals') && typeof params.value !== 'string') {
        errors.push({ path: `${prefix}.params.value`, message: 'Assert value is required for text expectations.' })
      }
      break
    case 'api-seed':
      if (typeof params.method !== 'string' || !API_METHODS.has(params.method)) {
        errors.push({
          path: `${prefix}.params.method`,
          message: `API seed method must be one of: ${[...API_METHODS].join(', ')}.`,
        })
      }
      if (typeof params.endpoint !== 'string' || params.endpoint.trim() === '') {
        errors.push({ path: `${prefix}.params.endpoint`, message: 'API seed endpoint must be a non-empty string.' })
      }
      break
    case 'store-dispatch':
      if (typeof params.store !== 'string' || params.store.trim() === '') {
        errors.push({ path: `${prefix}.params.store`, message: 'Store dispatch store name must be a non-empty string.' })
      }
      if (typeof params.action !== 'string' || params.action.trim() === '') {
        errors.push({ path: `${prefix}.params.action`, message: 'Store dispatch action must be a non-empty string.' })
      }
      break
  }

  return errors
}

/**
 * Create a blank scenario with sensible defaults.
 */
export function createBlankScenario(): Scenario {
  const now = new Date().toISOString()
  return {
    id: `scenario-${Date.now()}`,
    name: '',
    description: '',
    steps: [],
    tags: [],
    createdAt: now,
    updatedAt: now,
  }
}

/**
 * Create a blank step with sensible defaults for the given type.
 */
export function createBlankStep(type: ScenarioStepType): ScenarioStep {
  const id = `step-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`
  const base = { id, type, description: '', delayMs: 0 }

  switch (type) {
    case 'navigate':
      return { ...base, params: { path: '/workspace/home' } }
    case 'click':
      return { ...base, params: { selector: '' } }
    case 'fill':
      return { ...base, params: { selector: '', value: '' } }
    case 'wait':
      return { ...base, params: { durationMs: 1000 } }
    case 'assert':
      return { ...base, params: { selector: '', expectation: 'visible' as const } }
    case 'api-seed':
      return { ...base, params: { method: 'POST' as const, endpoint: '' } }
    case 'store-dispatch':
      return { ...base, params: { store: '', action: '' } }
  }
}

/**
 * Try to parse a JSON string as a Scenario. Returns the parsed object and validation errors.
 */
export function parseScenarioJson(json: string): { scenario: Scenario | null; errors: ValidationError[] } {
  let parsed: unknown
  try {
    parsed = JSON.parse(json)
  } catch (err) {
    return {
      scenario: null,
      errors: [{ path: '', message: `Invalid JSON: ${err instanceof Error ? err.message : String(err)}` }],
    }
  }

  const errors = validateScenario(parsed)
  if (errors.length > 0) {
    return { scenario: null, errors }
  }

  return { scenario: parsed as Scenario, errors: [] }
}
