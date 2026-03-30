import { describe, expect, it } from 'vitest'
import {
  validateScenario,
  createBlankScenario,
  createBlankStep,
  parseScenarioJson,
  type Scenario,
} from '../../utils/scenarioSchema'

function buildValidScenario(): Scenario {
  return {
    id: 'test-scenario',
    name: 'Test Scenario',
    description: 'A test scenario.',
    steps: [
      {
        id: 'step-1',
        type: 'navigate',
        description: 'Go to home',
        params: { path: '/workspace/home' },
      },
    ],
    tags: ['test'],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  }
}

describe('validateScenario', () => {
  it('returns no errors for a valid scenario', () => {
    const errors = validateScenario(buildValidScenario())
    expect(errors).toHaveLength(0)
  })

  it('rejects null input', () => {
    const errors = validateScenario(null)
    expect(errors.length).toBeGreaterThan(0)
    expect(errors[0].message).toContain('non-null object')
  })

  it('rejects missing id', () => {
    const scenario = buildValidScenario()
    ;(scenario as Record<string, unknown>).id = ''
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path === 'id')).toBe(true)
  })

  it('rejects missing name', () => {
    const scenario = buildValidScenario()
    ;(scenario as Record<string, unknown>).name = ''
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path === 'name')).toBe(true)
  })

  it('rejects non-array steps', () => {
    const scenario = buildValidScenario()
    ;(scenario as Record<string, unknown>).steps = 'not-an-array'
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path === 'steps')).toBe(true)
  })

  it('rejects empty steps array', () => {
    const scenario = buildValidScenario()
    scenario.steps = []
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.message.includes('at least one step'))).toBe(true)
  })

  it('rejects duplicate step ids', () => {
    const scenario = buildValidScenario()
    scenario.steps.push({
      id: 'step-1', // duplicate
      type: 'click',
      description: 'Click',
      params: { selector: '#btn' },
    })
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.message.includes('Duplicate step id'))).toBe(true)
  })

  it('rejects invalid step type', () => {
    const scenario = buildValidScenario()
    ;(scenario.steps[0] as Record<string, unknown>).type = 'invalid-type'
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('type'))).toBe(true)
  })

  it('validates navigate step requires path', () => {
    const scenario = buildValidScenario()
    scenario.steps[0].params = { path: '' }
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('path'))).toBe(true)
  })

  it('validates click step requires selector', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'click', description: 'Click', params: { selector: '' },
    }]
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('selector'))).toBe(true)
  })

  it('validates fill step requires selector and value', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'fill', description: 'Fill', params: { selector: '', value: 'x' },
    }]
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('selector'))).toBe(true)
  })

  it('validates wait step requires non-negative durationMs', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'wait', description: 'Wait', params: { durationMs: -1 },
    }]
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('durationMs'))).toBe(true)
  })

  it('validates assert step with text expectation requires value', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'assert', description: 'Assert',
      params: { selector: '#el', expectation: 'text-contains' },
    }]
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('value'))).toBe(true)
  })

  it('validates api-seed step requires method and endpoint', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'api-seed', description: 'Seed',
      params: { method: 'INVALID', endpoint: '' },
    }]
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('method'))).toBe(true)
    expect(errors.some(e => e.path.includes('endpoint'))).toBe(true)
  })

  it('validates store-dispatch step requires store and action', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'store-dispatch', description: 'Dispatch',
      params: { store: '', action: '' },
    }]
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('store'))).toBe(true)
    expect(errors.some(e => e.path.includes('action'))).toBe(true)
  })

  it('validates tags must be strings', () => {
    const scenario = buildValidScenario()
    ;(scenario as Record<string, unknown>).tags = [42]
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('tags'))).toBe(true)
  })

  it('rejects negative delayMs', () => {
    const scenario = buildValidScenario()
    scenario.steps[0].delayMs = -10
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('delayMs'))).toBe(true)
  })

  it('rejects non-string description', () => {
    const scenario = buildValidScenario()
    ;(scenario as Record<string, unknown>).description = 42
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path === 'description')).toBe(true)
  })

  it('rejects non-array tags', () => {
    const scenario = buildValidScenario()
    ;(scenario as Record<string, unknown>).tags = 'not-an-array'
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path === 'tags')).toBe(true)
  })

  it('rejects step with null params', () => {
    const scenario = buildValidScenario()
    ;(scenario.steps[0] as Record<string, unknown>).params = null
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('params'))).toBe(true)
  })

  it('rejects step that is not an object', () => {
    const scenario = buildValidScenario()
    ;(scenario as Record<string, unknown>).steps = ['not-an-object']
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.message.includes('non-null object'))).toBe(true)
  })

  it('validates assert step with text-equals expectation requires value', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'assert', description: 'Assert',
      params: { selector: '#el', expectation: 'text-equals' },
    }]
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('value'))).toBe(true)
  })

  it('validates assert with visible expectation does not require value', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'assert', description: 'Assert visible',
      params: { selector: '#el', expectation: 'visible' },
    }]
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('value'))).toBe(false)
  })

  it('validates assert with hidden expectation does not require value', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'assert', description: 'Assert hidden',
      params: { selector: '#el', expectation: 'hidden' },
    }]
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('value'))).toBe(false)
  })

  it('validates assert with invalid expectation', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'assert', description: 'Assert',
      params: { selector: '#el', expectation: 'invalid' },
    }]
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('expectation'))).toBe(true)
  })

  it('rejects step with missing description', () => {
    const scenario = buildValidScenario()
    delete (scenario.steps[0] as Record<string, unknown>).description
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('description'))).toBe(true)
  })

  it('accepts valid fill step', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'fill', description: 'Fill input',
      params: { selector: '#input', value: 'hello' },
    }]
    const errors = validateScenario(scenario)
    expect(errors).toHaveLength(0)
  })

  it('rejects fill step with missing value', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'fill', description: 'Fill input',
      params: { selector: '#input' },
    }]
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('value'))).toBe(true)
  })

  it('accepts valid api-seed step', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'api-seed', description: 'Seed data',
      params: { method: 'POST', endpoint: '/api/items' },
    }]
    const errors = validateScenario(scenario)
    expect(errors).toHaveLength(0)
  })

  it('accepts valid store-dispatch step', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'store-dispatch', description: 'Dispatch action',
      params: { store: 'boardStore', action: 'loadBoard' },
    }]
    const errors = validateScenario(scenario)
    expect(errors).toHaveLength(0)
  })

  it('accepts valid wait step', () => {
    const scenario = buildValidScenario()
    scenario.steps = [{
      id: 's1', type: 'wait', description: 'Wait',
      params: { durationMs: 500 },
    }]
    const errors = validateScenario(scenario)
    expect(errors).toHaveLength(0)
  })

  it('rejects delayMs that is not a number', () => {
    const scenario = buildValidScenario()
    ;(scenario.steps[0] as Record<string, unknown>).delayMs = 'not-a-number'
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('delayMs'))).toBe(true)
  })

  it('accepts undefined delayMs', () => {
    const scenario = buildValidScenario()
    delete (scenario.steps[0] as Record<string, unknown>).delayMs
    const errors = validateScenario(scenario)
    expect(errors.some(e => e.path.includes('delayMs'))).toBe(false)
  })

  it('accepts scenario without tags', () => {
    const scenario = buildValidScenario()
    delete (scenario as Record<string, unknown>).tags
    const errors = validateScenario(scenario)
    expect(errors).toHaveLength(0)
  })
})

