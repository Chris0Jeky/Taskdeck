<script setup lang="ts">
import { ref, onMounted } from 'vue'

const showUpdatePrompt = ref(false)
let swRegistration: ServiceWorkerRegistration | null = null

function applyUpdate() {
  if (swRegistration?.waiting) {
    // Tell the waiting SW to activate. The controllerchange listener
    // below will reload the page once the new SW takes over —
    // reloading here directly would race the activation and could
    // serve the page from the old SW.
    swRegistration.waiting.postMessage({ type: 'SKIP_WAITING' })
  }
  showUpdatePrompt.value = false
}

function dismissUpdate() {
  showUpdatePrompt.value = false
}

onMounted(() => {
  if (!('serviceWorker' in navigator)) return

  // Listen for SW updates from vite-plugin-pwa's auto-registration.
  // The plugin registers the SW and we hook into the update lifecycle.
  navigator.serviceWorker.ready.then((registration) => {
    swRegistration = registration

    // If there is already a waiting worker, prompt immediately
    if (registration.waiting) {
      showUpdatePrompt.value = true
    }

    // Listen for new service workers installing
    registration.addEventListener('updatefound', () => {
      const newWorker = registration.installing
      if (!newWorker) return

      newWorker.addEventListener('statechange', () => {
        // When the new SW is installed and waiting, prompt the user
        if (newWorker.state === 'installed' && navigator.serviceWorker.controller) {
          showUpdatePrompt.value = true
        }
      })
    })
  })

  // When the new SW activates and takes over, reload can happen
  let refreshing = false
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (refreshing) return
    refreshing = true
    window.location.reload()
  })
})
</script>

<template>
  <Transition name="sw-update">
    <div
      v-if="showUpdatePrompt"
      class="td-sw-update"
      role="alert"
      aria-live="polite"
    >
      <span class="material-symbols-outlined td-sw-update__icon" aria-hidden="true">
        system_update
      </span>
      <span class="td-sw-update__text">
        A new version of Taskdeck is available.
      </span>
      <button
        class="td-sw-update__btn td-sw-update__btn--primary"
        @click="applyUpdate"
      >
        Update now
      </button>
      <button
        class="td-sw-update__btn td-sw-update__btn--dismiss"
        aria-label="Dismiss update notification"
        @click="dismissUpdate"
      >
        <span class="material-symbols-outlined" aria-hidden="true">close</span>
      </button>
    </div>
  </Transition>
</template>

<style scoped>
.td-sw-update {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
  padding: var(--td-space-2) var(--td-space-4);
  background: var(--td-color-info-light);
  border-bottom: 1px solid var(--td-color-info);
  color: var(--td-color-info);
  font-size: var(--td-font-sm);
  font-weight: 500;
  z-index: 50;
  flex-shrink: 0;
}

.td-sw-update__icon {
  font-size: 18px;
  flex-shrink: 0;
}

.td-sw-update__text {
  flex: 1;
  line-height: 1.4;
}

.td-sw-update__btn {
  border: none;
  cursor: pointer;
  border-radius: var(--td-radius-sm);
  font-size: var(--td-font-sm);
  font-weight: 600;
  transition: background 0.15s ease;
}

.td-sw-update__btn--primary {
  padding: var(--td-space-1) var(--td-space-4);
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-sw-update__btn--primary:hover {
  background: var(--td-color-primary-hover);
}

.td-sw-update__btn--dismiss {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  background: transparent;
  color: var(--td-color-info);
}

.td-sw-update__btn--dismiss:hover {
  background: rgba(255, 179, 174, 0.15);
}

/* Transition */
.sw-update-enter-active,
.sw-update-leave-active {
  transition: transform 0.2s ease, opacity 0.2s ease;
}

.sw-update-enter-from,
.sw-update-leave-to {
  transform: translateY(-100%);
  opacity: 0;
}
</style>
