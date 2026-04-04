import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import ExportImportView from '../../views/ExportImportView.vue'

const mocks = vi.hoisted(() => ({
  exportBoardJson: vi.fn(),
  importBoardJson: vi.fn(),
  successToast: vi.fn(),
  errorToast: vi.fn(),
  warningToast: vi.fn(),
  requireUserId: vi.fn(),
}))

vi.mock('../../api/exportImportApi', () => ({
  exportImportApi: {
    exportBoardJson: mocks.exportBoardJson,
    importBoardJson: mocks.importBoardJson,
  },
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => ({
    requireUserId: mocks.requireUserId,
  }),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.successToast,
    error: mocks.errorToast,
    warning: mocks.warningToast,
  }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_err: unknown, fallback: string) => ({ message: fallback }),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('ExportImportView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.exportBoardJson.mockResolvedValue({ boardId: 'b-1', columns: [], cards: [] })
    mocks.importBoardJson.mockResolvedValue({
      success: true,
      errorMessage: null,
      columnsImported: 2,
      cardsImported: 5,
      labelsImported: 1,
    })
  })

  it('renders the Export / Import page title', () => {
    const wrapper = mount(ExportImportView)
    expect(wrapper.text()).toContain('Export / Import')
  })

  it('renders Export and Import tabs', () => {
    const wrapper = mount(ExportImportView)
    expect(wrapper.text()).toContain('Export')
    expect(wrapper.text()).toContain('Import')
  })

  it('shows the export panel by default', () => {
    const wrapper = mount(ExportImportView)
    expect(wrapper.text()).toContain('Export Board')
    expect(wrapper.text()).toContain('Board ID')
  })

  it('switches to import panel when Import tab is clicked', async () => {
    const wrapper = mount(ExportImportView)

    const importTab = wrapper.findAll('button').find((b) => b.text() === 'Import')
    expect(importTab).toBeDefined()
    await importTab!.trigger('click')
    await waitForUi()

    expect(wrapper.text()).toContain('Import Board')
    expect(wrapper.text()).toContain('Paste board JSON data to import.')
  })

  describe('export tab', () => {
    it('shows warning toast when exporting without a board ID', async () => {
      const wrapper = mount(ExportImportView)

      const exportBtn = wrapper.findAll('button').find((b) => b.text().includes('Export JSON'))
      expect(exportBtn).toBeDefined()
      await exportBtn!.trigger('click')
      await waitForUi()

      expect(mocks.warningToast).toHaveBeenCalledWith('Please enter a board ID.')
      expect(mocks.exportBoardJson).not.toHaveBeenCalled()
    })

    it('calls exportBoardJson with the entered board ID', async () => {
      const wrapper = mount(ExportImportView)

      await wrapper.find('#export-board').setValue('my-board-id')
      const exportBtn = wrapper.findAll('button').find((b) => b.text().includes('Export JSON'))
      await exportBtn!.trigger('click')
      await waitForUi()

      expect(mocks.exportBoardJson).toHaveBeenCalledWith('my-board-id')
      expect(mocks.successToast).toHaveBeenCalledWith('Board exported successfully')
    })

    it('renders the JSON result after successful export', async () => {
      const exportPayload = { boardId: 'my-board-id', columns: [], cards: [] }
      mocks.exportBoardJson.mockResolvedValue(exportPayload)

      const wrapper = mount(ExportImportView)

      await wrapper.find('#export-board').setValue('my-board-id')
      const exportBtn = wrapper.findAll('button').find((b) => b.text().includes('Export JSON'))
      await exportBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.text()).toContain('my-board-id')
      expect(wrapper.find('pre.td-json-viewer').exists()).toBe(true)
    })

    it('shows Copy and Download buttons after a successful export', async () => {
      const wrapper = mount(ExportImportView)

      await wrapper.find('#export-board').setValue('my-board-id')
      const exportBtn = wrapper.findAll('button').find((b) => b.text().includes('Export JSON'))
      await exportBtn!.trigger('click')
      await waitForUi()

      const resultActionBtns = wrapper.findAll('.td-result-actions button')
      expect(resultActionBtns.some((b) => b.text().includes('Copy'))).toBe(true)
      expect(resultActionBtns.some((b) => b.text().includes('Download'))).toBe(true)
    })

    it('shows error toast and clears result when export fails', async () => {
      mocks.exportBoardJson.mockRejectedValue(new Error('not found'))

      const wrapper = mount(ExportImportView)

      await wrapper.find('#export-board').setValue('bad-board')
      const exportBtn = wrapper.findAll('button').find((b) => b.text().includes('Export JSON'))
      await exportBtn!.trigger('click')
      await waitForUi()

      expect(mocks.errorToast).toHaveBeenCalledWith(
        'Export failed. Check board ID and permissions.',
      )
      expect(wrapper.find('pre.td-json-viewer').exists()).toBe(false)
    })
  })

  describe('import tab', () => {
    async function switchToImport(wrapper: ReturnType<typeof mount>) {
      const importTab = wrapper.findAll('button').find((b) => b.text() === 'Import')
      await importTab!.trigger('click')
      await waitForUi()
    }

    it('shows step 1 with a textarea for JSON paste', async () => {
      const wrapper = mount(ExportImportView)
      await switchToImport(wrapper)

      expect(wrapper.find('textarea').exists()).toBe(true)
      const validateBtn = wrapper.findAll('button').find((b) =>
        b.text().includes('Validate & Preview'),
      )
      expect(validateBtn).toBeDefined()
    })

    it('Validate & Preview button is disabled when textarea is empty', async () => {
      const wrapper = mount(ExportImportView)
      await switchToImport(wrapper)

      const validateBtn = wrapper.findAll('button').find((b) =>
        b.text().includes('Validate & Preview'),
      )
      expect(validateBtn!.attributes('disabled')).toBeDefined()
    })

    it('advances to step 2 preview when Validate & Preview is clicked with content', async () => {
      const wrapper = mount(ExportImportView)
      await switchToImport(wrapper)

      await wrapper.find('textarea').setValue('{"boardId":"x","columns":[]}')
      const validateBtn = wrapper.findAll('button').find((b) =>
        b.text().includes('Validate & Preview'),
      )
      await validateBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.text()).toContain('Review the data before importing.')
      expect(wrapper.findAll('button').some((b) => b.text().includes('Back'))).toBe(true)
      expect(wrapper.findAll('button').some((b) => b.text().includes('Import Board'))).toBe(true)
    })

    it('returns to step 1 when Back is clicked from step 2', async () => {
      const wrapper = mount(ExportImportView)
      await switchToImport(wrapper)

      await wrapper.find('textarea').setValue('{"boardId":"x"}')
      const validateBtn = wrapper.findAll('button').find((b) =>
        b.text().includes('Validate & Preview'),
      )
      await validateBtn!.trigger('click')
      await waitForUi()

      const backBtn = wrapper.findAll('button').find((b) => b.text().includes('Back'))
      await backBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.find('textarea').exists()).toBe(true)
      expect(wrapper.text()).toContain('Paste board JSON data to import.')
    })

    it('calls importBoardJson and shows success result on step 3', async () => {
      mocks.importBoardJson.mockResolvedValue({
        success: true,
        errorMessage: null,
        columnsImported: 3,
        cardsImported: 10,
        labelsImported: 2,
      })

      const wrapper = mount(ExportImportView)
      await switchToImport(wrapper)

      await wrapper.find('textarea').setValue('{"boardId":"x","columns":[]}')
      const validateBtn = wrapper.findAll('button').find((b) =>
        b.text().includes('Validate & Preview'),
      )
      await validateBtn!.trigger('click')
      await waitForUi()

      const importBtn = wrapper.findAll('button').find((b) => b.text().includes('Import Board'))
      await importBtn!.trigger('click')
      await waitForUi()

      expect(mocks.importBoardJson).toHaveBeenCalled()
      expect(mocks.successToast).toHaveBeenCalledWith('Board imported successfully')
      expect(wrapper.text()).toContain('Board imported successfully')
      expect(wrapper.text()).toContain('Columns: 3, Cards: 10, Labels: 2')
    })

    it('shows error result on step 3 when import fails with API error', async () => {
      mocks.importBoardJson.mockResolvedValue({
        success: false,
        errorMessage: 'Invalid schema',
        columnsImported: 0,
        cardsImported: 0,
        labelsImported: 0,
      })

      const wrapper = mount(ExportImportView)
      await switchToImport(wrapper)

      await wrapper.find('textarea').setValue('{}')
      const validateBtn = wrapper.findAll('button').find((b) =>
        b.text().includes('Validate & Preview'),
      )
      await validateBtn!.trigger('click')
      await waitForUi()

      const importBtn = wrapper.findAll('button').find((b) => b.text().includes('Import Board'))
      await importBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.text()).toContain('Invalid schema')
      expect(wrapper.text()).toContain('ERR')
    })

    it('shows error result on step 3 when import throws', async () => {
      mocks.importBoardJson.mockRejectedValue(new Error('network error'))

      const wrapper = mount(ExportImportView)
      await switchToImport(wrapper)

      await wrapper.find('textarea').setValue('{}')
      const validateBtn = wrapper.findAll('button').find((b) =>
        b.text().includes('Validate & Preview'),
      )
      await validateBtn!.trigger('click')
      await waitForUi()

      const importBtn = wrapper.findAll('button').find((b) => b.text().includes('Import Board'))
      await importBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.text()).toContain('ERR')
      expect(mocks.errorToast).toHaveBeenCalled()
    })

    it('resets to step 1 when Import Another is clicked after step 3', async () => {
      const wrapper = mount(ExportImportView)
      await switchToImport(wrapper)

      await wrapper.find('textarea').setValue('{}')
      const validateBtn = wrapper.findAll('button').find((b) =>
        b.text().includes('Validate & Preview'),
      )
      await validateBtn!.trigger('click')
      await waitForUi()

      const importBtn = wrapper.findAll('button').find((b) => b.text().includes('Import Board'))
      await importBtn!.trigger('click')
      await waitForUi()

      const importAnotherBtn = wrapper.findAll('button').find((b) =>
        b.text().includes('Import Another'),
      )
      expect(importAnotherBtn).toBeDefined()
      await importAnotherBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.find('textarea').exists()).toBe(true)
      expect(wrapper.text()).toContain('Paste board JSON data to import.')
    })
  })
})
