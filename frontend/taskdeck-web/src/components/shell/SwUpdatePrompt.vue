<script setup lang="ts">
import { ref } from 'vue'
import { registerSW } from 'virtual:pwa-register'

const showUpdatePrompt = ref(false)

// registerSW returns a callback that sends SKIP_WAITING to the waiting SW
// and triggers a page reload via the controlling/controllerchange lifecycle
// managed internally by workbox-window.
const updateSW = registerSW({
  onNeedRefresh() {
    showUpdatePrompt.value = true
  },
})

async function applyUpdate() {
  showUpdatePrompt.value = false
  await updateSW()
}

function dismissUpdate() {
  showUpdatePrompt.value = false
}
</script>

<template>
  <Transition name="sw-update">
    <div
      v-if="showUpdatePrompt"
      class="td-sw-update"
      role="status"
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
