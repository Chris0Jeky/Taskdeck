import { computed, ref, watch } from 'vue'
import { starterPacksApi } from '../api/starterPacksApi'
import { useBoardStore } from '../store/boardStore'
import { useToastStore } from '../store/toastStore'
import type {
  StarterPackApplyResult,
  StarterPackCatalogEntry,
} from '../types/starter-packs'
import { getErrorMessage } from '../utils/errorMessage'
import { extractConflictResult, useStarterPackResult } from './useStarterPackResult'

export function useStarterPackCatalog(
  boardId: () => string,
  onApplied: (result: StarterPackApplyResult) => void,
) {
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

  const resultHelpers = useStarterPackResult(latestResult)

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

  watch(filteredPacks, (entries) => {
    if (entries.length === 0) {
      selectedPackId.value = ''
      return
    }

    if (!entries.some((entry) => entry.id === selectedPackId.value)) {
      selectedPackId.value = entries[0]?.id ?? ''
    }
  })

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
      const catalog = await starterPacksApi.getCatalog(boardId())
      catalogEntries.value = catalog
      selectedPackId.value = catalog[0]?.id ?? ''
    } catch (error) {
      catalogLoadError.value = getErrorMessage(error, 'Failed to load starter pack catalog.')
      toast.error(catalogLoadError.value)
    } finally {
      loadingCatalog.value = false
    }
  }

  function selectPack(packId: string) {
    selectedPackId.value = packId
    clearFeedback()
  }


  async function finalizeSuccessfulApply() {
    if (!latestResult.value || !selectedPack.value) {
      return
    }

    if (!latestResult.value.applied) {
      toast.warning(`Starter pack "${selectedPack.value.title}" did not apply any changes.`)
      return
    }

    await boardStore.fetchBoard(boardId())
    onApplied(latestResult.value)

    if (resultHelpers.warningConflictCount.value > 0 || resultHelpers.actionSummary.value.skip > 0) {
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
      latestResult.value = await starterPacksApi.applyStarterPack(boardId(), {
        manifest: selectedPack.value.manifest,
        dryRun: true,
      })

      if (resultHelpers.hasBlockingConflicts.value) {
        toast.error('Dry-run found blocking starter pack conflicts.')
      } else if (resultHelpers.warningConflictCount.value > 0 || resultHelpers.actionSummary.value.skip > 0) {
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
      latestResult.value = await starterPacksApi.applyStarterPack(boardId(), {
        manifest: selectedPack.value.manifest,
        dryRun: false,
      })

      if (resultHelpers.hasBlockingConflicts.value) {
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

  function reset() {
    searchQuery.value = ''
    selectedPackId.value = ''
    clearFeedback()
  }

  return {
    catalogEntries,
    loadingCatalog,
    catalogLoadError,
    searchQuery,
    selectedPackId,
    selectedPack,
    filteredPacks,
    runningPreview,
    applyingPack,
    errorMessage,
    latestResult,
    ...resultHelpers,
    loadCatalog,
    selectPack,
    runPreview,
    applyPack,
    clearFeedback,
    reset,
  }
}
