import { describe, it, expect, vi, afterEach } from 'vitest'
import { useVoiceCapture } from '../../composables/useVoiceCapture'

interface MockRecognitionInstance {
  start: ReturnType<typeof vi.fn>
  stop: ReturnType<typeof vi.fn>
  continuous: boolean
  interimResults: boolean
  lang: string
  onresult: ((event: unknown) => void) | null
  onerror: ((event: unknown) => void) | null
  onend: (() => void) | null
}

function installMockSpeechRecognition() {
  const instances: MockRecognitionInstance[] = []

  function createInstance() {
    return {
      start: vi.fn(),
      stop: vi.fn(),
      continuous: false,
      interimResults: false,
      lang: '',
      onresult: null as ((event: unknown) => void) | null,
      onerror: null as ((event: unknown) => void) | null,
      onend: null as (() => void) | null,
    }
  }

  const fallback = createInstance()
  const first = () => instances[0] ?? fallback
  const api = {
    instances,
    get start() { return first().start },
    get stop() { return first().stop },
    get continuous() { return first().continuous },
    get interimResults() { return first().interimResults },
    get lang() { return first().lang },
    get onresult() { return first().onresult },
    get onerror() { return first().onerror },
    get onend() { return first().onend },
  }

  class MockSpeechRecognition {
    private readonly instance: MockRecognitionInstance

    constructor() {
      this.instance = createInstance()
      instances.push(this.instance)
    }

    start() { this.instance.start() }
    stop() { this.instance.stop() }
    get continuous() { return this.instance.continuous }
    set continuous(value: boolean) { this.instance.continuous = value }
    get interimResults() { return this.instance.interimResults }
    set interimResults(value: boolean) { this.instance.interimResults = value }
    get lang() { return this.instance.lang }
    set lang(value: string) { this.instance.lang = value }
    set onresult(fn: ((event: unknown) => void) | null) { this.instance.onresult = fn }
    set onerror(fn: ((event: unknown) => void) | null) { this.instance.onerror = fn }
    set onend(fn: (() => void) | null) { this.instance.onend = fn }
  }

  Object.defineProperty(window, 'SpeechRecognition', {
    value: MockSpeechRecognition,
    writable: true,
    configurable: true,
  })

  return api
}

function removeSpeechRecognition() {
  // @ts-expect-error cleanup test-only property
  delete window.SpeechRecognition
  // @ts-expect-error cleanup test-only property
  delete window.webkitSpeechRecognition
}

describe('useVoiceCapture', () => {
  afterEach(() => {
    removeSpeechRecognition()
  })

  it('reports unsupported when SpeechRecognition is absent', () => {
    const { isSupported, unsupportedReason } = useVoiceCapture()
    expect(isSupported.value).toBe(false)
    expect(unsupportedReason.value).toContain('not available')
  })

  it('rejects webkitSpeechRecognition with privacy reason', () => {
    Object.defineProperty(window, 'webkitSpeechRecognition', {
      value: class {},
      writable: true,
      configurable: true,
    })

    const { isSupported, isWebkitOnly, unsupportedReason } = useVoiceCapture()

    expect(isSupported.value).toBe(false)
    expect(isWebkitOnly.value).toBe(true)
    expect(unsupportedReason.value).toContain('Google servers')
  })

  it('returns error when starting on unsupported browser', () => {
    const { startListening, status, errorMessage } = useVoiceCapture()

    const result = startListening()

    expect(result).toBe(false)
    expect(status.value).toBe('error')
    expect(errorMessage.value).toBeTruthy()
  })

  it('starts listening when SpeechRecognition is available', () => {
    const instance = installMockSpeechRecognition()

    const { startListening, status, isSupported } = useVoiceCapture()

    expect(isSupported.value).toBe(true)

    const result = startListening()

    expect(result).toBe(true)
    expect(status.value).toBe('listening')
    expect(instance.start).toHaveBeenCalled()
  })

  it('does not start overlapping recognition sessions', () => {
    const instance = installMockSpeechRecognition()

    const { startListening, status } = useVoiceCapture()

    expect(startListening()).toBe(true)
    expect(startListening()).toBe(false)

    expect(status.value).toBe('listening')
    expect(instance.start).toHaveBeenCalledTimes(1)
  })

  it('captures transcript from recognition result', () => {
    const instance = installMockSpeechRecognition()

    const { startListening, transcript } = useVoiceCapture()
    startListening()

    instance.onresult!({
      results: [
        { 0: { transcript: 'add a task for code review' }, isFinal: true, length: 1 },
      ],
    })

    expect(transcript.value).toBe('add a task for code review')
  })

  it('handles recognition errors gracefully', () => {
    const instance = installMockSpeechRecognition()

    const { startListening, status, errorMessage } = useVoiceCapture()
    startListening()

    instance.onerror!({ error: 'not-allowed' })

    expect(status.value).toBe('error')
    expect(errorMessage.value).toContain('not-allowed')
  })

  it('stops recognition and resets status', () => {
    const instance = installMockSpeechRecognition()

    const { startListening, stopListening, status } = useVoiceCapture()
    startListening()
    expect(status.value).toBe('listening')

    stopListening()
    expect(status.value).toBe('idle')
    expect(instance.stop).toHaveBeenCalled()
  })

  it('preserves error state when onend fires after onerror', () => {
    const instance = installMockSpeechRecognition()

    const { startListening, status, errorMessage } = useVoiceCapture()
    startListening()

    instance.onerror!({ error: 'not-allowed' })
    expect(status.value).toBe('error')

    instance.onend!()
    expect(status.value).toBe('error')
    expect(errorMessage.value).toContain('not-allowed')
  })

  it('allows a new recognition session after the previous one ends', () => {
    const instance = installMockSpeechRecognition()

    const { startListening, status } = useVoiceCapture()

    expect(startListening()).toBe(true)
    instance.onend!()
    expect(status.value).toBe('idle')
    expect(startListening()).toBe(true)

    expect(instance.instances).toHaveLength(2)
    expect(instance.instances[0].start).toHaveBeenCalledTimes(1)
    expect(instance.instances[1].start).toHaveBeenCalledTimes(1)
  })

  it('ignores stale onend callbacks from a stopped previous session', () => {
    const instance = installMockSpeechRecognition()

    const { startListening, stopListening, status } = useVoiceCapture()

    expect(startListening()).toBe(true)
    const firstRecognition = instance.instances[0]
    stopListening()
    expect(startListening()).toBe(true)

    firstRecognition.onend!()
    expect(status.value).toBe('listening')

    instance.instances[1].onend!()
    expect(status.value).toBe('idle')
  })

  it('requires consent acknowledgment when requireConsent is true', () => {
    installMockSpeechRecognition()

    const { startListening, status, errorMessage, consentAcknowledged, acknowledgeConsent } =
      useVoiceCapture({ requireConsent: true })

    expect(consentAcknowledged.value).toBe(false)

    const result = startListening()
    expect(result).toBe(false)
    expect(status.value).toBe('error')
    expect(errorMessage.value).toContain('Consent required')

    acknowledgeConsent()
    expect(consentAcknowledged.value).toBe(true)

    const result2 = startListening()
    expect(result2).toBe(true)
    expect(status.value).toBe('listening')
  })
})
