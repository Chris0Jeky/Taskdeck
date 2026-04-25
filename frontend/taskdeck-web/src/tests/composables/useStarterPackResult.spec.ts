import { describe, it, expect } from 'vitest'
import { ref } from 'vue'
import {
  normalizeConflictSeverity,
  conflictSeverityBadgeClass,
  conflictSeverityLabel,
  useStarterPackResult,
} from '../../composables/useStarterPackResult'
import type {
  StarterPackApplyConflict,
  StarterPackApplyResult,
} from '../../types/starter-packs'

function makeConflict(overrides: Partial<StarterPackApplyConflict> = {}): StarterPackApplyConflict {
  return {
    code: 'TestConflict',
    path: '$.test',
    message: 'test conflict',
    existingValue: null,
    incomingValue: null,
    severity: 'blocking',
    ...overrides,
  }
}

function makeResult(overrides: Partial<StarterPackApplyResult> = {}): StarterPackApplyResult {
  return {
    boardId: 'b-1',
    packId: 'pack-1',
    dryRun: false,
    applied: true,
    actions: [],
    conflicts: [],
    ...overrides,
  }
}

describe('normalizeConflictSeverity', () => {
  it('returns "blocking" when severity is not a string', () => {
    const conflict = makeConflict({ severity: undefined as unknown as string })
    expect(normalizeConflictSeverity(conflict)).toBe('blocking')
  })

  it('returns "blocking" when severity is a number (not a string)', () => {
    const conflict = makeConflict({ severity: 42 as unknown as string })
    expect(normalizeConflictSeverity(conflict)).toBe('blocking')
  })

  it('returns "warning" when severity is "warning"', () => {
    const conflict = makeConflict({ severity: 'warning' })
    expect(normalizeConflictSeverity(conflict)).toBe('warning')
  })

  it('returns "warning" for case-insensitive "Warning" with whitespace', () => {
    const conflict = makeConflict({ severity: '  Warning  ' })
    expect(normalizeConflictSeverity(conflict)).toBe('warning')
  })

  it('returns "blocking" for "blocking" severity', () => {
    const conflict = makeConflict({ severity: 'blocking' })
    expect(normalizeConflictSeverity(conflict)).toBe('blocking')
  })

  it('returns "blocking" for unknown severity strings', () => {
    const conflict = makeConflict({ severity: 'critical' })
    expect(normalizeConflictSeverity(conflict)).toBe('blocking')
  })
})

describe('conflictSeverityBadgeClass', () => {
  it('returns "sp-tone-warning" for warning conflicts', () => {
    expect(conflictSeverityBadgeClass(makeConflict({ severity: 'warning' }))).toBe('sp-tone-warning')
  })

  it('returns "sp-tone-error" for blocking conflicts', () => {
    expect(conflictSeverityBadgeClass(makeConflict({ severity: 'blocking' }))).toBe('sp-tone-error')
  })
})

describe('conflictSeverityLabel', () => {
  it('returns "Warning" for warning conflicts', () => {
    expect(conflictSeverityLabel(makeConflict({ severity: 'warning' }))).toBe('Warning')
  })

  it('returns "Blocking" for blocking conflicts', () => {
    expect(conflictSeverityLabel(makeConflict({ severity: 'blocking' }))).toBe('Blocking')
  })
})