describe('createBlankScenario', () => {
  it('returns a valid empty scenario skeleton', () => {
    const scenario = createBlankScenario()
    expect(scenario.id).toBeTruthy()
    expect(scenario.steps).toHaveLength(0)
    expect(scenario.createdAt).toBeTruthy()
  })
})

describe('createBlankStep', () => {
  it('creates navigate step with default path', () => {
    const step = createBlankStep('navigate')
    expect(step.type).toBe('navigate')
    expect((step.params as Record<string, unknown>).path).toBe('/workspace/home')
  })

  it('creates wait step with default duration', () => {
    const step = createBlankStep('wait')
    expect(step.type).toBe('wait')
    expect((step.params as Record<string, unknown>).durationMs).toBe(1000)
  })

  it('creates unique ids', () => {
    const step1 = createBlankStep('click')
    const step2 = createBlankStep('click')
    expect(step1.id).not.toBe(step2.id)
  })

  it('creates click step with empty selector', () => {
    const step = createBlankStep('click')
    expect(step.type).toBe('click')
    expect((step.params as Record<string, unknown>).selector).toBe('')
  })

  it('creates fill step with empty selector and value', () => {
    const step = createBlankStep('fill')
    expect(step.type).toBe('fill')
    expect((step.params as Record<string, unknown>).selector).toBe('')
    expect((step.params as Record<string, unknown>).value).toBe('')
  })

  it('creates assert step with default visible expectation', () => {
    const step = createBlankStep('assert')
    expect(step.type).toBe('assert')
    expect((step.params as Record<string, unknown>).expectation).toBe('visible')
  })

  it('creates api-seed step with POST method', () => {
    const step = createBlankStep('api-seed')
    expect(step.type).toBe('api-seed')
    expect((step.params as Record<string, unknown>).method).toBe('POST')
  })

  it('creates store-dispatch step with empty store and action', () => {
    const step = createBlankStep('store-dispatch')
    expect(step.type).toBe('store-dispatch')
    expect((step.params as Record<string, unknown>).store).toBe('')
    expect((step.params as Record<string, unknown>).action).toBe('')
  })
})

describe('parseScenarioJson', () => {
  it('parses valid JSON', () => {
    const scenario = buildValidScenario()
    const result = parseScenarioJson(JSON.stringify(scenario))
    expect(result.scenario).not.toBeNull()
    expect(result.errors).toHaveLength(0)
  })

  it('returns error for invalid JSON', () => {
    const result = parseScenarioJson('{invalid}')
    expect(result.scenario).toBeNull()
    expect(result.errors.length).toBeGreaterThan(0)
    expect(result.errors[0].message).toContain('Invalid JSON')
  })

  it('returns validation errors for valid JSON with bad schema', () => {
    const result = parseScenarioJson('{"id":"","name":""}')
    expect(result.scenario).toBeNull()
    expect(result.errors.length).toBeGreaterThan(0)
  })
})
