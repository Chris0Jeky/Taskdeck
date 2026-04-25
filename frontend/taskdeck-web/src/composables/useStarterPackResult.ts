import { computed, type Ref } from 'vue'
import type {
  StarterPackApplyConflict,
  StarterPackApplyResult,
} from '../types/starter-packs'

export function normalizeConflictSeverity(conflict: StarterPackApplyConflict): 'blocking' | 'warning' {
  if (typeof conflict.severity !== 'string') {
    return 'blocking'
  }

  return conflict.severity.trim().toLowerCase() === 'warning' ? 'warning' : 'blocking'
}

export function conflictSeverityBadgeClass(conflict: StarterPackApplyConflict): string {
  return normalizeConflictSeverity(conflict) === 'warning'
    ? 'sp-tone-warning'
    : 'sp-tone-error'
}

export function conflictSeverityLabel(conflict: StarterPackApplyConflict): string {
  return normalizeConflictSeverity(conflict) === 'warning' ? 'Warning' : 'Blocking'
}

export function useStarterPackResult(result: Ref<StarterPackApplyResult | null>) {
  const hasPreviewResult = computed(() => result.value !== null)
  const resultConflicts = computed(() => result.value?.conflicts ?? [])

  const blockingConflictCount = computed(() => {
    return resultConflicts.value.filter((conflict) => normalizeConflictSeverity(conflict) === 'blocking').length
  })

  const hasBlockingConflicts = computed(() => {
    if (!result.value) {
      return false
    }

    if (typeof result.value.hasBlockingConflicts === 'boolean') {
      return result.value.hasBlockingConflicts
    }

    return blockingConflictCount.value > 0
  })

  const warningConflictCount = computed(() =>
    Math.max(resultConflicts.value.length - blockingConflictCount.value, 0)
  )

  const actionSummary = computed(() => {
    const summary = { create: 0, skip: 0, other: 0 }

    for (const action of result.value?.actions ?? []) {
      const operation = action.operation.trim().toLowerCase()
      if (operation === 'create') {
        summary.create += 1
        continue
      }

      if (operation === 'skip') {
        summary.skip += 1
        continue
      }

      summary.other += 1
    }

    return summary
  })

  const createActionLabel = computed(() => {
    if (!result.value) {
      return 'Planned create'
    }

    return result.value.applied ? 'Applied' : 'Planned create'
  })

  const outcomeSummaryLabel = computed(() => {
    if (!result.value) {
      return ''
    }

    if (hasBlockingConflicts.value) {
      return 'Blocked by conflicts'
    }

    const hasWarnings = warningConflictCount.value > 0 || actionSummary.value.skip > 0
    if (result.value.dryRun) {
      return hasWarnings ? 'Preview with warnings' : 'Preview ready'
    }

    if (!result.value.applied) {
      return 'No changes applied'
    }

    return hasWarnings ? 'Applied with warnings' : 'Applied'
  })

  const outcomeSummaryToneClass = computed(() => {
    if (!result.value) {
      return 'sp-tone-neutral'
    }

    if (hasBlockingConflicts.value) {
      return 'sp-tone-error'
    }

    const hasWarnings = warningConflictCount.value > 0 || actionSummary.value.skip > 0
    if (hasWarnings) {
      return 'sp-tone-warning'
    }

    if (result.value.dryRun) {
      return 'sp-tone-info'
    }

    return 'sp-tone-success'
  })

  const shouldShowWarningCallout = computed(() => {
    if (!result.value) {
      return false
    }

    return hasBlockingConflicts.value || warningConflictCount.value > 0 || actionSummary.value.skip > 0
  })

  return {
    hasPreviewResult,
    resultConflicts,
    blockingConflictCount,
    hasBlockingConflicts,
    warningConflictCount,
    actionSummary,
    createActionLabel,
    outcomeSummaryLabel,
    outcomeSummaryToneClass,
    shouldShowWarningCallout,
  }
}

export function extractConflictResult(error: unknown): StarterPackApplyResult | null {
  if (typeof error !== 'object' || error === null) {
    return null
  }

  const typed = error as {
    response?: {
      status?: number
      data?: unknown
    }
  }

  if (typed.response?.status !== 409) {
    return null
  }

  const payload = typed.response.data
  if (typeof payload !== 'object' || payload === null) {
    return null
  }

  const typedPayload = payload as {
    boardId?: unknown
    packId?: unknown
    dryRun?: unknown
    applied?: unknown
    actions?: unknown
    conflicts?: unknown
  }

  if (
    typeof typedPayload.boardId !== 'string' ||
    typeof typedPayload.packId !== 'string' ||
    typeof typedPayload.dryRun !== 'boolean' ||
    typeof typedPayload.applied !== 'boolean' ||
    !Array.isArray(typedPayload.actions) ||
    !Array.isArray(typedPayload.conflicts)
  ) {
    return null
  }

  return payload as StarterPackApplyResult
}
