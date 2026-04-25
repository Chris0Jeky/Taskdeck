import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useStarterPackImport } from '../../composables/useStarterPackImport'
import type {
  StarterPackApplyResult,
  StarterPackManifest,
} from '../../types/starter-packs'

const mocks = vi.hoisted(() => ({
  applyStarterPack: vi.fn(),
  validateManifestJson: vi.fn(),
  fetchBoard: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
  toastWarning: vi.fn(),
}))

vi.mock('../../api/starterPacksApi', () => ({
  starterPacksApi: {
    getCatalog: vi.fn(),
    applyStarterPack: mocks.applyStarterPack,
    validateManifestJson: mocks.validateManifestJson,
  },
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => ({
    fetchBoard: mocks.fetchBoard,
  }),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.toastSuccess,
    error: mocks.toastError,
    warning: mocks.toastWarning,
    info: vi.fn(),
  }),
}))

function makeManifest(overrides: Partial<StarterPackManifest> = {}): StarterPackManifest {
  return {
    schemaVersion: '1.0',
    packId: 'imported-pack',
    displayName: 'Imported Pack',
    description: 'A test manifest',
    compatibility: { minTaskdeckVersion: '1.0.0', requiredFeatures: ['boards'] },
    tags: ['test'],
    labels: [{ name: 'bug', color: '#FF0000' }],
    columns: [{ name: 'Todo', position: 0 }],
    templates: [],
    seedCards: [],
    ...overrides,
  }
}

function makeResult(overrides: Partial<StarterPackApplyResult> = {}): StarterPackApplyResult {
  return {
    boardId: 'b-1',
    packId: 'imported-pack',
    dryRun: false,
    applied: true,
    actions: [],
    conflicts: [],
    ...overrides,
  }
}

