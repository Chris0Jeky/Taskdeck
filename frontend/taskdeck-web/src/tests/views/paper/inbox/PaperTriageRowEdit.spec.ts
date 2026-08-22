import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PaperTriageRowEdit from '../../../../views/paper/inbox/PaperTriageRowEdit.vue'
import type { CaptureItem } from '../../../../types/capture'

/**
 * Pre-triage capture edit in the Paper inbox (GH-1951).
 *
 * The three paths the issue asks for — editable, save failure, not editable —
 * plus the two that decide whether the port is honest: a detail fetch that
 * fails, and a Save that is off for a stated reason rather than silently.
 */
const mockCaptureStore = {
  fetchDetail: vi.fn<(...args: unknown[]) => Promise<CaptureItem>>(),
  updateSuggestion: vi.fn<(...args: unknown[]) => Promise<CaptureItem>>(),
}

vi.mock('../../../../store/captureStore', () => ({
  useCaptureStore: () => mockCaptureStore,
}))

function makeDetail(overrides: Partial<CaptureItem> = {}): CaptureItem {
  return {
    id: 'capture-1',
    userId: 'user-1',
    boardId: 'board-alpha',
    status: 'New',
    source: 'Typed',
    textExcerpt: 'Ship teh releaes notes…',
    rawText: 'Ship teh releaes notes before Friday',
    createdAt: new Date('2026-04-25T09:42:00Z').toISOString(),
    processedAt: null,
    retryCount: 0,
    provenance: null,
    canEditSuggestion: true,
    ...overrides,
  }
}

async function mountEditor(itemId = 'capture-1', mutationInFlight = false) {
  const wrapper = mount(PaperTriageRowEdit, { props: { itemId, mutationInFlight } })
  await flushPromises()
  return wrapper
}

