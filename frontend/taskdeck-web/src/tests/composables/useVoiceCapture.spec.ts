import { describe, it, expect, vi, afterEach } from 'vitest'
import { useVoiceCapture } from '../../composables/useVoiceCapture'

function installMockSpeechRecognition() {
  const instance = {
    start: vi.fn(),
    stop: vi.fn(),
    continuous: false,
    interimResults: false,
    lang: '',
    onresult: null as ((event: unknown) => void) | null,
    onerror: null as ((event: unknown) => void) | null,
    onend: null as (() => void) | null,
  }

  class MockSpeechRecognition {
    start = instance.start
    stop = instance.stop
    continuous = instance.continuous
    interimResults = instance.interimResults
    lang = instance.lang
    set onresult(fn: ((event: unknown) => void) | null) { instance.onresult = fn }
    set onerror(fn: ((event: unknown) => void) | null) { instance.onerror = fn }
    set onend(fn: (() => void) | null) { instance.onend = fn }
  }

  Object.defineProperty(window, 'SpeechRecognition', {
    value: MockSpeechRecognition,
    writable: true,
    configurable: true,
  })

  return instance
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
