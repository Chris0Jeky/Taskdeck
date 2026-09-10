import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AudioTranscriptionPanel from '../../components/thinking/AudioTranscriptionPanel.vue'
import { audioTranscriptionApi, type TranscriptionStatus, type TranscriptionReceipt } from '../../api/audioTranscriptionApi'
import { useSessionStore } from '../../store/sessionStore'
import type { ThinkingAudio } from '../../api/thinkingAudioApi'

vi.mock('../../api/audioTranscriptionApi', () => ({ audioTranscriptionApi: { status: vi.fn(), start: vi.fn() } }))
const audio: ThinkingAudio = { id: 'recording', revision: 1, captureId: 'capture', sourceAssetId: 'source', questionHash: 'question',
  fileName: 'voice.wav', mediaType: 'audio/wav', byteSize: 100, contentHash: 'hash', originalEvidence: 'Question evidence',
  representationId: null, confirmedMemoryId: null, writtenVersions: [] }
const configuration = { enabled: true, provider: 'speech', model: 'fixture', origin: 'https://speech.example', configurationHash: 'a'.repeat(64),
  timeoutSeconds: 60, dailyAttempts: 5, dailyInputBytes: 10000 }
const fresh = (): TranscriptionStatus => ({ configuration: { ...configuration }, attemptsUsedToday: 0, inputBytesUsedToday: 0, attempts: [] })
const receipt = (requestId: string, state = 'Completed'): TranscriptionReceipt => ({ id: 'attempt', requestId, audioAnswerId: audio.id, state,
  provider: 'speech', model: 'fixture', configurationHash: configuration.configurationHash, startedAt: '2026-09-10T07:00:00Z', deadline: '2026-09-10T07:02:00Z',
  finishedAt: state === 'Running' ? null : '2026-09-10T07:00:10Z', failureCode: null, representationId: state === 'Completed' ? 'candidate' : null,
  text: state === 'Completed' ? 'Provisional words' : null })
function button(wrapper: ReturnType<typeof mount>, text: string) { return wrapper.findAll('button').find(x => x.text() === text)! }
beforeEach(() => {
  vi.resetAllMocks(); setActivePinia(createPinia())
  const session = useSessionStore(); session.userId = 'owner'; session.token = 'synthetic-token'
  vi.mocked(audioTranscriptionApi.status).mockImplementation(async () => fresh())
  vi.mocked(audioTranscriptionApi.start).mockImplementation(async (_, request) => receipt(request.requestId))
})

