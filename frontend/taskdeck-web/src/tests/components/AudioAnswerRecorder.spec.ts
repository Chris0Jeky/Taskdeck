import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import AudioAnswerRecorder from '../../components/thinking/AudioAnswerRecorder.vue'

class Recorder {
  static isTypeSupported = () => true
  static current: Recorder
  state = 'inactive'
  mimeType = 'audio/webm;codecs=opus'
  ondataavailable: ((event: { data: Blob }) => void) | null = null
  onstop: (() => void) | null = null
  onerror: (() => void) | null = null
  constructor() { Recorder.current = this }
  start() { this.state = 'recording' }
  stop() {
    this.state = 'inactive'
    this.ondataavailable?.({ data: new Blob(['synthetic audio'], { type: this.mimeType }) })
    this.onstop?.()
  }
}
const getUserMedia = vi.fn()
const stopTrack = vi.fn()
const stream = { getTracks: () => [{ stop: stopTrack }] }
function button(wrapper: ReturnType<typeof mount>, name: string) { return wrapper.findAll('button').find(item => item.text() === name)! }
describe('audio answer draft capture', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('MediaRecorder', Recorder)
    vi.stubGlobal('navigator', { mediaDevices: { getUserMedia } })
    getUserMedia.mockResolvedValue(stream)
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:audio-draft')
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
  })
  afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks(); vi.unstubAllGlobals() })
  it('starts only on explicit action and retains an original after stop without interpreting it', async () => {
    const wrapper = mount(AudioAnswerRecorder, { props: { modelValue: null } })
    expect(getUserMedia).not.toHaveBeenCalled()
    await button(wrapper, 'Record audio').trigger('click'); await flushPromises()
    expect(getUserMedia).toHaveBeenCalledWith({ audio: true })
    await button(wrapper, 'Stop recording').trigger('click')
    const file = wrapper.emitted('update:modelValue')![0]![0] as File
    expect(file.size).toBeGreaterThan(0)
    expect(file.type).toContain('audio/webm')
    expect(stopTrack).toHaveBeenCalledTimes(1)
    await wrapper.setProps({ modelValue: file })
    expect(wrapper.get('audio').attributes('src')).toBe('blob:audio-draft')
    expect(wrapper.text()).toContain('not transcribed or understood')
    wrapper.unmount()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:audio-draft')
  })
  it('stops acquired tracks when permission arrives after cancellation or unmount', async () => {
    let resolve!: (value: typeof stream) => void
    getUserMedia.mockReturnValueOnce(new Promise(done => { resolve = done }))
    const wrapper = mount(AudioAnswerRecorder, { props: { modelValue: null } })
    await button(wrapper, 'Record audio').trigger('click')
    await button(wrapper, 'Cancel recording').trigger('click')
    wrapper.unmount()
    resolve(stream); await flushPromises()
    expect(stopTrack).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })
  it('keeps the prior draft after denied permission and offers file input', async () => {
    getUserMedia.mockRejectedValueOnce(new Error('denied'))
    const file = new File(['prior'], 'prior.wav', { type: 'audio/wav' })
    const wrapper = mount(AudioAnswerRecorder, { props: { modelValue: file } })
    await button(wrapper, 'Record audio').trigger('click'); await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('permission')
    expect(wrapper.find('input[type="file"]').exists()).toBe(true)
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    wrapper.unmount()
  })
  it('rejects oversized and non-audio files without replacing the draft', async () => {
    const wrapper = mount(AudioAnswerRecorder, { props: { modelValue: null } })
    const input = wrapper.get('input[type="file"]')
    for (const file of [new File(['bad'], 'note.html', { type: 'text/html' }), new File([new Uint8Array(2 * 1024 * 1024 + 1)], 'long.wav', { type: 'audio/wav' })]) {
      Object.defineProperty(input.element, 'files', { value: [file], configurable: true })
      await input.trigger('change')
      expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    }
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    wrapper.unmount()
  })
  it('stops after sixty seconds and tears down an active recording on unmount', async () => {
    vi.useFakeTimers()
    const changed = vi.fn()
    const wrapper = mount(AudioAnswerRecorder, { props: { modelValue: null, 'onUpdate:modelValue': changed } })
    await button(wrapper, 'Record audio').trigger('click'); await flushPromises()
    await vi.advanceTimersByTimeAsync(60000)
    expect(changed).toHaveBeenCalledTimes(1)
    await button(wrapper, 'Record audio').trigger('click'); await flushPromises()
    wrapper.unmount()
    expect(stopTrack).toHaveBeenCalledTimes(2)
    expect(changed).toHaveBeenCalledTimes(1)
  })
})
