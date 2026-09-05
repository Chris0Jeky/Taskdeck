import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import PaperCaptureComposer from '../../../../views/paper/inbox/PaperCaptureComposer.vue'
import { i18n, type SupportedLocale } from '../../../../i18n'

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
      source: 'Typed',
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

  it('associates the body with an external error receipt only while invalid', async () => {
    const wrapper = mount(PaperCaptureComposer, {
      props: { invalid: true, errorId: 'paper-inbox-capture-error' },
    })
    const textarea = wrapper.get('textarea[aria-label="Capture body"]')

    expect(textarea.attributes('aria-invalid')).toBe('true')
    expect(textarea.attributes('aria-describedby')).toBe('paper-inbox-capture-error')

    await wrapper.setProps({ invalid: false, errorId: null })

    expect(textarea.attributes('aria-invalid')).toBeUndefined()
    expect(textarea.attributes('aria-describedby')).toBeUndefined()
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

  it.each([
    ['en', 'No board · land in inbox'],
    ['it', 'Nessuna bacheca · arriva nell’Inbox'],
    ['es', 'Sin tablero · llega al Inbox'],
  ] as const)('translates the no-board option in %s', (locale, expected) => {
    const previousLocale = i18n.global.locale.value
    try {
      i18n.global.locale.value = locale as SupportedLocale
      const wrapper = mount(PaperCaptureComposer)
      expect(wrapper.find('select option').text()).toBe(expected)
    } finally {
      i18n.global.locale.value = previousLocale
    }
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

  it('preserves commas in a label until Enter commits the exact name', async () => {
    const wrapper = mount(PaperCaptureComposer)
    const labelInput = wrapper.find('input[type="text"]')
    await labelInput.setValue('Sales, EMEA')
    await labelInput.trigger('keydown', { key: ',' })

    expect((labelInput.element as HTMLInputElement).value).toBe('Sales, EMEA')
    expect(wrapper.findAll('.paper-composer__labels li')).toHaveLength(0)

    await labelInput.trigger('keydown', { key: 'Enter' })
    expect((labelInput.element as HTMLInputElement).value).toBe('')
    expect(wrapper.find('.paper-composer__labels').text()).toContain('Sales, EMEA')

    await wrapper.find('textarea').setValue('Keep this label lossless')
    await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })

    const submitted = wrapper.emitted('submit')?.[0]?.[0] as { labels: string[] }
    expect(submitted.labels).toEqual(['Sales, EMEA'])
  })

  it('does not add labels on Enter while IME composition is active', async () => {
    const wrapper = mount(PaperCaptureComposer)
    const labelInput = wrapper.find('input[type="text"]')
    await labelInput.setValue('nihongo')
    await labelInput.trigger('keydown', { key: 'Enter', isComposing: true })

    expect((labelInput.element as HTMLInputElement).value).toBe('nihongo')
    expect(wrapper.text()).not.toContain('nihongo')
  })

  it('marks attachments unavailable instead of accepting files that would be discarded', () => {
    const wrapper = mount(PaperCaptureComposer)
    expect(wrapper.get('[data-testid="paper-composer-attachments-unavailable"]').text())
      .toContain('not saved with captures yet')
    expect(wrapper.find('input[aria-label="Attach files"]').exists()).toBe(false)
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
    const labelInput = wrapper.find('input[type="text"]')
    await labelInput.setValue('committed')
    await labelInput.trigger('keydown', { key: 'Enter' })

    await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })
    expect((wrapper.find('textarea').element as HTMLTextAreaElement).value).toBe('Preserve this if the API fails')
    expect(wrapper.find('.paper-composer__labels').text()).toContain('committed')

    ;(wrapper.vm as unknown as { resetDraft: () => void }).resetDraft()
    await wrapper.vm.$nextTick()
    expect((wrapper.find('textarea').element as HTMLTextAreaElement).value).toBe('')
    expect((labelInput.element as HTMLInputElement).value).toBe('')
    // GH-2490 -- the committed chips go with the reset. Without this the
    // `labels.value = []` line is unguarded and a stale chip could ride into
    // the NEXT capture.
    expect(wrapper.find('.paper-composer__labels').exists()).toBe(false)
  })

  // GH-2490 -- typing a label and pressing Cmd/Ctrl+Enter without first pressing
  // Enter filed a capture with NO label, and the success reset then wiped the
  // box that was the only evidence of it.
  describe('pending label on submit (GH-2490)', () => {
    it('flushes an uncommitted label into the emitted payload', async () => {
      const wrapper = mount(PaperCaptureComposer)
      await wrapper.find('textarea').setValue('Ship the label flush')
      const labelInput = wrapper.find('input[type="text"]')
      await labelInput.setValue('  urgent  ')

      await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })

      const payload = wrapper.emitted('submit')?.[0]?.[0] as { labels: string[] }
      expect(payload.labels).toEqual(['urgent'])
      expect((labelInput.element as HTMLInputElement).value).toBe('')
    })

    it('does not duplicate a pending label that is already a chip', async () => {
      const wrapper = mount(PaperCaptureComposer)
      await wrapper.find('textarea').setValue('Dedupe me')
      const labelInput = wrapper.find('input[type="text"]')
      await labelInput.setValue('urgent')
      await labelInput.trigger('keydown', { key: 'Enter' })
      await labelInput.setValue('urgent')

      await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })

      const payload = wrapper.emitted('submit')?.[0]?.[0] as { labels: string[] }
      expect(payload.labels).toEqual(['urgent'])
    })

    it('keeps a comma inside the pending label as content (GH-2485 unregressed)', async () => {
      const wrapper = mount(PaperCaptureComposer)
      await wrapper.find('textarea').setValue('Commas are content')
      await wrapper.find('input[type="text"]').setValue('ops, later')

      await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })

      const payload = wrapper.emitted('submit')?.[0]?.[0] as { labels: string[] }
      expect(payload.labels).toEqual(['ops, later'])
    })

    it('flushes nothing when the label box holds only whitespace', async () => {
      const wrapper = mount(PaperCaptureComposer)
      await wrapper.find('textarea').setValue('No label here')
      await wrapper.find('input[type="text"]').setValue('   ')

      await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })

      const payload = wrapper.emitted('submit')?.[0]?.[0] as { labels: string[] }
      expect(payload.labels).toEqual([])
    })

    it('does not flush the pending label when the submit is refused', async () => {
      mockBoardStore.boards = [{ id: 'board-readonly', name: 'Archive', canWrite: false }]
      const wrapper = mount(PaperCaptureComposer, { props: { defaultBoardId: 'board-readonly' } })
      await wrapper.find('textarea').setValue('nowhere to land')
      const labelInput = wrapper.find('input[type="text"]')
      await labelInput.setValue('urgent')

      await wrapper.find('textarea').trigger('keydown', { key: 'Enter', metaKey: true })

      expect(wrapper.emitted('submit')).toBeUndefined()
      // The box still holds the evidence while the capture cannot be filed.
      expect((labelInput.element as HTMLInputElement).value).toBe('urgent')
    })

    it('carries the pending label through snapshot and restore', async () => {
      const wrapper = mount(PaperCaptureComposer)
      await wrapper.find('textarea').setValue('Survive the redirect')
      const labelInput = wrapper.find('input[type="text"]')
      await labelInput.setValue('half-typed')

      const vm = wrapper.vm as unknown as {
        snapshotDraft: () => { labelInput: string }
        restoreDraft: (draft: { text: string; labelInput?: string | null }) => void
        resetDraft: () => void
      }
      const snapshot = vm.snapshotDraft()
      expect(snapshot.labelInput).toBe('half-typed')

      vm.resetDraft()
      vm.restoreDraft({ ...snapshot, text: 'Survive the redirect' })
      await wrapper.vm.$nextTick()
      expect((labelInput.element as HTMLInputElement).value).toBe('half-typed')
    })

    it('restores a pre-GH-2490 draft with no pending label as an empty box', async () => {
      const wrapper = mount(PaperCaptureComposer)
      const vm = wrapper.vm as unknown as {
        restoreDraft: (draft: { text: string }) => void
      }
      vm.restoreDraft({ text: 'older stash' })
      await wrapper.vm.$nextTick()
      expect((wrapper.find('input[type="text"]').element as HTMLInputElement).value).toBe('')
    })
  })

  // GH-2141 -- the Paper skin could not file a transcript without dropping into
  // the Legacy modal. A transcript source is what routes a capture to the LLM
  // triage extractor server-side, so these assert the CHOICE reaches the
  // payload, and that the honest sentence is on screen when it is made.
  describe('capture source (GH-2141)', () => {
    it('defaults to Typed and does not show the assistant note', () => {
      const wrapper = mount(PaperCaptureComposer)
      const typed = wrapper.get('[data-testid="paper-composer-source-typed"]')
        .element as HTMLInputElement
      expect(typed.checked).toBe(true)
      expect(wrapper.find('[data-testid="paper-composer-source-note"]').exists()).toBe(false)
    })

    it('emits source TranscriptPaste once the transcript option is chosen', async () => {
      const wrapper = mount(PaperCaptureComposer)
      await wrapper.get('[data-testid="paper-composer-source-transcript"]').setValue()
      const textarea = wrapper.find('textarea')
      await textarea.setValue('Ana: ship it Friday. Bo: I will cut the branch.')
      await textarea.trigger('keydown', { key: 'Enter', metaKey: true })

      const payload = wrapper.emitted('submit')?.[0]?.[0] as { source: string; text: string }
      expect(payload.source).toBe('TranscriptPaste')
      expect(payload.text).toBe('Ana: ship it Friday. Bo: I will cut the branch.')
    })

    it('states that transcripts are sent to the assistant while transcript is selected', async () => {
      const wrapper = mount(PaperCaptureComposer)
      await wrapper.get('[data-testid="paper-composer-source-transcript"]').setValue()
      const note = wrapper.get('[data-testid="paper-composer-source-note"]').text()
      expect(note).toContain('sent to the configured assistant')
    })

    it('refuses a transcript longer than the server limit instead of sending it', async () => {
      const wrapper = mount(PaperCaptureComposer)
      await wrapper.get('[data-testid="paper-composer-source-transcript"]').setValue()
      const textarea = wrapper.find('textarea')
      await textarea.setValue('x'.repeat(200_001))
      await textarea.trigger('keydown', { key: 'Enter', metaKey: true })

      expect(wrapper.emitted('submit')).toBeUndefined()
      expect(wrapper.find('[data-testid="paper-composer-transcript-too-long"]').exists()).toBe(true)
    })

    it('accepts a typed body above the transcript limit (that guard is source-specific)', async () => {
      const wrapper = mount(PaperCaptureComposer)
      const textarea = wrapper.find('textarea')
      await textarea.setValue('x'.repeat(200_001))
      await textarea.trigger('keydown', { key: 'Enter', metaKey: true })

      expect(wrapper.emitted('submit')).toBeDefined()
      expect(wrapper.find('[data-testid="paper-composer-transcript-too-long"]').exists()).toBe(false)
    })

    it('carries the source through snapshotDraft/restoreDraft', async () => {
      const wrapper = mount(PaperCaptureComposer)
      await wrapper.get('[data-testid="paper-composer-source-transcript"]').setValue()
      await wrapper.find('textarea').setValue('half a meeting')

      const snapshot = wrapper.vm.snapshotDraft()
      expect(snapshot.source).toBe('TranscriptPaste')

      wrapper.vm.resetDraft()
      wrapper.vm.restoreDraft(snapshot)
      await wrapper.vm.$nextTick()
      const transcript = wrapper.get('[data-testid="paper-composer-source-transcript"]')
        .element as HTMLInputElement
      expect(transcript.checked).toBe(true)
    })

    it('restores a pre-GH-2141 draft with no source as Typed', async () => {
      const wrapper = mount(PaperCaptureComposer)
      await wrapper.get('[data-testid="paper-composer-source-transcript"]').setValue()
      // A stash written before this field existed.
      wrapper.vm.restoreDraft({ text: 'older draft', labels: ['keep'] })
      await wrapper.vm.$nextTick()

      const typed = wrapper.get('[data-testid="paper-composer-source-typed"]')
        .element as HTMLInputElement
      expect(typed.checked).toBe(true)
      expect(wrapper.vm.snapshotDraft().source).toBe('Typed')
    })

    it('resets the source to Typed, and leaves label reset behaviour unchanged (#2481)', async () => {
      const wrapper = mount(PaperCaptureComposer)
      await wrapper.get('[data-testid="paper-composer-source-transcript"]').setValue()
      await wrapper.find('textarea').setValue('a transcript')
      const labelInput = wrapper.find('input[type="text"]')
      await labelInput.setValue('committed')
      await labelInput.trigger('keydown', { key: 'Enter' })
      // An uncommitted label draft, exactly as #2481 left it.
      await labelInput.setValue('still-typing')

      wrapper.vm.resetDraft()
      await wrapper.vm.$nextTick()

      const snapshot = wrapper.vm.snapshotDraft()
      expect(snapshot.source).toBe('Typed')
      expect(snapshot.labels).toEqual([])
      expect((labelInput.element as HTMLInputElement).value).toBe('')
    })
  })
})
