import { computed, ref } from 'vue'
import { starterPacksApi } from '../api/starterPacksApi'
import { useBoardStore } from '../store/boardStore'
import { useToastStore } from '../store/toastStore'
import type {
  ManifestValidationError,
  StarterPackApplyResult,
  StarterPackManifest,
} from '../types/starter-packs'
import { getErrorMessage } from '../utils/errorMessage'
import { extractConflictResult, useStarterPackResult } from './useStarterPackResult'


export function useStarterPackImport(
  boardId: () => string,
  onApplied: (result: StarterPackApplyResult) => void,
) {
  const boardStore = useBoardStore()
  const toast = useToastStore()

  const importJsonText = ref('')
  const importValidating = ref(false)
  const importValidationErrors = ref<ManifestValidationError[]>([])
  const importValidatedManifest = ref<StarterPackManifest | null>(null)
  const importRunningPreview = ref(false)
  const importApplying = ref(false)
  const importErrorMessage = ref<string | null>(null)
  const importLatestResult = ref<StarterPackApplyResult | null>(null)

  const resultHelpers = useStarterPackResult(importLatestResult)

  const importHasValidManifest = computed(() => {
    return importValidatedManifest.value !== null && importValidationErrors.value.length === 0
  })

  function clearImportState() {
    importJsonText.value = ''
    importValidating.value = false
    importValidationErrors.value = []
    importValidatedManifest.value = null
    importRunningPreview.value = false
    importApplying.value = false
    importErrorMessage.value = null
    importLatestResult.value = null
  }

  function clearImportFeedback() {
    importErrorMessage.value = null
    importLatestResult.value = null
    importValidationErrors.value = []
    importValidatedManifest.value = null
  }

  function handleFileUpload(event: Event) {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0]
    if (!file) {
      return
    }

    const reader = new FileReader()
    reader.onload = (readEvent) => {
      const text = readEvent.target?.result
      if (typeof text === 'string') {
        importJsonText.value = text
        clearImportFeedback()
      }
    }
    reader.readAsText(file)

    // Reset input so the same file can be re-selected
    input.value = ''
  }

  async function validateImportJson() {
    const trimmed = importJsonText.value.trim()
    if (!trimmed) {
      importValidationErrors.value = [{ path: '$', message: 'Paste or upload manifest JSON first.' }]
      return
    }

    importValidating.value = true
    clearImportFeedback()

    try {
      const result = await starterPacksApi.validateManifestJson(boardId(), trimmed)
      importValidationErrors.value = result.errors
      importValidatedManifest.value = result.manifest

      if (result.isValid) {
        toast.success('Manifest is valid.')
      } else {
        toast.error(`Manifest has ${result.errors.length} validation error(s).`)
      }
    } catch (error) {
      importErrorMessage.value = getErrorMessage(error, 'Failed to validate manifest.')
      toast.error(importErrorMessage.value)
    } finally {
      importValidating.value = false
    }
  }

  async function runImportPreview() {
    if (!importValidatedManifest.value || importRunningPreview.value || importApplying.value) return

    importRunningPreview.value = true
    importErrorMessage.value = null
    importLatestResult.value = null

    try {
      importLatestResult.value = await starterPacksApi.applyStarterPack(boardId(), {
        manifest: importValidatedManifest.value,
        dryRun: true,
      })

      if (resultHelpers.hasBlockingConflicts.value) {
        toast.error('Dry-run found blocking conflicts.')
      } else if (resultHelpers.warningConflictCount.value > 0 || resultHelpers.actionSummary.value.skip > 0) {
        toast.warning('Dry-run found warnings.')
      } else {
        toast.success('Dry-run preview generated.')
      }
    } catch (error) {
      importErrorMessage.value = getErrorMessage(error, 'Failed to preview imported manifest.')
      toast.error(importErrorMessage.value)
    } finally {
      importRunningPreview.value = false
    }
  }

  async function applyImportPack() {
    if (!importValidatedManifest.value || importRunningPreview.value || importApplying.value) return

    importApplying.value = true
    importErrorMessage.value = null
    importLatestResult.value = null

    try {
      importLatestResult.value = await starterPacksApi.applyStarterPack(boardId(), {
        manifest: importValidatedManifest.value,
        dryRun: false,
      })

      if (resultHelpers.hasBlockingConflicts.value) {
        toast.error('Import apply is blocked by conflicts.')
        return
      }

      if (!importLatestResult.value.applied) {
        toast.warning('Imported manifest did not apply any changes.')
        return
      }

      await boardStore.fetchBoard(boardId())
      onApplied(importLatestResult.value)

      if (resultHelpers.warningConflictCount.value > 0 || resultHelpers.actionSummary.value.skip > 0) {
        toast.warning('Applied imported manifest with warnings.')
      } else {
        toast.success('Applied imported manifest.')
      }
    } catch (error) {
      const conflictResult = extractConflictResult(error)
      if (conflictResult) {
        importLatestResult.value = conflictResult
        toast.error('Import apply is blocked by conflicts.')
        return
      }

      importErrorMessage.value = getErrorMessage(error, 'Failed to apply imported manifest.')
      toast.error(importErrorMessage.value)
    } finally {
      importApplying.value = false
    }
  }

  return {
    importJsonText,
    importValidating,
    importValidationErrors,
    importValidatedManifest,
    importRunningPreview,
    importApplying,
    importErrorMessage,
    importLatestResult,
    importHasValidManifest,
    ...resultHelpers,
    clearImportState,
    clearImportFeedback,
    handleFileUpload,
    validateImportJson,
    runImportPreview,
    applyImportPack,
  }
}
