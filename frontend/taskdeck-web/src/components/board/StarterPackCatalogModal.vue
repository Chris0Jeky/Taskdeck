<script setup lang="ts">
import { ref, watch } from 'vue'
import { useEscapeToClose } from '../../composables/useEscapeToClose'
import { useStarterPackCatalog } from '../../composables/useStarterPackCatalog'
import { useStarterPackImport } from '../../composables/useStarterPackImport'
import type { StarterPackApplyResult } from '../../types/starter-packs'
import StarterPackCatalogDetail from './starter-pack/StarterPackCatalogDetail.vue'
import StarterPackCatalogList from './starter-pack/StarterPackCatalogList.vue'
import StarterPackImportDetail from './starter-pack/StarterPackImportDetail.vue'
import StarterPackImportInput from './starter-pack/StarterPackImportInput.vue'

type ModalTab = 'catalog' | 'import'

const props = defineProps<{
  boardId: string
  isOpen: boolean
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'applied', result: StarterPackApplyResult): void
}>()

const activeTab = ref<ModalTab>('catalog')

const catalog = useStarterPackCatalog(
  () => props.boardId,
  (result) => emit('applied', result),
)

const importTab = useStarterPackImport(
  () => props.boardId,
  (result) => emit('applied', result),
)

watch(
  () => props.isOpen,
  (isOpen) => {
    if (!isOpen) {
      return
    }

    activeTab.value = 'catalog'
    catalog.reset()
    importTab.clearImportState()
    void catalog.loadCatalog()
  },
  { immediate: true },
)

function handleClose() {
  emit('close')
}

useEscapeToClose(() => props.isOpen, handleClose)
</script>

