/**
 * demo-director-presets.mjs
 *
 * Named configurations (presets) for common demo director proof modes.
 * Each preset defines a scenario sequence, timing, and expected outcomes.
 */

/**
 * @typedef {object} DirectorPreset
 * @property {string} id - Unique preset identifier
 * @property {string} name - Human-readable name
 * @property {string} description - What this preset demonstrates
 * @property {string} scenario - Scenario ID to run
 * @property {object} directorArgs - Override args for the demo director
 * @property {object} expectations - Trace expectations for assertion
 */

/** @type {Record<string, DirectorPreset>} */
const PRESETS = {
  'happy-path-capture': {
    id: 'happy-path-capture',
    name: 'Happy Path Capture',
    description:
      'Runs the client-onboarding scenario end-to-end with LLM steps skipped. ' +
      'Validates that capture creation, board setup, and artifact generation complete without errors.',
    scenario: 'client-onboarding',
    directorArgs: {
      skipLlm: true,
      skipSeed: false,
      turns: 0,
      loop: 'mixed',
      brain: 'heuristic',
      intervalMs: 700,
    },
    expectations: {
      requiredSequence: [
        'scenario.start',
        'scenario.step.ok',
      ],
      requiredEvents: ['scenario.start', 'scenario.end'],
      allowedErrorTypes: [],
    },
  },

  'review-approve-flow': {
    id: 'review-approve-flow',
    name: 'Review and Approve Flow',
    description:
      'Runs the client-onboarding scenario with autopilot turns to exercise ' +
      'the full capture-triage-propose-review-apply loop. LLM steps skipped for determinism.',
    scenario: 'client-onboarding',
    directorArgs: {
      skipLlm: true,
      skipSeed: false,
      turns: 6,
      loop: 'queue',
      brain: 'heuristic',
      intervalMs: 500,
    },
    expectations: {
      requiredSequence: ['scenario.start'],
      requiredEvents: ['scenario.start', 'scenario.end'],
      allowedErrorTypes: [],
    },
  },

  'error-recovery-demo': {
    id: 'error-recovery-demo',
    name: 'Error Recovery Demo',
    description:
      'Runs the engineering-sprint scenario with aggressive autopilot timing to ' +
      'surface and recover from transient errors. Allows autopilot turn errors.',
    scenario: 'engineering-sprint',
    directorArgs: {
      skipLlm: true,
      skipSeed: false,
      turns: 8,
      loop: 'mixed',
      brain: 'heuristic',
      intervalMs: 300,
    },
    expectations: {
      requiredSequence: ['scenario.start'],
      requiredEvents: ['scenario.start', 'scenario.end'],
      allowedErrorTypes: ['autopilot.turn.error'],
    },
  },

  'soak-baseline': {
    id: 'soak-baseline',
    name: 'Soak Baseline',
    description:
      'Minimal preset for soak testing. Runs client-onboarding with no autopilot, ' +
      'suitable for repeated loop execution to detect drift.',
    scenario: 'client-onboarding',
    directorArgs: {
      skipLlm: true,
      skipSeed: true,
      turns: 0,
      loop: 'mixed',
      brain: 'heuristic',
      intervalMs: 700,
    },
    expectations: {
      requiredSequence: ['scenario.start'],
      requiredEvents: ['scenario.start', 'scenario.end'],
      allowedErrorTypes: [],
    },
  },
}

/**
 * Returns the list of all available preset IDs.
 * @returns {string[]}
 */
export function listPresetIds() {
  return Object.keys(PRESETS)
}

/**
 * Returns all presets as an array.
 * @returns {DirectorPreset[]}
 */
export function listPresets() {
  return Object.values(PRESETS)
}

/**
 * Loads a preset by its ID.
 * @param {string} presetId
 * @returns {DirectorPreset | null}
 */
export function loadPreset(presetId) {
  const normalized = String(presetId || '').trim().toLowerCase()
  return PRESETS[normalized] || null
}

/**
 * Loads a preset or throws if not found.
 * @param {string} presetId
 * @returns {DirectorPreset}
 */
export function requirePreset(presetId) {
  const preset = loadPreset(presetId)
  if (!preset) {
    const available = listPresetIds().join(', ')
    throw new Error(
      `Unknown director preset: "${presetId}". Available presets: ${available}`,
    )
  }
  return preset
}

/**
 * Merges preset director args with any user overrides.
 * User overrides take precedence.
 *
 * @param {DirectorPreset} preset
 * @param {object} [overrides]
 * @returns {object} Merged director args
 */
export function mergePresetArgs(preset, overrides = {}) {
  return {
    scenario: preset.scenario,
    ...preset.directorArgs,
    ...stripUndefined(overrides),
  }
}

/**
 * Registers a custom preset at runtime (useful for testing or extensions).
 * @param {DirectorPreset} preset
 */
export function registerPreset(preset) {
  if (!preset?.id) {
    throw new Error('Preset must have an id')
  }
  PRESETS[preset.id] = preset
}

function stripUndefined(obj) {
  const result = {}
  for (const [key, value] of Object.entries(obj)) {
    if (value !== undefined) {
      result[key] = value
    }
  }
  return result
}