describe('useStarterPackImport', () => {
  let onApplied: ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.clearAllMocks()
    onApplied = vi.fn()
  })

  describe('initial state', () => {
    it('starts with empty import state', () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)
      expect(imp.importJsonText.value).toBe('')
      expect(imp.importValidating.value).toBe(false)
      expect(imp.importValidationErrors.value).toEqual([])
      expect(imp.importValidatedManifest.value).toBeNull()
      expect(imp.importRunningPreview.value).toBe(false)
      expect(imp.importApplying.value).toBe(false)
      expect(imp.importErrorMessage.value).toBeNull()
      expect(imp.importLatestResult.value).toBeNull()
    })
  })

  describe('importHasValidManifest', () => {
    it('returns false when no manifest is validated', () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)
      expect(imp.importHasValidManifest.value).toBe(false)
    })

    it('returns false when manifest exists but validation errors exist', () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()
      imp.importValidationErrors.value = [{ path: '$', message: 'bad' }]
      expect(imp.importHasValidManifest.value).toBe(false)
    })

    it('returns true when manifest exists and no validation errors', () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()
      imp.importValidationErrors.value = []
      expect(imp.importHasValidManifest.value).toBe(true)
    })
  })

  describe('clearImportState', () => {
    it('resets all import state fields', () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importJsonText.value = '{"some":"json"}'
      imp.importValidationErrors.value = [{ path: '$', message: 'err' }]
      imp.importValidatedManifest.value = makeManifest()
      imp.importErrorMessage.value = 'error'
      imp.importLatestResult.value = makeResult()

      imp.clearImportState()

      expect(imp.importJsonText.value).toBe('')
      expect(imp.importValidating.value).toBe(false)
      expect(imp.importValidationErrors.value).toEqual([])
      expect(imp.importValidatedManifest.value).toBeNull()
      expect(imp.importRunningPreview.value).toBe(false)
      expect(imp.importApplying.value).toBe(false)
      expect(imp.importErrorMessage.value).toBeNull()
      expect(imp.importLatestResult.value).toBeNull()
    })
  })

  describe('clearImportFeedback', () => {
    it('clears error, result, validation errors, and manifest', () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importErrorMessage.value = 'error'
      imp.importLatestResult.value = makeResult()
      imp.importValidationErrors.value = [{ path: '$', message: 'err' }]
      imp.importValidatedManifest.value = makeManifest()

      imp.clearImportFeedback()

      expect(imp.importErrorMessage.value).toBeNull()
      expect(imp.importLatestResult.value).toBeNull()
      expect(imp.importValidationErrors.value).toEqual([])
      expect(imp.importValidatedManifest.value).toBeNull()
    })
  })

  describe('handleFileUpload', () => {
    it('reads file content and sets importJsonText', async () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importErrorMessage.value = 'old error'
      imp.importValidationErrors.value = [{ path: '$', message: 'old' }]

      const fileContent = '{"packId":"test"}'
      const file = new File([fileContent], 'manifest.json', { type: 'application/json' })

      // Create a mock input element
      const input = document.createElement('input')
      input.type = 'file'
      Object.defineProperty(input, 'files', { value: [file] })

      const event = { target: input } as unknown as Event

      imp.handleFileUpload(event)

      // FileReader is async, wait for it
      await new Promise((resolve) => setTimeout(resolve, 50))

      expect(imp.importJsonText.value).toBe(fileContent)
      // clearImportFeedback should have been called
      expect(imp.importErrorMessage.value).toBeNull()
      expect(imp.importValidationErrors.value).toEqual([])
    })

    it('does nothing when no file is selected', () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)

      const input = document.createElement('input')
      input.type = 'file'
      // No files
      Object.defineProperty(input, 'files', { value: [] })

      const event = { target: input } as unknown as Event

      imp.handleFileUpload(event)

      expect(imp.importJsonText.value).toBe('')
    })

    it('does nothing when files is undefined/null', () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)

      const input = document.createElement('input')
      input.type = 'file'
      // files is null (no selection)

      const event = { target: input } as unknown as Event

      imp.handleFileUpload(event)

      expect(imp.importJsonText.value).toBe('')
    })
  })

  describe('validateImportJson', () => {
    it('shows client-side error when textarea is empty', async () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)

      await imp.validateImportJson()

      expect(mocks.validateManifestJson).not.toHaveBeenCalled()
      expect(imp.importValidationErrors.value).toEqual([
        { path: '$', message: 'Paste or upload manifest JSON first.' },
      ])
    })

    it('shows client-side error when textarea is whitespace only', async () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importJsonText.value = '   \n\t  '

      await imp.validateImportJson()

      expect(mocks.validateManifestJson).not.toHaveBeenCalled()
      expect(imp.importValidationErrors.value).toHaveLength(1)
    })

    it('validates JSON and shows success toast for valid manifest', async () => {
      const manifest = makeManifest()
      mocks.validateManifestJson.mockResolvedValue({
        isValid: true,
        manifest,
        errors: [],
      })

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importJsonText.value = JSON.stringify(manifest)

      await imp.validateImportJson()

      expect(mocks.validateManifestJson).toHaveBeenCalledWith('b-1', JSON.stringify(manifest))
      expect(imp.importValidatedManifest.value).toEqual(manifest)
      expect(imp.importValidationErrors.value).toEqual([])
      expect(mocks.toastSuccess).toHaveBeenCalledWith('Manifest is valid.')
      expect(imp.importValidating.value).toBe(false)
    })

    it('validates JSON and shows error toast for invalid manifest', async () => {
      const errors = [
        { path: '$.packId', message: 'Invalid pack ID' },
        { path: '$.displayName', message: 'Required' },
      ]
      mocks.validateManifestJson.mockResolvedValue({
        isValid: false,
        manifest: null,
        errors,
      })

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importJsonText.value = '{"bad":"json"}'

      await imp.validateImportJson()

      expect(imp.importValidationErrors.value).toEqual(errors)
      expect(imp.importValidatedManifest.value).toBeNull()
      expect(mocks.toastError).toHaveBeenCalledWith('Manifest has 2 validation error(s).')
    })

    it('handles validation API error', async () => {
      mocks.validateManifestJson.mockRejectedValue(new Error('validation API down'))

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importJsonText.value = '{"some":"json"}'

      await imp.validateImportJson()

      expect(imp.importErrorMessage.value).toBe('validation API down')
      expect(mocks.toastError).toHaveBeenCalledWith('validation API down')
      expect(imp.importValidating.value).toBe(false)
    })
  })

  describe('runImportPreview', () => {
    it('does nothing when no validated manifest', async () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)
      await imp.runImportPreview()
      expect(mocks.applyStarterPack).not.toHaveBeenCalled()
    })

    it('runs dry-run preview successfully', async () => {
      const manifest = makeManifest()
      const previewResult = makeResult({
        dryRun: true,
        applied: false,
        actions: [{ entityType: 'label', operation: 'create', key: 'l1', reason: 'new' }],
      })
      mocks.applyStarterPack.mockResolvedValue(previewResult)

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = manifest

      await imp.runImportPreview()

      expect(mocks.applyStarterPack).toHaveBeenCalledWith('b-1', {
        manifest,
        dryRun: true,
      })
      expect(imp.importLatestResult.value).toEqual(previewResult)
      expect(mocks.toastSuccess).toHaveBeenCalledWith('Dry-run preview generated.')
      expect(imp.importRunningPreview.value).toBe(false)
    })

    it('shows error toast for blocking conflicts in preview', async () => {
      mocks.applyStarterPack.mockResolvedValue(
        makeResult({
          dryRun: true,
          applied: false,
          hasBlockingConflicts: true,
          conflicts: [{ code: 'C', path: '$', message: 'blocked', existingValue: null, incomingValue: null, severity: 'blocking' }],
        }),
      )

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()

      await imp.runImportPreview()

      expect(mocks.toastError).toHaveBeenCalledWith('Dry-run found blocking conflicts.')
    })

    it('shows warning toast for warning conflicts in preview', async () => {
      mocks.applyStarterPack.mockResolvedValue(
        makeResult({
          dryRun: true,
          applied: false,
          conflicts: [{ code: 'C', path: '$', message: 'warn', existingValue: null, incomingValue: null, severity: 'warning' }],
        }),
      )

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()

      await imp.runImportPreview()

      expect(mocks.toastWarning).toHaveBeenCalledWith('Dry-run found warnings.')
    })

    it('shows warning toast when preview has skip actions', async () => {
      mocks.applyStarterPack.mockResolvedValue(
        makeResult({
          dryRun: true,
          applied: false,
          actions: [{ entityType: 'card', operation: 'skip', key: 'k', reason: 'exists' }],
        }),
      )

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()

      await imp.runImportPreview()

      expect(mocks.toastWarning).toHaveBeenCalledWith('Dry-run found warnings.')
    })

    it('handles preview API error', async () => {
      mocks.applyStarterPack.mockRejectedValue(new Error('preview failed'))

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()

      await imp.runImportPreview()

      expect(imp.importErrorMessage.value).toBe('preview failed')
      expect(mocks.toastError).toHaveBeenCalledWith('preview failed')
      expect(imp.importRunningPreview.value).toBe(false)
    })
  })

  describe('applyImportPack', () => {
    it('does nothing when no validated manifest', async () => {
      const imp = useStarterPackImport(() => 'b-1', onApplied)
      await imp.applyImportPack()
      expect(mocks.applyStarterPack).not.toHaveBeenCalled()
    })

    it('applies pack successfully and refreshes board', async () => {
      const manifest = makeManifest()
      mocks.applyStarterPack.mockResolvedValue(makeResult())

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = manifest

      await imp.applyImportPack()

      expect(mocks.applyStarterPack).toHaveBeenCalledWith('b-1', { manifest, dryRun: false })
      expect(mocks.fetchBoard).toHaveBeenCalledWith('b-1')
      expect(onApplied).toHaveBeenCalled()
      expect(mocks.toastSuccess).toHaveBeenCalledWith('Applied imported manifest.')
      expect(imp.importApplying.value).toBe(false)
    })

    it('shows warning toast when applied with warning conflicts', async () => {
      mocks.applyStarterPack.mockResolvedValue(
        makeResult({
          applied: true,
          conflicts: [{ code: 'C', path: '$', message: 'w', existingValue: null, incomingValue: null, severity: 'warning' }],
        }),
      )

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()

      await imp.applyImportPack()

      expect(mocks.toastWarning).toHaveBeenCalledWith('Applied imported manifest with warnings.')
      expect(mocks.fetchBoard).toHaveBeenCalled()
      expect(onApplied).toHaveBeenCalled()
    })

    it('shows warning toast when applied with skip actions', async () => {
      mocks.applyStarterPack.mockResolvedValue(
        makeResult({
          applied: true,
          actions: [{ entityType: 'card', operation: 'skip', key: 'k', reason: 'r' }],
        }),
      )

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()

      await imp.applyImportPack()

      expect(mocks.toastWarning).toHaveBeenCalledWith('Applied imported manifest with warnings.')
    })

    it('does not finalize when result has blocking conflicts', async () => {
      mocks.applyStarterPack.mockResolvedValue(
        makeResult({
          applied: false,
          hasBlockingConflicts: true,
          conflicts: [{ code: 'C', path: '$', message: 'blocked', existingValue: null, incomingValue: null, severity: 'blocking' }],
        }),
      )

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()

      await imp.applyImportPack()

      expect(mocks.toastError).toHaveBeenCalledWith('Import apply is blocked by conflicts.')
      expect(mocks.fetchBoard).not.toHaveBeenCalled()
      expect(onApplied).not.toHaveBeenCalled()
    })

    it('shows warning when applied is false (no changes)', async () => {
      mocks.applyStarterPack.mockResolvedValue(makeResult({ applied: false }))

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()

      await imp.applyImportPack()

      expect(mocks.toastWarning).toHaveBeenCalledWith('Imported manifest did not apply any changes.')
      expect(mocks.fetchBoard).not.toHaveBeenCalled()
      expect(onApplied).not.toHaveBeenCalled()
    })

    it('handles 409 conflict error response', async () => {
      const conflictPayload = makeResult({
        applied: false,
        hasBlockingConflicts: true,
        conflicts: [{ code: 'C', path: '$', message: 'blocked', existingValue: null, incomingValue: null, severity: 'blocking' }],
      })
      mocks.applyStarterPack.mockRejectedValue({
        response: { status: 409, data: conflictPayload },
      })

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()

      await imp.applyImportPack()

      expect(imp.importLatestResult.value).toEqual(conflictPayload)
      expect(mocks.toastError).toHaveBeenCalledWith('Import apply is blocked by conflicts.')
      expect(mocks.fetchBoard).not.toHaveBeenCalled()
    })

    it('handles non-409 error response', async () => {
      mocks.applyStarterPack.mockRejectedValue(new Error('server error'))

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()

      await imp.applyImportPack()

      expect(imp.importErrorMessage.value).toBe('server error')
      expect(mocks.toastError).toHaveBeenCalledWith('server error')
    })
  })

  describe('extractConflictResult (tested via applyImportPack)', () => {
    it('returns null for non-object error', async () => {
      mocks.applyStarterPack.mockRejectedValue('string error')

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()
      await imp.applyImportPack()

      expect(imp.importLatestResult.value).toBeNull()
    })

    it('returns null for null error', async () => {
      mocks.applyStarterPack.mockRejectedValue(null)

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()
      await imp.applyImportPack()

      expect(imp.importLatestResult.value).toBeNull()
    })

    it('returns null for non-409 status', async () => {
      mocks.applyStarterPack.mockRejectedValue({
        response: { status: 500, data: {} },
        message: 'Internal Server Error',
      })

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()
      await imp.applyImportPack()

      expect(imp.importLatestResult.value).toBeNull()
    })

    it('returns null for 409 with non-object data', async () => {
      mocks.applyStarterPack.mockRejectedValue({
        response: { status: 409, data: 'string' },
        message: 'Conflict',
      })

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()
      await imp.applyImportPack()

      expect(imp.importLatestResult.value).toBeNull()
    })

    it('returns null for 409 with null data', async () => {
      mocks.applyStarterPack.mockRejectedValue({
        response: { status: 409, data: null },
        message: 'Conflict',
      })

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()
      await imp.applyImportPack()

      expect(imp.importLatestResult.value).toBeNull()
    })

    it('returns null for 409 with incomplete payload', async () => {
      mocks.applyStarterPack.mockRejectedValue({
        response: {
          status: 409,
          data: { packId: 'x', dryRun: false, applied: false, actions: [], conflicts: [] },
        },
        message: 'Conflict',
      })

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()
      await imp.applyImportPack()

      expect(imp.importLatestResult.value).toBeNull()
    })

    it('returns null for 409 with non-boolean dryRun', async () => {
      mocks.applyStarterPack.mockRejectedValue({
        response: {
          status: 409,
          data: { boardId: 'b', packId: 'p', dryRun: 'false', applied: false, actions: [], conflicts: [] },
        },
        message: 'Conflict',
      })

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()
      await imp.applyImportPack()

      expect(imp.importLatestResult.value).toBeNull()
    })

    it('returns null for error without response property', async () => {
      mocks.applyStarterPack.mockRejectedValue({ message: 'no response' })

      const imp = useStarterPackImport(() => 'b-1', onApplied)
      imp.importValidatedManifest.value = makeManifest()
      await imp.applyImportPack()

      expect(imp.importLatestResult.value).toBeNull()
    })
  })
})
