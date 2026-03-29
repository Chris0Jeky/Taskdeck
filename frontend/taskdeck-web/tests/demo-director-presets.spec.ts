import { describe, expect, it } from 'vitest'

import {
  listPresetIds,
  listPresets,
  loadPreset,
  requirePreset,
  mergePresetArgs,
  registerPreset,
} from '../scripts/demo-director-presets.mjs'

describe('demo director presets', () => {
  describe('listPresetIds', () => {
    it('returns a non-empty array of preset IDs', () => {
      const ids = listPresetIds()
      expect(ids.length).toBeGreaterThan(0)
      expect(ids).toContain('happy-path-capture')
      expect(ids).toContain('review-approve-flow')
      expect(ids).toContain('error-recovery-demo')
      expect(ids).toContain('soak-baseline')
    })
  })

  describe('listPresets', () => {
    it('returns preset objects with required fields', () => {
      const presets = listPresets()
      expect(presets.length).toBe(listPresetIds().length)

      for (const preset of presets) {
        expect(preset).toHaveProperty('id')
        expect(preset).toHaveProperty('name')
        expect(preset).toHaveProperty('description')
        expect(preset).toHaveProperty('scenario')
        expect(preset).toHaveProperty('directorArgs')
        expect(preset).toHaveProperty('expectations')
        expect(typeof preset.id).toBe('string')
        expect(typeof preset.scenario).toBe('string')
      }
    })
  })

  describe('loadPreset', () => {
    it('returns the preset for a valid ID', () => {
      const preset = loadPreset('happy-path-capture')
      expect(preset).not.toBeNull()
      expect(preset.id).toBe('happy-path-capture')
      expect(preset.scenario).toBe('client-onboarding')
    })

    it('returns null for unknown preset IDs', () => {
      expect(loadPreset('nonexistent-preset')).toBeNull()
      expect(loadPreset('')).toBeNull()
      expect(loadPreset(null)).toBeNull()
    })

    it('normalizes case on lookup', () => {
      const preset = loadPreset('Happy-Path-Capture')
      // loadPreset lowercases the input, so mixed case finds the lowercase key
      expect(preset).not.toBeNull()
      expect(preset.id).toBe('happy-path-capture')
    })
  })

  describe('requirePreset', () => {
    it('returns the preset for a valid ID', () => {
      const preset = requirePreset('soak-baseline')
      expect(preset.id).toBe('soak-baseline')
    })

    it('throws for unknown preset IDs with available list', () => {
      expect(() => requirePreset('nope')).toThrow('Unknown director preset')
      expect(() => requirePreset('nope')).toThrow('happy-path-capture')
    })
  })

  describe('mergePresetArgs', () => {
    it('returns preset defaults when no overrides are given', () => {
      const preset = loadPreset('happy-path-capture')!
      const args = mergePresetArgs(preset)

      expect(args.scenario).toBe('client-onboarding')
      expect(args.skipLlm).toBe(true)
      expect(args.turns).toBe(0)
    })

    it('applies user overrides on top of preset defaults', () => {
      const preset = loadPreset('happy-path-capture')!
      const args = mergePresetArgs(preset, { turns: 5, intervalMs: 200 })

      expect(args.turns).toBe(5)
      expect(args.intervalMs).toBe(200)
      // Preset defaults still present
      expect(args.skipLlm).toBe(true)
      expect(args.scenario).toBe('client-onboarding')
    })

    it('ignores undefined override values', () => {
      const preset = loadPreset('happy-path-capture')!
      const args = mergePresetArgs(preset, { turns: undefined })

      expect(args.turns).toBe(0) // preset default
    })
  })

  describe('registerPreset', () => {
    it('registers a custom preset that can be loaded', () => {
      registerPreset({
        id: 'test-custom-preset',
        name: 'Custom Test',
        description: 'A test preset',
        scenario: 'client-onboarding',
        directorArgs: { skipLlm: true, turns: 1 },
        expectations: { requiredEvents: ['scenario.start'] },
      })

      const loaded = loadPreset('test-custom-preset')
      expect(loaded).not.toBeNull()
      expect(loaded.name).toBe('Custom Test')
    })

    it('rejects presets without an id', () => {
      expect(() => registerPreset({ name: 'No ID' } as any)).toThrow('Preset must have an id')
    })
  })

  describe('preset expectations are well-formed', () => {
    it('all presets have valid expectation structures', () => {
      for (const preset of listPresets()) {
        const exp = preset.expectations
        expect(exp).toBeDefined()

        if (exp.requiredSequence) {
          expect(Array.isArray(exp.requiredSequence)).toBe(true)
          for (const item of exp.requiredSequence) {
            expect(typeof item).toBe('string')
          }
        }

        if (exp.requiredEvents) {
          expect(Array.isArray(exp.requiredEvents)).toBe(true)
        }

        if (exp.allowedErrorTypes) {
          expect(Array.isArray(exp.allowedErrorTypes)).toBe(true)
        }
      }
    })
  })
})
