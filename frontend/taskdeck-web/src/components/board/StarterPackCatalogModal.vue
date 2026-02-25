<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { starterPacksApi } from '../../api/starterPacksApi'
import { useEscapeToClose } from '../../composables/useEscapeToClose'
import { useBoardStore } from '../../store/boardStore'
import { useToastStore } from '../../store/toastStore'
import type {
  StarterPackApplyConflict,
  StarterPackApplyResult,
  StarterPackCatalogEntry,
} from '../../types/starter-packs'
import { getErrorMessage } from '../../utils/errorMessage'

const props = defineProps<{
  boardId: string
  isOpen: boolean
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'applied', result: StarterPackApplyResult): void
}>()

const boardStore = useBoardStore()
const toast = useToastStore()

const catalogEntries = ref<StarterPackCatalogEntry[]>([])
const loadingCatalog = ref(false)
const catalogLoadError = ref<string | null>(null)
const searchQuery = ref('')
const selectedPackId = ref('')
const runningPreview = ref(false)
const applyingPack = ref(false)
const errorMessage = ref<string | null>(null)
const latestResult = ref<StarterPackApplyResult | null>(null)

const filteredPacks = computed(() => {
  const query = searchQuery.value.trim().toLowerCase()
  if (!query) {
    return catalogEntries.value
  }

  return catalogEntries.value.filter((entry) => {
    const haystack = [
      entry.title,
      entry.summary,
      entry.manifest.packId,
      entry.manifest.displayName,
      entry.manifest.description ?? '',
      ...entry.manifest.tags,
      ...entry.highlights,
    ]
      .join(' ')
      .toLowerCase()

    return haystack.includes(query)
  })
})

const selectedPack = computed<StarterPackCatalogEntry | null>(() => {
  if (filteredPacks.value.length === 0) {
    return null
  }

  return (
    filteredPacks.value.find((entry) => entry.id === selectedPackId.value) ??
    filteredPacks.value[0] ??
    null
  )
})

const hasPreviewResult = computed(() => latestResult.value !== null)
const resultConflicts = computed(() => latestResult.value?.conflicts ?? [])

function normalizeConflictSeverity(conflict: StarterPackApplyConflict): 'blocking' | 'warning' {
  if (typeof conflict.severity !== 'string') {
    return 'blocking'
  }

  return conflict.severity.trim().toLowerCase() === 'warning' ? 'warning' : 'blocking'
}

const blockingConflictCount = computed(() => {
  return resultConflicts.value.filter((conflict) => normalizeConflictSeverity(conflict) === 'blocking').length
})

const hasBlockingConflicts = computed(() => {
  if (!latestResult.value) {
    return false
  }

  if (typeof latestResult.value.hasBlockingConflicts === 'boolean') {
    return latestResult.value.hasBlockingConflicts
  }

  return blockingConflictCount.value > 0
})

const warningConflictCount = computed(() =>
  Math.max(resultConflicts.value.length - blockingConflictCount.value, 0)
)