<template>
  <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- modal backdrop with dialog role and escape key handler; click-to-close is standard modal UX -->
  <div
    v-if="isOpen"
    class="fixed inset-0 z-50 overflow-y-auto"
    role="dialog"
    aria-label="Starter Pack Catalog"
    aria-modal="true"
    @click.self="handleClose"
    @keydown.escape="handleClose"
  >
    <div class="sp-backdrop fixed inset-0 transition-opacity"></div>

    <div class="flex min-h-full items-center justify-center p-4">
      <div class="sp-panel relative max-h-[90vh] w-full max-w-6xl overflow-hidden rounded-lg shadow-xl" @click.stop>
        <div class="sp-header flex items-center justify-between px-6 py-4">
          <div>
            <h2 class="sp-title text-2xl font-semibold">Starter Packs</h2>
            <p class="sp-subtitle text-sm">
              Search templates, preview what will be created, then apply to this board.
            </p>
          </div>
          <button
            type="button"
            class="sp-close-btn transition-colors"
            aria-label="Close starter packs"
            @click="handleClose"
          >
            <svg class="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <div class="sp-tab-bar flex">
          <button
            type="button"
            :class="[
              'sp-tab px-6 py-2.5 text-sm font-medium transition-colors',
              activeTab === 'catalog' ? 'sp-tab--active' : ''
            ]"
            data-testid="tab-catalog"
            @click="activeTab = 'catalog'"
          >
            Catalog
          </button>
          <button
            type="button"
            :class="[
              'sp-tab px-6 py-2.5 text-sm font-medium transition-colors',
              activeTab === 'import' ? 'sp-tab--active' : ''
            ]"
            data-testid="tab-import"
            @click="activeTab = 'import'"
          >
            JSON Import
          </button>
        </div>

        <div v-if="activeTab === 'catalog'" class="grid max-h-[calc(90vh-130px)] grid-cols-1 overflow-y-auto md:grid-cols-2">
          <StarterPackCatalogList
            :filtered-packs="catalog.filteredPacks.value"
            :catalog-entries="catalog.catalogEntries.value"
            :selected-pack-id="catalog.selectedPack.value?.id ?? null"
            :loading-catalog="catalog.loadingCatalog.value"
            :catalog-load-error="catalog.catalogLoadError.value"
            :search-query="catalog.searchQuery.value"
            @update:search-query="catalog.searchQuery.value = $event"
            @select="catalog.selectPack($event)"
          />

          <StarterPackCatalogDetail
            :selected-pack="catalog.selectedPack.value"
            :running-preview="catalog.runningPreview.value"
            :applying-pack="catalog.applyingPack.value"
            :error-message="catalog.errorMessage.value"
            :latest-result="catalog.latestResult.value"
            :has-preview-result="catalog.hasPreviewResult.value"
            :create-action-label="catalog.createActionLabel.value"
            :outcome-summary-label="catalog.outcomeSummaryLabel.value"
            :outcome-summary-tone-class="catalog.outcomeSummaryToneClass.value"
            :action-summary="catalog.actionSummary.value"
            :blocking-conflict-count="catalog.blockingConflictCount.value"
            :warning-conflict-count="catalog.warningConflictCount.value"
            :has-blocking-conflicts="catalog.hasBlockingConflicts.value"
            :should-show-warning-callout="catalog.shouldShowWarningCallout.value"
            @preview="catalog.runPreview()"
            @apply="catalog.applyPack()"
          />
        </div>

        <div v-if="activeTab === 'import'" class="grid max-h-[calc(90vh-130px)] grid-cols-1 overflow-y-auto md:grid-cols-2">
          <StarterPackImportInput
            :import-json-text="importTab.importJsonText.value"
            :import-validating="importTab.importValidating.value"
            :import-validation-errors="importTab.importValidationErrors.value"
            :import-error-message="importTab.importErrorMessage.value"
            @update:import-json-text="importTab.importJsonText.value = $event"
            @validate="importTab.validateImportJson()"
            @file-upload="importTab.handleFileUpload($event)"
            @clear-feedback="importTab.clearImportFeedback()"
          />

          <StarterPackImportDetail
            :import-has-valid-manifest="importTab.importHasValidManifest.value"
            :import-validated-manifest="importTab.importValidatedManifest.value"
            :import-running-preview="importTab.importRunningPreview.value"
            :import-applying="importTab.importApplying.value"
            :import-latest-result="importTab.importLatestResult.value"
            :has-preview-result="importTab.hasPreviewResult.value"
            :create-action-label="importTab.createActionLabel.value"
            :outcome-summary-label="importTab.outcomeSummaryLabel.value"
            :outcome-summary-tone-class="importTab.outcomeSummaryToneClass.value"
            :action-summary="importTab.actionSummary.value"
            :blocking-conflict-count="importTab.blockingConflictCount.value"
            :warning-conflict-count="importTab.warningConflictCount.value"
            :has-blocking-conflicts="importTab.hasBlockingConflicts.value"
            :should-show-warning-callout="importTab.shouldShowWarningCallout.value"
            @preview="importTab.runImportPreview()"
            @apply="importTab.applyImportPack()"
          />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── Backdrop ── */
.sp-backdrop {
  background: rgba(0, 0, 0, 0.5);
}

/* ── Main panel ── */
.sp-panel {
  background: var(--td-surface-primary);
  color: var(--td-text-primary);
}

/* ── Header ── */
.sp-header {
  border-bottom: 1px solid var(--td-border-default);
}

.sp-title {
  color: var(--td-text-primary);
}

.sp-subtitle {
  color: var(--td-text-secondary);
}

.sp-close-btn {
  color: var(--td-text-tertiary);
}

.sp-close-btn:hover {
  color: var(--td-text-secondary);
}

/* ── Tabs ── */
.sp-tab-bar {
  border-bottom: 1px solid var(--td-border-default);
}

.sp-tab {
  color: var(--td-text-muted);
}

.sp-tab:hover {
  color: var(--td-text-secondary);
}

.sp-tab--active {
  color: var(--td-color-primary);
  border-bottom: 2px solid var(--td-color-primary);
}
</style>
