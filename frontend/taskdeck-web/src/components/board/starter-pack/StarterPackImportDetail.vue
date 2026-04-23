<script setup lang="ts">
import type {
  StarterPackApplyResult,
  StarterPackManifest,
} from '../../../types/starter-packs'
import StarterPackResultPanel from './StarterPackResultPanel.vue'

defineProps<{
  importHasValidManifest: boolean
  importValidatedManifest: StarterPackManifest | null
  importRunningPreview: boolean
  importApplying: boolean
  importLatestResult: StarterPackApplyResult | null
  hasPreviewResult: boolean
  createActionLabel: string
  outcomeSummaryLabel: string
  outcomeSummaryToneClass: string
  actionSummary: { create: number; skip: number; other: number }
  blockingConflictCount: number
  warningConflictCount: number
  hasBlockingConflicts: boolean
  shouldShowWarningCallout: boolean
}>()

defineEmits<{
  (e: 'preview'): void
  (e: 'apply'): void
}>()
</script>

<template>
  <section class="p-6">
    <div v-if="importHasValidManifest && importValidatedManifest" class="space-y-5">
      <div>
        <h3 class="sp-text-primary text-xl font-semibold">{{ importValidatedManifest.displayName }}</h3>
        <p class="sp-text-secondary mt-1 text-sm">{{ importValidatedManifest.description || 'No description provided.' }}</p>
        <div class="sp-text-secondary mt-3 grid grid-cols-2 gap-2 text-xs">
          <p><span class="font-semibold">Pack ID:</span> {{ importValidatedManifest.packId }}</p>
          <p><span class="font-semibold">Schema:</span> {{ importValidatedManifest.schemaVersion }}</p>
          <p><span class="font-semibold">Columns:</span> {{ importValidatedManifest.columns.length }}</p>
          <p><span class="font-semibold">Labels:</span> {{ importValidatedManifest.labels.length }}</p>
          <p><span class="font-semibold">Templates:</span> {{ importValidatedManifest.templates.length }}</p>
          <p><span class="font-semibold">Seed cards:</span> {{ importValidatedManifest.seedCards.length }}</p>
        </div>
      </div>

      <div class="flex flex-wrap gap-2">
        <button
          type="button"
          :disabled="importRunningPreview || importApplying"
          class="sp-btn-secondary rounded-md px-4 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-60"
          data-testid="import-preview-btn"
          @click="$emit('preview')"
        >
          {{ importRunningPreview ? 'Running preview...' : 'Preview (Dry Run)' }}
        </button>
        <button
          type="button"
          :disabled="importRunningPreview || importApplying"
          class="sp-btn-primary rounded-md px-4 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed"
          data-testid="import-apply-btn"
          @click="$emit('apply')"
        >
          {{ importApplying ? 'Applying...' : 'Apply Imported Pack' }}
        </button>
      </div>

      <StarterPackResultPanel
        v-if="hasPreviewResult && importLatestResult"
        :result="importLatestResult"
        :create-action-label="createActionLabel"
        :outcome-summary-label="outcomeSummaryLabel"
        :outcome-summary-tone-class="outcomeSummaryToneClass"
        :action-summary="actionSummary"
        :blocking-conflict-count="blockingConflictCount"
        :warning-conflict-count="warningConflictCount"
        :has-blocking-conflicts="hasBlockingConflicts"
        :should-show-warning-callout="shouldShowWarningCallout"
        data-testid="import-result-panel"
      />
    </div>

    <div v-else class="sp-empty-state rounded-md border border-dashed p-6 text-center">
      <p class="sp-text-secondary text-sm">Paste or upload manifest JSON, then validate to preview and apply.</p>
    </div>
  </section>
</template>

<style scoped>
@import './starter-pack-tokens.css';
</style>
