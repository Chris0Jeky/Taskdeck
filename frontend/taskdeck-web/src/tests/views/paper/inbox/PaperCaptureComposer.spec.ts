import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import PaperCaptureComposer from '../../../../views/paper/inbox/PaperCaptureComposer.vue'

type MockBoard = { id: string; name: string; canWrite?: boolean }

const defaultBoards = (): MockBoard[] => [
  { id: 'board-alpha', name: 'Alpha' },
  { id: 'board-beta', name: 'Beta' },
]

const mockBoardStore = reactive({
  boards: defaultBoards() as MockBoard[],
  fetchBoards: vi.fn<() => Promise<void>>(),
})

vi.mock('../../../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

describe('PaperCaptureComposer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockBoardStore.boards = defaultBoards()
    mockBoardStore.fetchBoards.mockResolvedValue(undefined)
  })

  it('emits submit on Cmd+Enter with body, board, labels, dueAt', async () => {
    const wrapper = mount(PaperCaptureComposer)
    const textarea = wrapper.find('textarea')
    await textarea.setValue('Pick the right field tone for the ledger header')

    // Pick a board.
    const select = wrapper.find('select')
    await select.setValue('board-alpha')

    // Add two labels via the chip input.
    const labelInput = wrapper.find('input[type="text"]')
    await labelInput.setValue('arch')
    await labelInput.trigger('keydown', { key: 'Enter' })
    await labelInput.setValue('read-later')
    await labelInput.trigger('keydown', { key: 'Enter' })

    // Set due date.
    const dueInput = wrapper.find('input[type="date"]')
    await dueInput.setValue('2026-05-01')

    // Cmd+Enter.
    await textarea.trigger('keydown', { key: 'Enter', metaKey: true })

    const events = wrapper.emitted('submit')
    expect(events).toBeDefined()
    expect(events?.[0]?.[0]).toEqual({
      text: 'Pick the right field tone for the ledger header',
      boardId: 'board-alpha',
      labels: ['arch', 'read-later'],
      dueAt: '2026-05-01',
    })
  })

  it('also submits on Ctrl+Enter (non-Mac path)', async () => {
    const wrapper = mount(PaperCaptureComposer)
    const textarea = wrapper.find('textarea')
    await textarea.setValue('From a Linux box')
    await textarea.trigger('keydown', { key: 'Enter', ctrlKey: true })
    expect(wrapper.emitted('submit')).toBeDefined()
  })

  it('does not submit on plain Enter (newline only)', async () => {
    const wrapper = mount(PaperCaptureComposer)
    const textarea = wrapper.find('textarea')
    await textarea.setValue('Multi-line\nthought')
    await textarea.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('reflects the chosen board in the select binding', async () => {
    const wrapper = mount(PaperCaptureComposer)
    const select = wrapper.find('select')
    await select.setValue('board-beta')
    expect((select.element as HTMLSelectElement).value).toBe('board-beta')

    // Confirm the value reaches the submit payload.
    await wrapper.find('textarea').setValue('hi')
    await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })
    const submitted = wrapper.emitted('submit')?.[0]?.[0] as { boardId: string | null }
    expect(submitted.boardId).toBe('board-beta')
  })

  it('syncs the board picker when the active inbox scope changes', async () => {
    const wrapper = mount(PaperCaptureComposer, { props: { defaultBoardId: 'board-alpha' } })

    await wrapper.setProps({ defaultBoardId: 'board-beta' })
    expect((wrapper.find('select').element as HTMLSelectElement).value).toBe('board-beta')

    await wrapper.find('textarea').setValue('Capture in the new board scope')
    await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })

    const submitted = wrapper.emitted('submit')?.[0]?.[0] as { boardId: string | null }
    expect(submitted.boardId).toBe('board-beta')
  })

  it('reflects label selections in the submit payload', async () => {
    const wrapper = mount(PaperCaptureComposer)
    const labelInput = wrapper.find('input[type="text"]')
    await labelInput.setValue('motion')
    await labelInput.trigger('keydown', { key: 'Enter' })
    await labelInput.setValue('qa')
    await labelInput.trigger('keydown', { key: 'Enter' })

    await wrapper.find('textarea').setValue('thought')
    await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })

    const submitted = wrapper.emitted('submit')?.[0]?.[0] as { labels: string[] }
    expect(submitted.labels).toEqual(['motion', 'qa'])
  })

  it('does not add labels on Enter while IME composition is active', async () => {
    const wrapper = mount(PaperCaptureComposer)
    const labelInput = wrapper.find('input[type="text"]')
    await labelInput.setValue('nihongo')
    await labelInput.trigger('keydown', { key: 'Enter', isComposing: true })

    expect((labelInput.element as HTMLInputElement).value).toBe('nihongo')
    expect(wrapper.text()).not.toContain('nihongo')
  })

  it('emits attachments-changed when files are dropped', async () => {
    const wrapper = mount(PaperCaptureComposer)
    const dropZone = wrapper.find('[data-testid="paper-composer-drop"]')
    const file = new File(['hello'], 'note.txt', { type: 'text/plain' })
    const dataTransfer = { files: [file] } as unknown as DataTransfer
    await dropZone.trigger('drop', { dataTransfer })
    const events = wrapper.emitted('attachments-changed')
    expect(events).toBeDefined()
    expect((events?.[0]?.[0] as File[])[0]?.name).toBe('note.txt')
  })

  it('does not submit when body is empty', async () => {
    const wrapper = mount(PaperCaptureComposer)
    await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })
    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('does not submit while creation is already in flight', async () => {
    const wrapper = mount(PaperCaptureComposer, { props: { submitting: true } })
    expect(wrapper.find('textarea').attributes('disabled')).toBeDefined()
    expect(wrapper.find('select[aria-label="Board picker"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('input[aria-label="Add label"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('input[aria-label="Due date"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('input[aria-label="Attach files"]').attributes('disabled')).toBeDefined()
    const browseButton = wrapper.findAll('button').find((button) => button.text().includes('Browse'))
    expect(browseButton?.attributes('disabled')).toBeDefined()

    await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })

    expect(wrapper.emitted('submit')).toBeUndefined()
    const captureButton = wrapper.findAll('button').find((button) => button.text().includes('Capture'))
    expect(captureButton?.attributes('disabled')).toBeDefined()
  })

  // --- board picker write capability (#1836) -------------------------------

  it('renders a read-only board visible but disabled and annotated view-only', () => {
    mockBoardStore.boards = [
      { id: 'board-alpha', name: 'Alpha', canWrite: true },
      { id: 'board-readonly', name: 'Archive', canWrite: false },
    ]
    const wrapper = mount(PaperCaptureComposer)

    const options = wrapper.findAll('select[aria-label="Board picker"] option')
    const readOnly = options.find((option) => option.attributes('value') === 'board-readonly')

    // Visible, NOT filtered away.
    expect(readOnly).toBeDefined()
    expect(readOnly!.attributes('disabled')).toBeDefined()
    expect(readOnly!.text()).toContain('Archive')
    expect(readOnly!.text()).toContain('view-only')
    expect(wrapper.find('[data-testid="composer-view-only-hint"]').exists()).toBe(true)
  })

  it('leaves a write-capable board enabled and unannotated', () => {
    mockBoardStore.boards = [{ id: 'board-alpha', name: 'Alpha', canWrite: true }]
    const wrapper = mount(PaperCaptureComposer)

    const option = wrapper
      .findAll('select[aria-label="Board picker"] option')
      .find((o) => o.attributes('value') === 'board-alpha')

    expect(option!.attributes('disabled')).toBeUndefined()
    expect(option!.text()).toBe('Alpha')
    expect(option!.text()).not.toContain('view-only')
    expect(wrapper.find('[data-testid="composer-view-only-hint"]').exists()).toBe(false)
  })

  it('treats a board with no canWrite field as writable (older payloads unchanged)', () => {
    mockBoardStore.boards = [{ id: 'board-alpha', name: 'Alpha' }]
    const wrapper = mount(PaperCaptureComposer)

    const option = wrapper
      .findAll('select[aria-label="Board picker"] option')
      .find((o) => o.attributes('value') === 'board-alpha')

    expect(option!.attributes('disabled')).toBeUndefined()
    expect(option!.text()).toBe('Alpha')
  })

  it('blocks capture while a read-only board is the active scope', async () => {
    // defaultBoardId can preselect a board the user only reads; capturing into it
    // would produce an item that 403s the moment it is accepted for triage.
    mockBoardStore.boards = [{ id: 'board-readonly', name: 'Archive', canWrite: false }]
    const wrapper = mount(PaperCaptureComposer, { props: { defaultBoardId: 'board-readonly' } })

    await wrapper.find('textarea').setValue('a thought that has nowhere to land')
    await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })

    expect(wrapper.emitted('submit')).toBeUndefined()
    const captureButton = wrapper.findAll('button').find((button) => button.text().includes('Capture'))
    expect(captureButton?.attributes('disabled')).toBeDefined()

    // Switching to "no board" unblocks it — the escape hatch is one click away.
    await wrapper.setProps({ defaultBoardId: null })
    await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })
    expect(wrapper.emitted('submit')).toHaveLength(1)
  })

  it('keeps the draft after submit until the parent confirms success', async () => {
    const wrapper = mount(PaperCaptureComposer)
    await wrapper.find('textarea').setValue('Preserve this if the API fails')
    await wrapper.find('input[type="text"]').setValue('paper')
    await wrapper.find('input[type="text"]').trigger('keydown', { key: 'Enter' })

    await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })
    expect((wrapper.find('textarea').element as HTMLTextAreaElement).value).toBe('Preserve this if the API fails')
    expect(wrapper.text()).toContain('paper')

    ;(wrapper.vm as unknown as { resetDraft: () => void }).resetDraft()
    await wrapper.vm.$nextTick()
    expect((wrapper.find('textarea').element as HTMLTextAreaElement).value).toBe('')
    expect(wrapper.text()).not.toContain('paper')
  })
})
