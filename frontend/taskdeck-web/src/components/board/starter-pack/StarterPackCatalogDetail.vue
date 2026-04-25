<script setup lang="ts">
import type {
  StarterPackApplyResult,
  StarterPackCatalogEntry,
} from '../../../types/starter-packs'
import StarterPackResultPanel from './StarterPackResultPanel.vue'

defineProps<{
  selectedPack: StarterPackCatalogEntry | null
  runningPreview: boolean
  applyingPack: boolean
  errorMessage: string | null
  latestResult: StarterPackApplyResult | null
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
    <div v-if="selectedPack" class="space-y-5">
      <div>
        <h3 class="sp-text-primary text-xl font-semibold">{{ selectedPack.title }}</h3>
        <p class="sp-text-secondary mt-1 text-sm">{{ selectedPack.manifest.description || selectedPack.summary }}</p>
        <div class="sp-text-secondary mt-3 grid grid-cols-2 gap-2 text-xs">
          <p><span class="font-semibold">Columns:</span> {{ selectedPack.manifest.columns.length }}</p>
          <p><span class="font-semibold">Labels:</span> {{ selectedPack.manifest.labels.length }}</p>
          <p><span class="font-semibold">Templates:</span> {{ selectedPack.manifest.templates.length }}</p>
          <p><span class="font-semibold">Seed cards:</span> {{ selectedPack.manifest.seedCards.length }}</p>
        </div>
      </div>

      <div>
        <p class="sp-text-primary mb-2 text-sm font-semibold">Preview Highlights</p>
        <ul class="sp-text-secondary list-disc space-y-1 pl-5 text-sm">
          <li v-for="highlight in selectedPack.highlights" :key="highlight">{{ highlight }}</li>
        </ul>
      </div>

      <div class="sp-inset-box rounded-md p-4">
        <p class="sp-text-primary mb-2 text-sm font-semibold">Columns</p>
        <div class="sp-text-secondary space-y-1 text-sm">
          <p v-for="column in selectedPack.manifest.columns" :key="`${selectedPack.id}-${column.name}`">
            {{ column.position }} - {{ column.name }}
            <span v-if="column.wipLimit !== null && column.wipLimit !== undefined" class="sp-muted text-xs">
              (WIP {{ column.wipLimit }})
            </span>
          </p>
        </div>
      </div>

      <div class="flex flex-wrap gap-2">
        <button
          type="button"
          :disabled="runningPreview || applyingPack"
          class="sp-btn-secondary rounded-md px-4 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-60"
          @click="$emit('preview')"
        >
          {{ runningPreview ? 'Running preview...' : 'Preview (Dry Run)' }}
        </button>
        <button
          type="button"
          :disabled="runningPreview || applyingPack"
          class="sp-btn-primary rounded-md px-4 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed"
          @click="$emit('apply')"
        >
          {{ applyingPack ? 'Applying...' : 'Apply Starter Pack' }}
        </button>
      </div>

      <div v-if="errorMessage" class="sp-error-box rounded-md p-3 text-sm">
        {{ errorMessage }}
      </div>

      <StarterPackResultPanel
        v-if="hasPreviewResult && latestResult"
        :result="latestResult"
        :create-action-label="createActionLabel"
        :outcome-summary-label="outcomeSummaryLabel"
        :outcome-summary-tone-class="outcomeSummaryToneClass"
        :action-summary="actionSummary"
        :blocking-conflict-count="blockingConflictCount"
        :warning-conflict-count="warningConflictCount"
        :has-blocking-conflicts="hasBlockingConflicts"
        :should-show-warning-callout="shouldShowWarningCallout"
      />
    </div>

    <div v-else class="sp-empty-state rounded-md border border-dashed p-6 text-center">
      <p class="sp-text-secondary text-sm">Select a starter pack to preview and apply.</p>
    </div>
  </section>
</template>

<style scoped>
@import './starter-pack-tokens.css';
</style>
