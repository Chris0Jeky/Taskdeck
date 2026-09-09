import { mount, flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ThinkingAudioAnswer from '../../components/thinking/ThinkingAudioAnswer.vue'
import ThinkingQuestionAnswer from '../../components/thinking/ThinkingQuestionAnswer.vue'
import { thinkingAudioApi, type ThinkingAudio } from '../../api/thinkingAudioApi'
import { thinkingApi } from '../../api/thinkingApi'
import { createSourceUploadId } from '../../utils/sourceUploadId'

vi.mock('../../api/thinkingAudioApi', () => ({ thinkingAudioApi: { get: vi.fn(), upload: vi.fn(), write: vi.fn(), confirm: vi.fn(), original: vi.fn() } }))
vi.mock('../../api/thinkingApi', () => ({ thinkingApi: { getAnswer: vi.fn(), answer: vi.fn() } }))
const props = { boardId: 'board', cardId: 'card', layerId: 'question', revision: 3, sourceReady: true }
const original: ThinkingAudio = { id: 'audio', revision: 1, captureId: 'capture', sourceAssetId: 'asset', questionHash: 'hash', fileName: 'voice.wav', mediaType: 'audio/wav', byteSize: 123,
  contentHash: 'content', originalEvidence: 'Exact question', representationId: null, confirmedMemoryId: null, writtenVersions: [] }
const written: ThinkingAudio = { ...original, revision: 2, representationId: 'written', writtenVersions: [{ id: 'written', text: 'My version', quality: 'Final', supersededById: null }] }
const confirmed: ThinkingAudio = { ...written, revision: 3, representationId: 'confirmed', confirmedMemoryId: 'memory', writtenVersions: [
  { ...written.writtenVersions[0]!, quality: 'Superseded', supersededById: 'confirmed' }, { id: 'confirmed', text: 'My version', quality: 'Verified', supersededById: null },
] }
const global = { stubs: {
  RouterLink: { template: '<a><slot /></a>' },
  AudioAnswerRecorder: { name: 'AudioAnswerRecorder', props: ['modelValue', 'disabled'], emits: ['update:modelValue', 'busy'], template: '<div>Recorder draft: {{ modelValue?.name }}</div>' },
} }
function button(wrapper: ReturnType<typeof mount>, text: string) { return wrapper.findAll('button').find(x => x.text() === text)! }
beforeEach(() => {
  vi.resetAllMocks()
  vi.mocked(thinkingAudioApi.get).mockResolvedValue(null)
  vi.mocked(thinkingApi.getAnswer).mockResolvedValue(null)
  vi.stubGlobal('URL', { createObjectURL: vi.fn(() => 'blob:original'), revokeObjectURL: vi.fn() })
})

describe('private audio answers', () => {
  it('keeps the original unanswered through a saved written version and confirms separately', async () => {
    const wrapper = mount(ThinkingAudioAnswer, { props, global }); await flushPromises()
    const file = new File(['audio'], 'voice.wav', { type: 'audio/wav' })
    wrapper.findComponent({ name: 'AudioAnswerRecorder' }).vm.$emit('update:modelValue', file); await flushPromises()
    vi.mocked(thinkingAudioApi.upload).mockResolvedValue(original)
    await button(wrapper, 'Save original privately').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('This question is still unanswered')
    expect(thinkingAudioApi.confirm).not.toHaveBeenCalled()
    await wrapper.get('textarea').setValue('My version')
    expect(button(wrapper, 'Confirm written version as my answer').attributes('disabled')).toBeDefined()
    vi.mocked(thinkingAudioApi.write).mockResolvedValue(written)
    await button(wrapper, 'Save written version').trigger('click'); await flushPromises()
    expect(thinkingAudioApi.write).toHaveBeenCalledWith('audio', 1, 'My version')
    expect(wrapper.text()).toContain('The question stays unanswered until you confirm')
    vi.mocked(thinkingAudioApi.confirm).mockResolvedValue(confirmed)
    await wrapper.get('select').setValue('unknown')
    await button(wrapper, 'Confirm written version as my answer').trigger('click'); await flushPromises()
    expect(thinkingAudioApi.confirm).toHaveBeenCalledWith('audio', 2, 3, 'written', 'unknown')
    expect(wrapper.emitted('confirmed')).toHaveLength(1)
    expect(wrapper.emitted('dirty-change')?.at(-1)).toEqual([false])
    expect(wrapper.text()).toContain('Confirmed by you and kept in private memory')
    wrapper.unmount()
  })

  it('retries an uncertain upload with the same ID and retains the file', async () => {
    const wrapper = mount(ThinkingAudioAnswer, { props, global }); await flushPromises()
    const file = new File(['audio'], 'voice.wav', { type: 'audio/wav' })
    wrapper.findComponent({ name: 'AudioAnswerRecorder' }).vm.$emit('update:modelValue', file); await flushPromises()
    vi.mocked(thinkingAudioApi.upload).mockRejectedValueOnce(new Error('lost response')).mockResolvedValueOnce(original)
    await button(wrapper, 'Save original privately').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('request could not be confirmed')
    expect(wrapper.text()).toContain('voice.wav')
    await button(wrapper, 'Save original privately').trigger('click'); await flushPromises()
    const attempts = vi.mocked(thinkingAudioApi.upload).mock.calls
    expect(attempts[0]![4]).toBe(attempts[1]![4])
    expect(attempts[0]![4]).toMatch(/^[a-f\d]{8}-[a-f\d]{4}-4[a-f\d]{3}-[89ab][a-f\d]{3}-[a-f\d]{12}$/i)
    expect(attempts[1]![5]).toBe(attempts[0]![5])
    expect(attempts[1]![5]).toMatchObject({ name: file.name, size: file.size, type: file.type })
    wrapper.unmount()
  })

  it('holds recording and pending saves busy, and ignores completion after disposal', async () => {
    vi.mocked(thinkingAudioApi.get).mockResolvedValue(original)
    const onBusy = vi.fn(); const onConfirmed = vi.fn()
    const wrapper = mount(ThinkingAudioAnswer, { props: { ...props, onBusy, onConfirmed }, global }); await flushPromises()
    await wrapper.get('textarea').setValue('My version')
    let resolve!: (value: ThinkingAudio) => void
    vi.mocked(thinkingAudioApi.write).mockReturnValue(new Promise(done => { resolve = done }))
    await button(wrapper, 'Save written version').trigger('click')
    expect(onBusy).toHaveBeenLastCalledWith(true)
    expect(wrapper.get('textarea').attributes('disabled')).toBeDefined()
    wrapper.unmount(); resolve(written); await flushPromises()
    expect(onConfirmed).not.toHaveBeenCalled()
  })

  it('retains newer typing while a reload is in flight and keeps draft on permission failure', async () => {
    vi.mocked(thinkingAudioApi.get).mockResolvedValue(original)
    const wrapper = mount(ThinkingAudioAnswer, { props, global }); await flushPromises()
    vi.mocked(thinkingAudioApi.write).mockRejectedValue({ response: { status: 403 } })
    await wrapper.get('textarea').setValue('Draft')
    await button(wrapper, 'Save written version').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('no longer available to you')
    let resolve!: (value: ThinkingAudio) => void
    vi.mocked(thinkingAudioApi.get).mockReturnValue(new Promise(done => { resolve = done }))
    await button(wrapper, 'Reload saved recording').trigger('click')
    await wrapper.get('textarea').setValue('More recent typing')
    resolve(written); await flushPromises()
    expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('More recent typing')
    expect(button(wrapper, 'Confirm written version as my answer').attributes('disabled')).toBeDefined()
    wrapper.unmount()
  })

  it('fetches the original only on request and revokes the playback URL on disposal', async () => {
    vi.mocked(thinkingAudioApi.get).mockResolvedValue(original)
    vi.mocked(thinkingAudioApi.original).mockResolvedValue(new Blob(['original'], { type: 'audio/wav' }))
    const wrapper = mount(ThinkingAudioAnswer, { props, global }); await flushPromises()
    expect(thinkingAudioApi.original).not.toHaveBeenCalled()
    await button(wrapper, 'Load original for playback or download').trigger('click'); await flushPromises()
    expect(wrapper.get('audio').attributes('src')).toBe('blob:original')
    expect(wrapper.get('a[download]').attributes('download')).toBe('voice.wav')
    wrapper.unmount(); expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:original')
  })

  it('preserves an audio draft when the private answer panel is hidden and reopened', async () => {
    const wrapper = mount(ThinkingQuestionAnswer, { props, global }); await flushPromises()
    expect(thinkingAudioApi.get).not.toHaveBeenCalled()
    await button(wrapper, 'Your private answer').trigger('click'); await flushPromises()
    await button(wrapper, 'Record or open a private audio answer').trigger('click'); await flushPromises()
    wrapper.findComponent({ name: 'AudioAnswerRecorder' }).vm.$emit('update:modelValue', new File(['audio'], 'voice.wav', { type: 'audio/wav' })); await flushPromises()
    await button(wrapper, 'Hide private answer').trigger('click'); await button(wrapper, 'Your private answer').trigger('click')
    expect(wrapper.text()).toContain('Recorder draft: voice.wav')
    expect(wrapper.emitted('dirty-change')?.at(-1)).toEqual([true])
    expect(thinkingAudioApi.get).toHaveBeenCalledOnce()
    wrapper.unmount()
  })

  it('creates server-compatible upload UUIDs without randomUUID', () => {
    vi.stubGlobal('crypto', { getRandomValues: (bytes: Uint8Array) => { bytes.fill(255); return bytes } })
    expect(createSourceUploadId()).toBe('ffffffff-ffff-4fff-bfff-ffffffffffff')
    vi.unstubAllGlobals()
  })
})