describe('PaperTriageRowEdit', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockCaptureStore.fetchDetail.mockResolvedValue(makeDetail())
    mockCaptureStore.updateSuggestion.mockImplementation(async (_itemId, dto) =>
      makeDetail({ rawText: (dto as { text: string }).text, textExcerpt: (dto as { text: string }).text }),
    )
  })

  // ── the editable path ──────────────────────────────────────────────────────

  it('loads the untruncated text rather than offering the row excerpt', async () => {
    const wrapper = await mountEditor()

    // Editing the excerpt would SAVE the truncation, so the full text has to be
    // fetched — and forced, because a cached detail can predate a triage attempt.
    expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith(
      'capture-1',
      expect.objectContaining({ forceRefresh: true }),
    )
    const textarea = wrapper.get<HTMLTextAreaElement>('[data-testid="capture-edit-textarea"]')
    expect(textarea.element.value).toBe('Ship teh releaes notes before Friday')
    expect(textarea.element.value).not.toBe('Ship teh releaes notes…')
  })

  it('saves the edited text through the existing update path and closes', async () => {
    const wrapper = await mountEditor()

    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue('Ship the release notes before Friday')
    await wrapper.get('button[data-action="edit-save"]').trigger('click')
    await flushPromises()

    expect(mockCaptureStore.updateSuggestion).toHaveBeenCalledTimes(1)
    expect(mockCaptureStore.updateSuggestion).toHaveBeenCalledWith('capture-1', {
      text: 'Ship the release notes before Friday',
    })
    expect(wrapper.emitted('saved')?.[0]).toEqual(['capture-1'])
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('sends text only, so the server keeps an existing title hint', async () => {
    // The backend writes `TitleHint ?? current`: a request that carried an
    // explicit null would CLEAR a hint the user never touched. Asserting the
    // key is absent is the only way that stays true.
    const wrapper = await mountEditor()
    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue('corrected text')
    await wrapper.get('button[data-action="edit-save"]').trigger('click')
    await flushPromises()

    const dto = mockCaptureStore.updateSuggestion.mock.calls[0][1] as Record<string, unknown>
    expect(Object.keys(dto)).toEqual(['text'])
  })

  it('shows the saving state and holds Cancel shut while the write is in flight', async () => {
    // Definite-assignment rather than a `| null` union: the resolver is always
    // set by the executor below, and the union narrows to `never` at the call.
    let resolveSave!: (value: CaptureItem) => void
    mockCaptureStore.updateSuggestion.mockImplementation(
      () => new Promise<CaptureItem>((resolve) => {
        resolveSave = resolve
      }),
    )
    const wrapper = await mountEditor()
    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue('corrected text')
    await wrapper.get('button[data-action="edit-save"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.get('button[data-action="edit-save"]').text()).toContain('Saving')
    // Closing mid-write would leave the user without the outcome of a write
    // that is still going to land.
    expect(wrapper.get('button[data-action="edit-cancel"]').attributes('disabled')).toBeDefined()

    resolveSave(makeDetail({ rawText: 'corrected text' }))
    await flushPromises()
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  // ── the save failure path ──────────────────────────────────────────────────

  it('keeps the draft and states the reason when the save fails', async () => {
    mockCaptureStore.updateSuggestion.mockRejectedValue(new Error('network'))
    const wrapper = await mountEditor()

    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue('corrected text')
    await wrapper.get('button[data-action="edit-save"]').trigger('click')
    await flushPromises()

    // Closing on failure would read as a success, and dropping the draft would
    // cost the user the text they just wrote.
    expect(wrapper.emitted('close')).toBeUndefined()
    expect(wrapper.emitted('saved')).toBeUndefined()
    expect(wrapper.get<HTMLTextAreaElement>('[data-testid="capture-edit-textarea"]').element.value)
      .toBe('corrected text')
    expect(wrapper.get('[data-testid="capture-edit-save-error"]').text()).toContain('The text was not saved')
  })

  // ── the not-editable path ──────────────────────────────────────────────────

  it('explains instead of offering a textarea when the server refuses edits', async () => {
    mockCaptureStore.fetchDetail.mockResolvedValue(makeDetail({ canEditSuggestion: false }))
    const wrapper = await mountEditor()

    expect(wrapper.find('[data-testid="capture-edit-textarea"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="capture-edit-blocked"]').text())
      .toContain("This capture's text can't be edited")
    expect(wrapper.attributes('data-edit-state')).toBe('blocked')
  })

  it('treats an absent canEditSuggestion as not editable', async () => {
    // An API older than the flag omits it. Guessing "editable" would offer a
    // Save the server answers with a 409.
    mockCaptureStore.fetchDetail.mockResolvedValue(makeDetail({ canEditSuggestion: undefined }))
    const wrapper = await mountEditor()

    expect(wrapper.find('[data-testid="capture-edit-textarea"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="capture-edit-blocked"]').exists()).toBe(true)
  })

  // ── the load failure path ──────────────────────────────────────────────────

  it('offers a retry when the detail fetch fails, and recovers on success', async () => {
    mockCaptureStore.fetchDetail.mockRejectedValueOnce(new Error('network'))
    const wrapper = await mountEditor()

    expect(wrapper.get('[data-testid="capture-edit-load-error"]').text())
      .toContain('The full capture text did not load')
    expect(wrapper.find('[data-testid="capture-edit-textarea"]').exists()).toBe(false)

    await wrapper.get('button[data-action="edit-retry"]').trigger('click')
    await flushPromises()

    expect(wrapper.get<HTMLTextAreaElement>('[data-testid="capture-edit-textarea"]').element.value)
      .toBe('Ship teh releaes notes before Friday')
  })

  // ── Save is never enabled-and-silent ───────────────────────────────────────

  it('states why Save is off when nothing has changed, and writes nothing if clicked', async () => {
    const wrapper = await mountEditor()

    const reason = wrapper.get('[data-testid="capture-edit-save-reason"]')
    expect(reason.attributes('data-reason')).toBe('unchanged')
    expect(reason.text()).toContain('Nothing has changed yet')
    expect(wrapper.get('button[data-action="edit-save"]').attributes('disabled')).toBeDefined()

    // The guard behind the disabled button, not just the binding.
    await wrapper.get('button[data-action="edit-save"]').trigger('click')
    await flushPromises()
    expect(mockCaptureStore.updateSuggestion).not.toHaveBeenCalled()
  })

  it('states why Save is off when the text is emptied, and writes nothing if clicked', async () => {
    const wrapper = await mountEditor()
    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue('   ')

    const reason = wrapper.get('[data-testid="capture-edit-save-reason"]')
    expect(reason.attributes('data-reason')).toBe('empty')
    expect(reason.text()).toContain("Text can't be empty")

    await wrapper.get('button[data-action="edit-save"]').trigger('click')
    await flushPromises()
    expect(mockCaptureStore.updateSuggestion).not.toHaveBeenCalled()
  })

  it('holds Save shut while another capture mutation owns the shared busy slot', async () => {
    // `updateSuggestion` takes the same single `actionBusyItemId` Accept and
    // Reject take. Saving into an occupied slot overwrites the other row's
    // in-flight state and releases the lock when THIS write ends, not that one.
    const wrapper = await mountEditor()
    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue('corrected text')
    expect(wrapper.get('button[data-action="edit-save"]').attributes('disabled')).toBeUndefined()

    await wrapper.setProps({ mutationInFlight: true })

    const reason = wrapper.get('[data-testid="capture-edit-save-reason"]')
    expect(reason.attributes('data-reason')).toBe('busyElsewhere')
    expect(reason.text()).toContain('Another capture action is still finishing')
    const save = wrapper.get('button[data-action="edit-save"]')
    expect(save.attributes('disabled')).toBeDefined()
    expect(save.attributes('aria-describedby')).toBe(reason.attributes('id'))

    // The guard behind the disabled button, not just the binding.
    await save.trigger('click')
    await flushPromises()
    expect(mockCaptureStore.updateSuggestion).not.toHaveBeenCalled()

    // And it comes back, rather than stranding a draft the user can never save.
    await wrapper.setProps({ mutationInFlight: false })
    expect(wrapper.get('button[data-action="edit-save"]').attributes('disabled')).toBeUndefined()
  })

  it('drops a stale save failure as soon as the draft changes again', async () => {
    // The failure describes a write of text that no longer exists. Left up, it
    // reads as a verdict on what is in the textarea now.
    mockCaptureStore.updateSuggestion.mockRejectedValue(new Error('network'))
    const wrapper = await mountEditor()

    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue('corrected text')
    const save = wrapper.get('button[data-action="edit-save"]')
    await save.trigger('click')
    await flushPromises()

    const error = wrapper.get('[data-testid="capture-edit-save-error"]')
    // The error is the node that renders, so it must be the node Save names —
    // the reason id points at an element the v-if branch never produced.
    expect(wrapper.get('button[data-action="edit-save"]').attributes('aria-describedby'))
      .toBe(error.attributes('id'))
    expect(wrapper.find('[data-testid="capture-edit-save-reason"]').exists()).toBe(false)

    // Reverting to the original text: the failure is gone and the honest reason
    // for Save being off is now "nothing has changed".
    await wrapper.get('[data-testid="capture-edit-textarea"]')
      .setValue('Ship teh releaes notes before Friday')

    expect(wrapper.find('[data-testid="capture-edit-save-error"]').exists()).toBe(false)
    const reason = wrapper.get('[data-testid="capture-edit-save-reason"]')
    expect(reason.attributes('data-reason')).toBe('unchanged')
    expect(wrapper.get('button[data-action="edit-save"]').attributes('aria-describedby'))
      .toBe(reason.attributes('id'))
  })

  it('cancels without writing anything', async () => {
    const wrapper = await mountEditor()
    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue('abandoned edit')
    await wrapper.get('button[data-action="edit-cancel"]').trigger('click')

    expect(mockCaptureStore.updateSuggestion).not.toHaveBeenCalled()
    expect(wrapper.emitted('close')).toHaveLength(1)
  })
})
