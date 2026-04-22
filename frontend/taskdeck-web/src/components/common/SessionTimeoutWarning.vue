<script setup lang="ts">
import { computed } from 'vue'
import { useSessionTimeout } from '../../composables/useSessionTimeout'

const { showWarning, secondsRemaining, extending, dismiss, extend } = useSessionTimeout()

const formattedTime = computed(() => {
  const secs = secondsRemaining.value
  if (secs === null || secs <= 0) return '0:00'
  const minutes = Math.floor(secs / 60)
  const seconds = secs % 60
  return `${minutes}:${seconds.toString().padStart(2, '0')}`
})
</script>

<template>
  <Transition name="session-warning">
    <div
      v-if="showWarning"
      class="fixed bottom-4 right-4 z-50 max-w-sm rounded-lg border border-yellow-300 bg-yellow-50 px-4 py-3 shadow-lg"
      role="alert"
      aria-live="assertive"
    >
      <div class="flex items-start gap-3">
        <!-- Warning icon -->
        <div class="flex-shrink-0 mt-0.5" aria-hidden="true">
          <svg class="h-5 w-5 text-yellow-600" fill="currentColor" viewBox="0 0 20 20">
            <path
              fill-rule="evenodd"
              d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z"
              clip-rule="evenodd"
            />
          </svg>
        </div>

        <div class="flex-1">
          <p class="text-sm font-medium text-yellow-800">
            Session expires in
            <span class="font-mono font-bold">{{ formattedTime }}</span>
          </p>
          <p class="mt-1 text-xs text-yellow-700">
            Save your work or extend your session to continue.
          </p>
          <div class="mt-2 flex gap-2">
            <button
              class="rounded bg-yellow-600 px-3 py-1 text-xs font-medium text-white hover:bg-yellow-700 focus:outline-none focus:ring-2 focus:ring-yellow-500 focus:ring-offset-1 disabled:opacity-50"
              :disabled="extending"
              @click="extend"
            >
              {{ extending ? 'Extending...' : 'Extend Session' }}
            </button>
            <button
              class="rounded border border-yellow-400 px-3 py-1 text-xs font-medium text-yellow-700 hover:bg-yellow-100 focus:outline-none focus:ring-2 focus:ring-yellow-500 focus:ring-offset-1"
              @click="dismiss"
            >
              Dismiss
            </button>
          </div>
        </div>

        <!-- Close button -->
        <button
          class="flex-shrink-0 text-yellow-600 hover:text-yellow-800"
          aria-label="Dismiss session warning"
          @click="dismiss"
        >
          <svg class="h-4 w-4" fill="currentColor" viewBox="0 0 20 20">
            <path
              fill-rule="evenodd"
              d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z"
              clip-rule="evenodd"
            />
          </svg>
        </button>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.session-warning-enter-active,
.session-warning-leave-active {
  transition: all 0.3s ease;
}

.session-warning-enter-from {
  opacity: 0;
  transform: translateY(1rem);
}

.session-warning-leave-to {
  opacity: 0;
  transform: translateY(1rem);
}
</style>
