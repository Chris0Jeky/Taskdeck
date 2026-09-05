import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick, reactive } from 'vue'
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
    const textarea = wrapper.get('[data-testid="paper-composer-body"]')

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

  // --- field chrome i18n (#1871) -------------------------------------------

  /**
   * The eyebrows, accessible names and placeholders of the four Composer
   * fields, in the file's own locale idiom (the `it.each` above, from #2654).
   *
   * The English case is the regression guard, and since the selector-to-testid
   * migration in this PR it can assert the names the a11y rule wants rather
   * than the pre-extraction English the old selectors froze: every accessible
   * name leads with its visible eyebrow and then says what the control does
   * (WCAG 2.5.3, the PR #2675 pattern). The Italian case is the one that proves
   * the catalogs reach the DOM at all — every assertion in it fails on the
   * pre-#1871 component, which hardcoded English in the template regardless of
   * locale.
   *
   * Selectors are locale-independent on purpose (testid and class, never the
   * aria-label being asserted): a selector that reads the string under test
   * cannot fail when that string is wrong, it just finds nothing.
   */
  function fieldChrome(wrapper: ReturnType<typeof mount>) {
    return {
      eyebrows: wrapper.findAll('.paper-composer__label .tk-eyebrow').map((node) => node.text()),
      body: wrapper.get('[data-testid="paper-composer-body"]'),
      board: wrapper.get('[data-testid="paper-composer-board"]'),
      label: wrapper.get('[data-testid="paper-composer-label-input"]'),
      due: wrapper.get('[data-testid="paper-composer-due"]'),
      attachments: wrapper.get('[data-testid="paper-composer-attachments-unavailable"]'),
    }
  }

  it('renders the field chrome in English on the default locale', () => {
    const wrapper = mount(PaperCaptureComposer)
    const chrome = fieldChrome(wrapper)

    expect(chrome.eyebrows).toEqual(['Body', 'Board', 'Labels', 'Due (optional)'])
    expect(chrome.body.attributes('aria-label')).toBe('Body: write the text of this capture')
    expect(chrome.body.attributes('placeholder')).toBe('The thought, in plain language…')
    expect(chrome.board.attributes('aria-label')).toBe(
      'Board: choose which board this capture is linked to for triage',
    )
    expect(chrome.label.attributes('aria-label')).toBe(
      'Labels: type a label and press Enter to add it',
    )
    expect(chrome.label.attributes('placeholder')).toBe('add and press Enter')
    expect(chrome.due.attributes('aria-label')).toBe(
      'Due (optional): set a due date for this capture',
    )
    expect(chrome.attachments.text()).toBe('Attachments are not saved with captures yet.')
  })

  /**
   * WCAG 2.5.3 label-in-name, asserted as a RELATION rather than as four more
   * literals: each accessible name must START with the eyebrow rendered above
   * its control. Written this way it keeps holding when the copy is reworded
   * and it fails on the pre-rewrite names, where `Add label` did not contain
   * the visible `Labels` and `Due date` did not contain `Due (optional)`.
   *
   * It runs in EVERY supported locale, both halves of each pair read off the
   * DOM. The rule is a property of the catalog, not of English, and the
   * es/it docblocks tell a translator this test holds them to it — Spanish
   * composer chrome has no other assertion anywhere, so on the default locale
   * alone that promise would have been empty.
   */
  it.each(['en', 'it', 'es'] as const)(
    'starts every field accessible name with the visible eyebrow above it in %s',
    (locale) => {
      const previousLocale = i18n.global.locale.value
      try {
        i18n.global.locale.value = locale as SupportedLocale
        const wrapper = mount(PaperCaptureComposer)
        const chrome = fieldChrome(wrapper)
        const [bodyEyebrow, boardEyebrow, labelsEyebrow, dueEyebrow] = chrome.eyebrows

        const named = [
          [bodyEyebrow, chrome.body.attributes('aria-label')],
          [boardEyebrow, chrome.board.attributes('aria-label')],
          [labelsEyebrow, chrome.label.attributes('aria-label')],
          [dueEyebrow, chrome.due.attributes('aria-label')],
        ] as const

        // Reported as pairs so a failure names the eyebrow AND the name that
        // broke the rule, instead of four indistinguishable `false`s.
        expect(named.filter(([eyebrow, name]) => !name?.startsWith(`${eyebrow}: `))).toEqual([])
      } finally {
        i18n.global.locale.value = previousLocale
      }
    },
  )

  it('re-renders the field chrome in Italian when the locale switches', () => {
    const previousLocale = i18n.global.locale.value
    try {
      i18n.global.locale.value = 'it' as SupportedLocale
      const wrapper = mount(PaperCaptureComposer)
      const chrome = fieldChrome(wrapper)

      expect(chrome.eyebrows).toEqual(['Testo', 'Bacheca', 'Etichette', 'Scadenza (facoltativa)'])
      expect(chrome.body.attributes('aria-label')).toBe(
        'Testo: scrivi il contenuto di questa cattura',
      )
      expect(chrome.body.attributes('placeholder')).toBe('Il pensiero, in parole semplici…')
      expect(chrome.board.attributes('aria-label')).toBe(
        'Bacheca: scegli a quale bacheca collegare questa cattura per il triage',
      )
      expect(chrome.label.attributes('aria-label')).toBe(
        'Etichette: scrivi un’etichetta e premi Enter per aggiungerla',
      )
      expect(chrome.label.attributes('placeholder')).toBe('aggiungi e premi Enter')
      expect(chrome.due.attributes('aria-label')).toBe(
        'Scadenza (facoltativa): scegli quando scade questa cattura',
      )
      expect(chrome.attachments.text()).toBe(
        'Gli allegati non vengono ancora salvati con le catture.',
      )
    } finally {
      i18n.global.locale.value = previousLocale
    }
  })

  /**
   * The case above sets the locale BEFORE mounting, so it proves first render
   * only — a component that read `t()` once into a non-reactive snapshot would
   * still pass it. This one mounts in English and switches AFTER, so the
   * eyebrows and an accessible name have to change on an already-rendered
   * component or the assertion fails.
   */
  it('re-renders the field chrome when the locale switches after mount', async () => {
    const previousLocale = i18n.global.locale.value
    try {
      i18n.global.locale.value = 'en' as SupportedLocale
      const wrapper = mount(PaperCaptureComposer)
      expect(fieldChrome(wrapper).eyebrows).toEqual(['Body', 'Board', 'Labels', 'Due (optional)'])

      i18n.global.locale.value = 'it' as SupportedLocale
      await nextTick()

      const chrome = fieldChrome(wrapper)
      expect(chrome.eyebrows).toEqual(['Testo', 'Bacheca', 'Etichette', 'Scadenza (facoltativa)'])
      expect(chrome.body.attributes('aria-label')).toBe(
        'Testo: scrivi il contenuto di questa cattura',
      )
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
    expect(wrapper.find('[data-testid="paper-composer-board"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="paper-composer-label-input"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="paper-composer-due"]').attributes('disabled')).toBeDefined()

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

    const options = wrapper.findAll('[data-testid="paper-composer-board"] option')
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
      .findAll('[data-testid="paper-composer-board"] option')
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
      .findAll('[data-testid="paper-composer-board"] option')
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
