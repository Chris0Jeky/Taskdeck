import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import ExportImportView from '../../views/ExportImportView.vue'
import exportImportSource from '../../views/ExportImportView.vue?raw'

const mocks = vi.hoisted(() => ({
  exportBoardJson: vi.fn(),
  importBoard: vi.fn(),
  previewBoardJson: vi.fn(),
  successToast: vi.fn(),
  errorToast: vi.fn(),
  warningToast: vi.fn(),
  requireUserId: vi.fn(),
}))

vi.mock('../../api/exportImportApi', () => ({
  exportImportApi: {
    exportBoardJson: mocks.exportBoardJson,
    importBoard: mocks.importBoard,
    previewBoardJson: mocks.previewBoardJson,
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
    mocks.previewBoardJson.mockResolvedValue({ board: { name: 'Preview board', cards: [] }, cardCount: 0, columnCount: 0, sourceAssignees: [], me: { userId: 'me', displayName: 'Me' } })
    mocks.exportBoardJson.mockResolvedValue({ boardId: 'b-1', columns: [], cards: [] })
    mocks.importBoard.mockResolvedValue({
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

  it('requires an explicit choice for every source person and sends only reviewed mappings', async () => {
    mocks.previewBoardJson.mockResolvedValue({
      board: { name: 'Mapped', cards: [{ title: 'Imported task', columnName: 'Next' }] }, cardCount: 1, columnCount: 1,
      sourceAssignees: [
        { sourceKey: 'source-a', displayName: 'Alex', affectedCardCount: 1 },
        { sourceKey: 'source-b', displayName: 'Blair', affectedCardCount: 1 },
      ], me: { userId: 'me', displayName: 'Current owner' },
    })
    const wrapper = mount(ExportImportView)
    await wrapper.findAll('button').find(b => b.text() === 'Import')!.trigger('click')
    await wrapper.find('textarea').setValue('{"source":"fixture"}')
    await wrapper.findAll('button').find(b => b.text() === 'Validate & Preview')!.trigger('click')
    await waitForUi()
    const apply = () => wrapper.findAll('button').find(b => b.text() === 'Import Board')!
    expect(wrapper.text()).toContain('Imported task')
    expect(wrapper.text()).toContain('1 affected cards')
    expect(apply().attributes('disabled')).toBeDefined()
    await wrapper.findAll('select')[0]!.setValue('me')
    expect(apply().attributes('disabled')).toBeDefined()
    await wrapper.findAll('select')[1]!.setValue('unassigned')
    expect(apply().attributes('disabled')).toBeUndefined()
    await apply().trigger('click'); await waitForUi()
    expect(mocks.importBoard).toHaveBeenCalledWith(expect.objectContaining({
      name: 'Mapped', assigneeMappings: { 'source-a': 'me', 'source-b': null },
    }))
  })

  it('keeps invalid JSON input editable and never presents it as validated', async () => {
    mocks.previewBoardJson.mockRejectedValue(new Error('Invalid'))
    const wrapper = mount(ExportImportView)
    await wrapper.findAll('button').find(b => b.text() === 'Import')!.trigger('click')
    await wrapper.find('textarea').setValue('{invalid}')
    await wrapper.findAll('button').find(b => b.text() === 'Validate & Preview')!.trigger('click')
    await waitForUi()
    expect(wrapper.find('[role="alert"]').text()).toContain('invalid')
    expect((wrapper.find('textarea').element as HTMLTextAreaElement).value).toBe('{invalid}')
    expect(mocks.importBoard).not.toHaveBeenCalled()
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

  it('renders with the Paper theme class hooks (not the legacy Obsidian ones)', () => {
    const wrapper = mount(ExportImportView)

    // Root, tabs, and panels should use the Paper (`paper-portability__*`)
    // idiom, and none of the legacy Obsidian hooks should survive.
    expect(wrapper.find('.paper-portability').exists()).toBe(true)
    expect(wrapper.find('.paper-portability__tabs').exists()).toBe(true)
    expect(wrapper.find('.paper-portability__panel').exists()).toBe(true)
    expect(wrapper.find('[class*="td-export-import"]').exists()).toBe(false)
    expect(wrapper.find('[class*="td-tab"]').exists()).toBe(false)
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
      expect(wrapper.find('pre.paper-portability__json').exists()).toBe(true)
    })

    it('shows Copy and Download buttons after a successful export', async () => {
      const wrapper = mount(ExportImportView)

      await wrapper.find('#export-board').setValue('my-board-id')
      const exportBtn = wrapper.findAll('button').find((b) => b.text().includes('Export JSON'))
      await exportBtn!.trigger('click')
      await waitForUi()

      const resultActionBtns = wrapper.findAll('.paper-portability__result-actions button')
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
      expect(wrapper.find('pre.paper-portability__json').exists()).toBe(false)
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

      expect(wrapper.text()).toContain('Create a new board')
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

    it('calls importBoard and shows success result on step 3', async () => {
      mocks.importBoard.mockResolvedValue({
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

      expect(mocks.importBoard).toHaveBeenCalled()
      expect(mocks.successToast).toHaveBeenCalledWith('Board imported successfully')
      expect(wrapper.text()).toContain('Board imported successfully')
      expect(wrapper.text()).toContain('Columns: 3, Cards: 10, Labels: 2')
    })

    it('shows error result on step 3 when import fails with API error', async () => {
      mocks.importBoard.mockResolvedValue({
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
      mocks.importBoard.mockRejectedValue(new Error('network error'))

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

// ── #1808 review (MEDIUM): Legacy ("off") mode substrate guard ──
// Paper tokens exist only under `.paper` / `.paper-night` (paper-tokens.css), so
// in Legacy mode this view's `color: var(--ink, …)` resolves to the near-black
// literal while AppShell's `.td-content` still paints `--td-surface-base`
// (#131313) — ~1.05:1 on the hero, and ~3.2:1 / ~2.5:1 on the tab strip. A root
// that sets the Paper ink MUST therefore also paint the Paper substrate; that is
// a no-op under `.paper`/`.paper-night`.
// Source is read through Vite's `?raw` rather than `node:fs` because
// `tsconfig.vitest.json` deliberately omits the "node" types.
// #1815 tracks unifying these per-view assertions into one wave-wide spec.
describe('ExportImportView Legacy-mode substrate', () => {
  it('paints --paper on the root wherever it sets --ink', () => {
    const rule = exportImportSource.match(/^\.paper-portability \{([\s\S]*?)\}/m)?.[1]
    expect(rule, '.paper-portability root rule').toBeTruthy()
    // Guard the guard: if the ink declaration were dropped or renamed, the
    // substrate assertion below would otherwise pass vacuously.
    expect(rule).toMatch(/color:\s*var\(--ink,\s*#[0-9a-fA-F]{3,8}\s*\)/)
    expect(rule).toMatch(/background:\s*var\(--paper,\s*#[0-9a-fA-F]{3,8}\s*\)/)
  })
})
