<script setup lang="ts">
import {
  conflictSeverityBadgeClass,
  conflictSeverityLabel,
} from '../../../composables/useStarterPackResult'
import type { StarterPackApplyResult } from '../../../types/starter-packs'

defineProps<{
  result: StarterPackApplyResult
  createActionLabel: string
  outcomeSummaryLabel: string
  outcomeSummaryToneClass: string
  actionSummary: { create: number; skip: number; other: number }
  blockingConflictCount: number
  warningConflictCount: number
  hasBlockingConflicts: boolean
  shouldShowWarningCallout: boolean
}>()
</script>

<template>
  <div class="sp-result-box rounded-md p-4">
    <div class="mb-3 flex flex-wrap items-center justify-between gap-2">
      <p class="sp-text-primary text-sm font-semibold">
        {{ result.dryRun ? 'Dry-run Result' : 'Apply Result' }}
      </p>
      <span :class="['rounded px-2 py-1 text-xs font-medium', outcomeSummaryToneClass]">
        {{ outcomeSummaryLabel }}
      </span>
    </div>

    <div class="sp-text-secondary mb-3 flex flex-wrap gap-2 text-xs">
      <span class="sp-tone-success rounded px-2 py-1 font-medium">
        {{ createActionLabel }}: {{ actionSummary.create }}
      </span>
      <span class="sp-tone-neutral rounded px-2 py-1 font-medium">
        Skipped: {{ actionSummary.skip }}
      </span>
      <span class="sp-tone-error rounded px-2 py-1 font-medium">
        Blocked: {{ blockingConflictCount }}
      </span>
      <span class="sp-tone-warning rounded px-2 py-1 font-medium">
        Warnings: {{ warningConflictCount }}
      </span>
    </div>

    <div
      v-if="shouldShowWarningCallout"
      :class="[
        'mb-3 rounded-md border p-3 text-xs',
        hasBlockingConflicts
          ? 'sp-callout-error'
          : 'sp-callout-warning'
      ]"
    >
      <p class="font-semibold">
        {{
          hasBlockingConflicts
            ? 'Blocking conflicts must be resolved before apply can complete.'
            : 'Warnings detected: starter pack apply can proceed, but some items were skipped.'
        }}
      </p>
    </div>

    <div v-if="result.actions.length > 0" class="mb-3">
      <p class="sp-text-secondary mb-1 text-xs font-semibold uppercase tracking-wide">Actions</p>
      <ul class="sp-list-box max-h-36 space-y-1 overflow-y-auto rounded p-2 text-xs">
        <li v-for="(action, index) in result.actions" :key="`action-${index}-${action.key}`">
          <span class="font-semibold">{{ action.operation }}</span>
          {{ action.entityType }} - {{ action.key }}
          <span class="sp-muted">({{ action.reason }})</span>
        </li>
      </ul>
    </div>

    <div v-if="result.conflicts.length > 0">
      <p
        :class="[
          'mb-1 text-xs font-semibold uppercase tracking-wide',
          hasBlockingConflicts ? 'sp-text-error' : 'sp-text-warning'
        ]"
      >
        Conflicts
      </p>
      <ul
        :class="[
          'max-h-36 space-y-1 overflow-y-auto rounded p-2 text-xs',
          hasBlockingConflicts
            ? 'sp-callout-error'
            : 'sp-callout-warning'
        ]"
      >
        <li v-for="(conflict, index) in result.conflicts" :key="`conflict-${index}-${conflict.code}`">
          <span :class="['mr-2 rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase', conflictSeverityBadgeClass(conflict)]">
            {{ conflictSeverityLabel(conflict) }}
          </span>
          <span class="font-semibold">{{ conflict.code }}</span>
          at {{ conflict.path }} - {{ conflict.message }}
        </li>
      </ul>
    </div>
  </div>
</template>

<style scoped>
@import './starter-pack-tokens.css';
</style>
