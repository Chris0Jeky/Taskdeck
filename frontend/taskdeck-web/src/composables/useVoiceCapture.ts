import { ref, computed, onUnmounted } from 'vue'

export type VoiceCaptureStatus = 'idle' | 'listening' | 'error'

const UNSUPPORTED_REASON_WEBKIT =
  'webkitSpeechRecognition streams audio to Google servers, violating local-first privacy principles.'

export function useVoiceCapture() {
  const status = ref<VoiceCaptureStatus>('idle')
  const transcript = ref('')
  const errorMessage = ref('')
  let recognition: SpeechRecognition | null = null

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

  function startListening(): boolean {
    if (!isSupported.value) {
      status.value = 'error'
      errorMessage.value = unsupportedReason.value ?? 'Not supported'
      return false
    }

    try {
      recognition = new SpeechRecognition()
      recognition.continuous = false
      recognition.interimResults = false
      recognition.lang = navigator.language || 'en-US'

      recognition.onresult = (event: SpeechRecognitionEvent) => {
        const result = event.results[event.results.length - 1]
        if (result.isFinal) {
          transcript.value = result[0].transcript
        }
      }

      recognition.onerror = (event: SpeechRecognitionErrorEvent) => {
        status.value = 'error'
        errorMessage.value = `Speech recognition error: ${event.error}`
      }

      recognition.onend = () => {
        status.value = 'idle'
      }

      transcript.value = ''
      errorMessage.value = ''
      status.value = 'listening'
      recognition.start()
      return true
    } catch (err) {
      status.value = 'error'
      errorMessage.value = err instanceof Error ? err.message : 'Failed to start'
      return false
    }
  }

  function stopListening(): void {
    if (recognition) {
      recognition.stop()
      recognition = null
    }
    status.value = 'idle'
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
    startListening,
    stopListening,
  }
}