describe('explicit transcription', () => {
  it('loads only on request, requires destination consent and emits an unconfirmed draft', async () => {
    const wrapper = mount(AudioTranscriptionPanel, { props: { audio, canAdopt: true } })
    expect(audioTranscriptionApi.status).not.toHaveBeenCalled(); expect(audioTranscriptionApi.start).not.toHaveBeenCalled()
    await button(wrapper, 'Transcription options and receipts').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('https://speech.example'); expect(wrapper.text()).toContain('fixture')
    expect(button(wrapper, 'Request a transcript').attributes('disabled')).toBeDefined()
    await wrapper.get('input').setValue(true); await button(wrapper, 'Request a transcript').trigger('click'); await flushPromises()
    expect(audioTranscriptionApi.start).toHaveBeenCalledWith('recording', expect.objectContaining({ expectedRevision: 1, configurationHash: configuration.configurationHash }))
    expect(wrapper.text()).toContain('Provisional transcript'); expect(wrapper.emitted('adopt')).toBeUndefined()
    await button(wrapper, 'Use this transcript as a written draft').trigger('click')
    expect(wrapper.emitted('adopt')).toEqual([[{ id: 'candidate', text: 'Provisional words' }]])
    expect(button(wrapper, 'Request a transcript').attributes('disabled')).toBeDefined()
  })

  it('recovers an uncertain request with the same ID and never retries automatically', async () => {
    vi.mocked(audioTranscriptionApi.start).mockRejectedValueOnce(new Error('lost response'))
    const wrapper = mount(AudioTranscriptionPanel, { props: { audio } })
    await button(wrapper, 'Transcription options and receipts').trigger('click'); await flushPromises()
    await wrapper.get('input').setValue(true); await button(wrapper, 'Request a transcript').trigger('click'); await flushPromises()
    const originalRequest = vi.mocked(audioTranscriptionApi.start).mock.calls[0]![1]
    expect(audioTranscriptionApi.start).toHaveBeenCalledTimes(1)
    expect(button(wrapper, 'Retry the same transcription request').attributes('disabled')).toBeDefined()
    await button(wrapper, 'Refresh transcription receipts').trigger('click'); await flushPromises()
    await wrapper.get('input').setValue(true); await button(wrapper, 'Retry the same transcription request').trigger('click'); await flushPromises()
    expect(audioTranscriptionApi.start).toHaveBeenLastCalledWith('recording', originalRequest)
    expect(audioTranscriptionApi.start).toHaveBeenCalledTimes(2)
  })

  it('finds a saved running receipt after a lost response and suppresses a second dispatch', async () => {
    vi.mocked(audioTranscriptionApi.start).mockRejectedValueOnce(new Error('lost response'))
    const wrapper = mount(AudioTranscriptionPanel, { props: { audio } })
    await button(wrapper, 'Transcription options and receipts').trigger('click'); await flushPromises()
    await wrapper.get('input').setValue(true); await button(wrapper, 'Request a transcript').trigger('click'); await flushPromises()
    const request = vi.mocked(audioTranscriptionApi.start).mock.calls[0]![1]
    vi.mocked(audioTranscriptionApi.status).mockResolvedValue({ ...fresh(), attemptsUsedToday: 1, attempts: [receipt(request.requestId, 'Running')] })
    await button(wrapper, 'Refresh transcription receipts').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('Processing is underway')
    expect(button(wrapper, 'Request a transcript').attributes('disabled')).toBeDefined()
    expect(audioTranscriptionApi.start).toHaveBeenCalledTimes(1)
  })

  it.each(['account', 'recording'])('discards late private text after a %s change', async change => {
    let complete!: (value: TranscriptionReceipt) => void
    vi.mocked(audioTranscriptionApi.start).mockReturnValue(new Promise(resolve => { complete = resolve }))
    const wrapper = mount(AudioTranscriptionPanel, { props: { audio, canAdopt: true } })
    await button(wrapper, 'Transcription options and receipts').trigger('click'); await flushPromises()
    await wrapper.get('input').setValue(true); await button(wrapper, 'Request a transcript').trigger('click')
    const request = vi.mocked(audioTranscriptionApi.start).mock.calls[0]![1]
    if (change === 'account') useSessionStore().userId = 'another-owner'
    else await wrapper.setProps({ audio: { ...audio, id: 'new-recording' } })
    await flushPromises(); complete(receipt(request.requestId)); await flushPromises()
    expect(wrapper.text()).not.toContain('Provisional words'); expect(wrapper.emitted('adopt')).toBeUndefined()
    expect(wrapper.emitted('busy')?.at(-1)).toEqual([false])
  })

  it('keeps disabled-provider receipts readable and prevents adoption into a guarded answer', async () => {
    vi.mocked(audioTranscriptionApi.status).mockResolvedValue({ ...fresh(), configuration: { ...configuration, enabled: false }, attempts: [receipt('old')] })
    const wrapper = mount(AudioTranscriptionPanel, { props: { audio, canAdopt: false } })
    await button(wrapper, 'Transcription options and receipts').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('not configured or is paused'); expect(wrapper.text()).toContain('Provisional words')
    expect(wrapper.find('input').exists()).toBe(false); expect(button(wrapper, 'Use this transcript as a written draft')).toBeUndefined()
  })
})