describe('useStarterPackResult', () => {
  it('hasPreviewResult is false when result is null', () => {
    const result = ref<StarterPackApplyResult | null>(null)
    const helpers = useStarterPackResult(result)
    expect(helpers.hasPreviewResult.value).toBe(false)
  })

  it('hasPreviewResult is true when result is set', () => {
    const result = ref<StarterPackApplyResult | null>(makeResult())
    const helpers = useStarterPackResult(result)
    expect(helpers.hasPreviewResult.value).toBe(true)
  })

  it('resultConflicts returns empty array when result is null', () => {
    const result = ref<StarterPackApplyResult | null>(null)
    const helpers = useStarterPackResult(result)
    expect(helpers.resultConflicts.value).toEqual([])
  })

  it('resultConflicts returns conflicts from result', () => {
    const conflicts = [makeConflict()]
    const result = ref<StarterPackApplyResult | null>(makeResult({ conflicts }))
    const helpers = useStarterPackResult(result)
    expect(helpers.resultConflicts.value).toHaveLength(1)
  })

  describe('blockingConflictCount', () => {
    it('counts only blocking conflicts', () => {
      const conflicts = [
        makeConflict({ severity: 'blocking' }),
        makeConflict({ severity: 'warning' }),
        makeConflict({ severity: 'blocking' }),
      ]
      const result = ref<StarterPackApplyResult | null>(makeResult({ conflicts }))
      const helpers = useStarterPackResult(result)
      expect(helpers.blockingConflictCount.value).toBe(2)
    })

    it('returns 0 when no conflicts', () => {
      const result = ref<StarterPackApplyResult | null>(makeResult({ conflicts: [] }))
      const helpers = useStarterPackResult(result)
      expect(helpers.blockingConflictCount.value).toBe(0)
    })
  })

  describe('hasBlockingConflicts', () => {
    it('returns false when result is null', () => {
      const result = ref<StarterPackApplyResult | null>(null)
      const helpers = useStarterPackResult(result)
      expect(helpers.hasBlockingConflicts.value).toBe(false)
    })

    it('uses hasBlockingConflicts boolean from result when present', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({ hasBlockingConflicts: true, conflicts: [] }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.hasBlockingConflicts.value).toBe(true)
    })

    it('uses hasBlockingConflicts=false from result when present', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({ hasBlockingConflicts: false, conflicts: [makeConflict({ severity: 'blocking' })] }),
      )
      const helpers = useStarterPackResult(result)
      // The boolean field takes precedence
      expect(helpers.hasBlockingConflicts.value).toBe(false)
    })

    it('falls back to counting blocking conflicts when hasBlockingConflicts is undefined', () => {
      const r = makeResult({ conflicts: [makeConflict({ severity: 'blocking' })] })
      delete (r as Record<string, unknown>).hasBlockingConflicts
      const result = ref<StarterPackApplyResult | null>(r)
      const helpers = useStarterPackResult(result)
      expect(helpers.hasBlockingConflicts.value).toBe(true)
    })

    it('falls back to false when hasBlockingConflicts is undefined and no blocking conflicts', () => {
      const r = makeResult({ conflicts: [makeConflict({ severity: 'warning' })] })
      delete (r as Record<string, unknown>).hasBlockingConflicts
      const result = ref<StarterPackApplyResult | null>(r)
      const helpers = useStarterPackResult(result)
      expect(helpers.hasBlockingConflicts.value).toBe(false)
    })
  })

  describe('warningConflictCount', () => {
    it('counts warning conflicts as total minus blocking', () => {
      const conflicts = [
        makeConflict({ severity: 'blocking' }),
        makeConflict({ severity: 'warning' }),
        makeConflict({ severity: 'warning' }),
      ]
      const result = ref<StarterPackApplyResult | null>(makeResult({ conflicts }))
      const helpers = useStarterPackResult(result)
      expect(helpers.warningConflictCount.value).toBe(2)
    })

    it('returns 0 when all conflicts are blocking', () => {
      const conflicts = [makeConflict({ severity: 'blocking' })]
      const result = ref<StarterPackApplyResult | null>(makeResult({ conflicts }))
      const helpers = useStarterPackResult(result)
      expect(helpers.warningConflictCount.value).toBe(0)
    })
  })

  describe('actionSummary', () => {
    it('returns zero counts when result is null', () => {
      const result = ref<StarterPackApplyResult | null>(null)
      const helpers = useStarterPackResult(result)
      expect(helpers.actionSummary.value).toEqual({ create: 0, skip: 0, other: 0 })
    })

    it('counts create, skip, and other operations', () => {
      const actions = [
        { entityType: 'label', operation: 'create', key: 'l1', reason: 'new' },
        { entityType: 'label', operation: 'Create', key: 'l2', reason: 'new' },
        { entityType: 'column', operation: 'skip', key: 'c1', reason: 'exists' },
        { entityType: 'card', operation: ' Skip ', key: 'card1', reason: 'exists' },
        { entityType: 'template', operation: 'update', key: 't1', reason: 'modified' },
      ]
      const result = ref<StarterPackApplyResult | null>(makeResult({ actions }))
      const helpers = useStarterPackResult(result)
      expect(helpers.actionSummary.value).toEqual({ create: 2, skip: 2, other: 1 })
    })

    it('handles empty actions array', () => {
      const result = ref<StarterPackApplyResult | null>(makeResult({ actions: [] }))
      const helpers = useStarterPackResult(result)
      expect(helpers.actionSummary.value).toEqual({ create: 0, skip: 0, other: 0 })
    })
  })

  describe('createActionLabel', () => {
    it('returns "Planned create" when result is null', () => {
      const result = ref<StarterPackApplyResult | null>(null)
      const helpers = useStarterPackResult(result)
      expect(helpers.createActionLabel.value).toBe('Planned create')
    })

    it('returns "Applied" when result.applied is true', () => {
      const result = ref<StarterPackApplyResult | null>(makeResult({ applied: true }))
      const helpers = useStarterPackResult(result)
      expect(helpers.createActionLabel.value).toBe('Applied')
    })

    it('returns "Planned create" when result.applied is false', () => {
      const result = ref<StarterPackApplyResult | null>(makeResult({ applied: false }))
      const helpers = useStarterPackResult(result)
      expect(helpers.createActionLabel.value).toBe('Planned create')
    })
  })

  describe('outcomeSummaryLabel', () => {
    it('returns empty string when result is null', () => {
      const result = ref<StarterPackApplyResult | null>(null)
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryLabel.value).toBe('')
    })

    it('returns "Blocked by conflicts" when hasBlockingConflicts', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({ hasBlockingConflicts: true, conflicts: [makeConflict({ severity: 'blocking' })] }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryLabel.value).toBe('Blocked by conflicts')
    })

    it('returns "Preview with warnings" for dry-run with warnings', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({
          dryRun: true,
          applied: false,
          conflicts: [makeConflict({ severity: 'warning' })],
        }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryLabel.value).toBe('Preview with warnings')
    })

    it('returns "Preview ready" for dry-run without warnings', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({ dryRun: true, applied: false }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryLabel.value).toBe('Preview ready')
    })

    it('returns "No changes applied" when not applied and not dry-run', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({ dryRun: false, applied: false }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryLabel.value).toBe('No changes applied')
    })

    it('returns "Applied with warnings" when applied with skip actions', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({
          dryRun: false,
          applied: true,
          actions: [{ entityType: 'card', operation: 'skip', key: 'k', reason: 'r' }],
        }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryLabel.value).toBe('Applied with warnings')
    })

    it('returns "Applied" when applied without warnings', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({ dryRun: false, applied: true }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryLabel.value).toBe('Applied')
    })

    it('returns "Preview with warnings" for dry-run with skip actions', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({
          dryRun: true,
          applied: false,
          actions: [{ entityType: 'card', operation: 'skip', key: 'k', reason: 'r' }],
        }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryLabel.value).toBe('Preview with warnings')
    })
  })

  describe('outcomeSummaryToneClass', () => {
    it('returns "sp-tone-neutral" when result is null', () => {
      const result = ref<StarterPackApplyResult | null>(null)
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryToneClass.value).toBe('sp-tone-neutral')
    })

    it('returns "sp-tone-error" when has blocking conflicts', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({ hasBlockingConflicts: true, conflicts: [makeConflict({ severity: 'blocking' })] }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryToneClass.value).toBe('sp-tone-error')
    })

    it('returns "sp-tone-warning" when has warning conflicts', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({ conflicts: [makeConflict({ severity: 'warning' })] }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryToneClass.value).toBe('sp-tone-warning')
    })

    it('returns "sp-tone-warning" when has skip actions', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({
          actions: [{ entityType: 'card', operation: 'skip', key: 'k', reason: 'r' }],
        }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryToneClass.value).toBe('sp-tone-warning')
    })

    it('returns "sp-tone-info" for dry-run without warnings', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({ dryRun: true, applied: false }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryToneClass.value).toBe('sp-tone-info')
    })

    it('returns "sp-tone-success" for applied without warnings or dry-run', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({ dryRun: false, applied: true }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.outcomeSummaryToneClass.value).toBe('sp-tone-success')
    })
  })

  describe('shouldShowWarningCallout', () => {
    it('returns false when result is null', () => {
      const result = ref<StarterPackApplyResult | null>(null)
      const helpers = useStarterPackResult(result)
      expect(helpers.shouldShowWarningCallout.value).toBe(false)
    })

    it('returns true when has blocking conflicts', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({ hasBlockingConflicts: true, conflicts: [makeConflict({ severity: 'blocking' })] }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.shouldShowWarningCallout.value).toBe(true)
    })

    it('returns true when has warning conflicts', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({ conflicts: [makeConflict({ severity: 'warning' })] }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.shouldShowWarningCallout.value).toBe(true)
    })

    it('returns true when has skip actions', () => {
      const result = ref<StarterPackApplyResult | null>(
        makeResult({
          actions: [{ entityType: 'card', operation: 'skip', key: 'k', reason: 'r' }],
        }),
      )
      const helpers = useStarterPackResult(result)
      expect(helpers.shouldShowWarningCallout.value).toBe(true)
    })

    it('returns false when no warnings or conflicts', () => {
      const result = ref<StarterPackApplyResult | null>(makeResult())
      const helpers = useStarterPackResult(result)
      expect(helpers.shouldShowWarningCallout.value).toBe(false)
    })
  })
})