const actionSummary = computed(() => {
  const summary = { create: 0, skip: 0, other: 0 }

  for (const action of latestResult.value?.actions ?? []) {
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
  if (!latestResult.value) {
    return 'Planned create'
  }

  return latestResult.value.applied ? 'Applied' : 'Planned create'
})

const outcomeSummaryLabel = computed(() => {
  if (!latestResult.value) {
    return ''
  }

  if (hasBlockingConflicts.value) {
    return 'Blocked by conflicts'
  }

  const hasWarnings = warningConflictCount.value > 0 || actionSummary.value.skip > 0
  if (latestResult.value.dryRun) {
    return hasWarnings ? 'Preview with warnings' : 'Preview ready'
  }

  if (!latestResult.value.applied) {
    return 'No changes applied'
  }

  return hasWarnings ? 'Applied with warnings' : 'Applied'
})

const outcomeSummaryToneClass = computed(() => {
  if (!latestResult.value) {
    return 'bg-gray-100 text-gray-700'
  }

  if (hasBlockingConflicts.value) {
    return 'bg-red-100 text-red-700'
  }

  const hasWarnings = warningConflictCount.value > 0 || actionSummary.value.skip > 0
  if (hasWarnings) {
    return 'bg-amber-100 text-amber-800'
  }

  if (latestResult.value.dryRun) {
    return 'bg-blue-100 text-blue-700'
  }

  return 'bg-green-100 text-green-700'
})

const shouldShowWarningCallout = computed(() => {
  if (!latestResult.value) {
    return false
  }

  return hasBlockingConflicts.value || warningConflictCount.value > 0 || actionSummary.value.skip > 0
})

function conflictSeverityBadgeClass(conflict: StarterPackApplyConflict): string {
  return normalizeConflictSeverity(conflict) === 'warning'
    ? 'bg-amber-100 text-amber-800'
    : 'bg-red-100 text-red-800'
}

function conflictSeverityLabel(conflict: StarterPackApplyConflict): string {
  return normalizeConflictSeverity(conflict) === 'warning' ? 'Warning' : 'Blocking'
}

watch(filteredPacks, (entries) => {
  if (entries.length === 0) {
    selectedPackId.value = ''
    return
  }

  if (!entries.some((entry) => entry.id === selectedPackId.value)) {
    selectedPackId.value = entries[0]?.id ?? ''
  }
})

watch(
  () => props.isOpen,
  (isOpen) => {
    if (!isOpen) {
      return
    }

    searchQuery.value = ''
    selectedPackId.value = ''
    clearFeedback()
    void loadCatalog()
  },
  { immediate: true }
)

function clearFeedback() {
  errorMessage.value = null
  latestResult.value = null
}

async function loadCatalog() {
  loadingCatalog.value = true
  catalogLoadError.value = null
  catalogEntries.value = []
  selectedPackId.value = ''
  clearFeedback()

  try {
    const catalog = await starterPacksApi.getCatalog(props.boardId)
    catalogEntries.value = catalog
    selectedPackId.value = catalog[0]?.id ?? ''
  } catch (error) {
    catalogLoadError.value = getErrorMessage(error, 'Failed to load starter pack catalog.')
    toast.error(catalogLoadError.value)
  } finally {
    loadingCatalog.value = false
  }
}

function handleClose() {
  emit('close')
}

function selectPack(packId: string) {
  selectedPackId.value = packId
  clearFeedback()
}

function extractConflictResult(error: unknown): StarterPackApplyResult | null {
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

async function finalizeSuccessfulApply() {
  if (!latestResult.value || !selectedPack.value) {
    return
  }

  if (!latestResult.value.applied) {
    toast.warning(`Starter pack "${selectedPack.value.title}" did not apply any changes.`)
    return
  }

  await boardStore.fetchBoard(props.boardId)
  emit('applied', latestResult.value)

  if (warningConflictCount.value > 0 || actionSummary.value.skip > 0) {
    toast.warning(`Applied starter pack "${selectedPack.value.title}" with warnings.`)
    return
  }

  toast.success(`Applied starter pack "${selectedPack.value.title}".`)
}

async function runPreview() {
  if (!selectedPack.value || loadingCatalog.value || runningPreview.value || applyingPack.value) {
    return
  }

  runningPreview.value = true
  clearFeedback()

  try {
    latestResult.value = await starterPacksApi.applyStarterPack(props.boardId, {
      manifest: selectedPack.value.manifest,
      dryRun: true,
    })

    if (hasBlockingConflicts.value) {
      toast.error('Dry-run found blocking starter pack conflicts.')
    } else if (warningConflictCount.value > 0 || actionSummary.value.skip > 0) {
      toast.warning('Dry-run found warnings. Review skipped or unresolved items.')
    } else {
      toast.success('Dry-run preview generated.')
    }
  } catch (error) {
    errorMessage.value = getErrorMessage(error, 'Failed to preview starter pack.')
    toast.error(errorMessage.value)
  } finally {
    runningPreview.value = false
  }
}

async function applyPack() {
  if (!selectedPack.value || loadingCatalog.value || runningPreview.value || applyingPack.value) {
    return
  }

  applyingPack.value = true
  clearFeedback()

  try {
    latestResult.value = await starterPacksApi.applyStarterPack(props.boardId, {
      manifest: selectedPack.value.manifest,
      dryRun: false,
    })

    if (hasBlockingConflicts.value) {
      toast.error('Starter pack apply is blocked by conflicts.')
      return
    }

    await finalizeSuccessfulApply()
  } catch (error) {
    const conflictResult = extractConflictResult(error)
    if (conflictResult) {
      latestResult.value = conflictResult
      toast.error('Starter pack apply is blocked by conflicts.')
      return
    }

    errorMessage.value = getErrorMessage(error, 'Failed to apply starter pack.')
    toast.error(errorMessage.value)
  } finally {
    applyingPack.value = false
  }
}

useEscapeToClose(() => props.isOpen, handleClose)
</script>

<template>
  <div
    v-if="isOpen"
    class="fixed inset-0 z-50 overflow-y-auto"
    @click.self="handleClose"
  >
    <div class="fixed inset-0 bg-black bg-opacity-50 transition-opacity"></div>

    <div class="flex min-h-full items-center justify-center p-4">
      <div class="relative max-h-[90vh] w-full max-w-6xl overflow-hidden rounded-lg bg-white shadow-xl" @click.stop>
        <div class="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <div>
            <h2 class="text-2xl font-semibold text-gray-900">Starter Packs</h2>
            <p class="text-sm text-gray-600">
              Search templates, preview what will be created, then apply to this board.
            </p>
          </div>
          <button
            type="button"
            class="text-gray-400 transition-colors hover:text-gray-600"
            @click="handleClose"
          >
            <svg class="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <div class="grid max-h-[calc(90vh-84px)] grid-cols-1 overflow-y-auto md:grid-cols-2">
          <section class="border-b border-gray-200 p-6 md:border-b-0 md:border-r">
            <label for="starter-pack-search" class="mb-2 block text-sm font-medium text-gray-700">
              Search
            </label>
            <input
              id="starter-pack-search"
              v-model="searchQuery"
              type="text"
              placeholder="Search by name, tag, or purpose"
              :disabled="loadingCatalog || catalogLoadError !== null"
              class="mb-4 w-full rounded-md border border-gray-300 px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
            />

            <div v-if="loadingCatalog" class="rounded-md border border-dashed border-gray-300 bg-gray-50 p-6 text-center">
              <p class="text-sm font-medium text-gray-700">Loading starter packs...</p>
            </div>

            <div v-else-if="catalogLoadError" class="rounded-md border border-red-200 bg-red-50 p-6 text-center">
              <p class="text-sm font-medium text-red-700">{{ catalogLoadError }}</p>
            </div>

            <div v-else-if="catalogEntries.length === 0" class="rounded-md border border-dashed border-gray-300 bg-gray-50 p-6 text-center">
              <p class="text-sm font-medium text-gray-700">No starter packs are currently available.</p>
            </div>

            <div v-else-if="filteredPacks.length === 0" class="rounded-md border border-dashed border-gray-300 bg-gray-50 p-6 text-center">
              <p class="text-sm font-medium text-gray-700">No starter packs match this search.</p>
              <p class="mt-1 text-xs text-gray-500">Try another keyword to view available packs.</p>
            </div>

            <ul v-else class="space-y-3">
              <li v-for="entry in filteredPacks" :key="entry.id">
                <button
                  type="button"
                  :class="[
                    'w-full rounded-lg border px-4 py-3 text-left transition-colors',
                    selectedPack?.id === entry.id
                      ? 'border-blue-500 bg-blue-50'
                      : 'border-gray-200 bg-white hover:border-gray-300 hover:bg-gray-50'
                  ]"
                  @click="selectPack(entry.id)"
                >
                  <div class="flex items-center justify-between gap-3">
                    <p class="text-sm font-semibold text-gray-900">{{ entry.title }}</p>
                    <span class="rounded bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-700">
                      {{ entry.manifest.packId }}
                    </span>
                  </div>
                  <p class="mt-1 text-sm text-gray-600">{{ entry.summary }}</p>
                  <div class="mt-2 flex flex-wrap gap-1">
                    <span
                      v-for="tag in entry.manifest.tags"
                      :key="`${entry.id}-${tag}`"
                      class="rounded bg-gray-100 px-2 py-0.5 text-xs text-gray-600"
                    >
                      #{{ tag }}
                    </span>
                  </div>
                </button>
              </li>
            </ul>
          </section>

          <section class="p-6">
            <div v-if="selectedPack" class="space-y-5">
              <div>
                <h3 class="text-xl font-semibold text-gray-900">{{ selectedPack.title }}</h3>
                <p class="mt-1 text-sm text-gray-600">{{ selectedPack.manifest.description || selectedPack.summary }}</p>
                <div class="mt-3 grid grid-cols-2 gap-2 text-xs text-gray-600">
                  <p><span class="font-semibold">Columns:</span> {{ selectedPack.manifest.columns.length }}</p>
                  <p><span class="font-semibold">Labels:</span> {{ selectedPack.manifest.labels.length }}</p>
                  <p><span class="font-semibold">Templates:</span> {{ selectedPack.manifest.templates.length }}</p>
                  <p><span class="font-semibold">Seed cards:</span> {{ selectedPack.manifest.seedCards.length }}</p>
                </div>
              </div>

              <div>
                <p class="mb-2 text-sm font-semibold text-gray-800">Preview Highlights</p>
                <ul class="list-disc space-y-1 pl-5 text-sm text-gray-700">
                  <li v-for="highlight in selectedPack.highlights" :key="highlight">{{ highlight }}</li>
                </ul>
              </div>

              <div class="rounded-md border border-gray-200 bg-gray-50 p-4">
                <p class="mb-2 text-sm font-semibold text-gray-800">Columns</p>
                <div class="space-y-1 text-sm text-gray-700">
                  <p v-for="column in selectedPack.manifest.columns" :key="`${selectedPack.id}-${column.name}`">
                    {{ column.position }} - {{ column.name }}
                    <span v-if="column.wipLimit !== null && column.wipLimit !== undefined" class="text-xs text-gray-500">
                      (WIP {{ column.wipLimit }})
                    </span>
                  </p>
                </div>
              </div>

              <div class="flex flex-wrap gap-2">
                <button
                  type="button"
                  :disabled="runningPreview || applyingPack"
                  class="rounded-md border border-blue-300 px-4 py-2 text-sm font-medium text-blue-700 transition-colors hover:bg-blue-50 disabled:cursor-not-allowed disabled:opacity-60"
                  @click="runPreview"
                >
                  {{ runningPreview ? 'Running preview...' : 'Preview (Dry Run)' }}
                </button>
                <button
                  type="button"
                  :disabled="runningPreview || applyingPack"
                  class="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-gray-400"
                  @click="applyPack"
                >
                  {{ applyingPack ? 'Applying...' : 'Apply Starter Pack' }}
                </button>
              </div>

              <div v-if="errorMessage" class="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                {{ errorMessage }}
              </div>

              <div v-if="hasPreviewResult && latestResult" class="rounded-md border border-gray-200 bg-white p-4">
                <div class="mb-3 flex flex-wrap items-center justify-between gap-2">
                  <p class="text-sm font-semibold text-gray-900">
                    {{ latestResult.dryRun ? 'Dry-run Result' : 'Apply Result' }}
                  </p>
                  <span :class="['rounded px-2 py-1 text-xs font-medium', outcomeSummaryToneClass]">
                    {{ outcomeSummaryLabel }}
                  </span>
                </div>

                <div class="mb-3 flex flex-wrap gap-2 text-xs text-gray-700">
                  <span class="rounded bg-green-100 px-2 py-1 font-medium text-green-700">
                    {{ createActionLabel }}: {{ actionSummary.create }}
                  </span>
                  <span class="rounded bg-gray-100 px-2 py-1 font-medium text-gray-700">
                    Skipped: {{ actionSummary.skip }}
                  </span>
                  <span class="rounded bg-red-100 px-2 py-1 font-medium text-red-700">
                    Blocked: {{ blockingConflictCount }}
                  </span>
                  <span class="rounded bg-amber-100 px-2 py-1 font-medium text-amber-800">
                    Warnings: {{ warningConflictCount }}
                  </span>
                </div>

                <div
                  v-if="shouldShowWarningCallout"
                  :class="[
                    'mb-3 rounded-md border p-3 text-xs',
                    hasBlockingConflicts
                      ? 'border-red-200 bg-red-50 text-red-800'
                      : 'border-amber-200 bg-amber-50 text-amber-900'
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

                <div v-if="latestResult.actions.length > 0" class="mb-3">
                  <p class="mb-1 text-xs font-semibold uppercase tracking-wide text-gray-600">Actions</p>
                  <ul class="max-h-36 space-y-1 overflow-y-auto rounded border border-gray-100 bg-gray-50 p-2 text-xs text-gray-700">
                    <li v-for="(action, index) in latestResult.actions" :key="`action-${index}-${action.key}`">
                      <span class="font-semibold">{{ action.operation }}</span>
                      {{ action.entityType }} - {{ action.key }}
                      <span class="text-gray-500">({{ action.reason }})</span>
                    </li>
                  </ul>
                </div>

                <div v-if="latestResult.conflicts.length > 0">
                  <p
                    :class="[
                      'mb-1 text-xs font-semibold uppercase tracking-wide',
                      hasBlockingConflicts ? 'text-red-700' : 'text-amber-800'
                    ]"
                  >
                    Conflicts
                  </p>
                  <ul
                    :class="[
                      'max-h-36 space-y-1 overflow-y-auto rounded p-2 text-xs',
                      hasBlockingConflicts
                        ? 'border border-red-100 bg-red-50 text-red-800'
                        : 'border border-amber-100 bg-amber-50 text-amber-900'
                    ]"
                  >
                    <li v-for="(conflict, index) in latestResult.conflicts" :key="`conflict-${index}-${conflict.code}`">
                      <span :class="['mr-2 rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase', conflictSeverityBadgeClass(conflict)]">
                        {{ conflictSeverityLabel(conflict) }}
                      </span>
                      <span class="font-semibold">{{ conflict.code }}</span>
                      at {{ conflict.path }} - {{ conflict.message }}
                    </li>
                  </ul>
                </div>
              </div>
            </div>

            <div v-else class="rounded-md border border-dashed border-gray-300 bg-gray-50 p-6 text-center">
              <p class="text-sm text-gray-700">Select a starter pack to preview and apply.</p>
            </div>
          </section>
        </div>
      </div>
    </div>
  </div>
</template>
