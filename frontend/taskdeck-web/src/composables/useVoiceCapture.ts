import { ref, computed, onUnmounted } from 'vue'

export type VoiceCaptureStatus = 'idle' | 'listening' | 'error'

const UNSUPPORTED_REASON_WEBKIT =
  'webkitSpeechRecognition streams audio to Google servers, violating local-first privacy principles.'

export interface VoiceCaptureOptions {
  requireConsent?: boolean
}

export function useVoiceCapture(options: VoiceCaptureOptions = {}) {
  const status = ref<VoiceCaptureStatus>('idle')
  const transcript = ref('')
  const errorMessage = ref('')
  const consentAcknowledged = ref(!options.requireConsent)
  let recognition: SpeechRecognition | null = null
  let stoppingRecognition: SpeechRecognition | null = null

  const isSupported = computed(() => {
    if (typeof window === 'undefined') return false
    return 'SpeechRecognition' in window
  })

  const isWebkitOnly = computed(() => {
    if (typeof window === 'undefined') return false
    return !('SpeechRecognition' in window) && 'webkitSpeechRecognition' in window
  })

  const unsupportedReason = computed(() => {
    if (isWebkitOnly.value) return UNSUPPORTED_REASON_WEBKIT
    if (!isSupported.value) return 'SpeechRecognition API is not available in this browser.'
    return null
  })

  function acknowledgeConsent(): void {
    consentAcknowledged.value = true
  }

  function startListening(): boolean {
    if (recognition || status.value === 'listening') {
      return false
    }

    if (!consentAcknowledged.value) {
      status.value = 'error'
      errorMessage.value = 'Consent required before starting voice capture.'
      return false
    }

    if (!isSupported.value) {
      status.value = 'error'
      errorMessage.value = unsupportedReason.value ?? 'Not supported'
      return false
    }

    try {
      const SpeechRecognitionCtor = window.SpeechRecognition
      if (!SpeechRecognitionCtor) {
        status.value = 'error'
        errorMessage.value = unsupportedReason.value ?? 'Not supported'
        return false
      }

      const activeRecognition = new SpeechRecognitionCtor()
      recognition = activeRecognition
      stoppingRecognition = null
      activeRecognition.continuous = false
      activeRecognition.interimResults = false
      activeRecognition.lang = navigator.language || 'en-US'

      activeRecognition.onresult = (event: SpeechRecognitionEvent) => {
        if (recognition !== activeRecognition && !(stoppingRecognition === activeRecognition && !recognition)) return
        const result = event.results[event.results.length - 1]
        if (result.isFinal) {
          transcript.value = result[0].transcript
        }
      }

      activeRecognition.onerror = (event: SpeechRecognitionErrorEvent) => {
        if (recognition !== activeRecognition) return
        status.value = 'error'
        errorMessage.value = `Speech recognition error: ${event.error}`
        recognition = null
      }

      activeRecognition.onend = () => {
        if (recognition !== activeRecognition && stoppingRecognition !== activeRecognition) return
        if (recognition === activeRecognition) {
          recognition = null
        }
        if (stoppingRecognition === activeRecognition) {
          stoppingRecognition = null
        }
        if (status.value !== 'error') {
          status.value = 'idle'
        }
      }

      transcript.value = ''
      errorMessage.value = ''
      status.value = 'listening'
      activeRecognition.start()
      return true
    } catch (err) {
      recognition = null
      stoppingRecognition = null
      status.value = 'error'
      errorMessage.value = err instanceof Error ? err.message : 'Failed to start'
      return false
    }
  }

  function stopListening(): void {
    if (recognition) {
      stoppingRecognition = recognition
      recognition.stop()
      recognition = null
    }
    if (status.value !== 'error') {
      status.value = 'idle'
    }
  }

  onUnmounted(() => {
    stopListening()
  })

  return {
    status,
    transcript,
    errorMessage,
    isSupported,
    isWebkitOnly,
    unsupportedReason,
    consentAcknowledged,
    acknowledgeConsent,
    startListening,
    stopListening,
  }
}
