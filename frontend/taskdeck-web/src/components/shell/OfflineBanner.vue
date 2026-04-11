<script setup lang="ts">
import { useOnlineStatus } from '../../composables/useOnlineStatus'

const { isOnline } = useOnlineStatus()
</script>

<template>
  <Transition name="offline-banner">
    <div
      v-if="!isOnline"
      class="td-offline-banner"
      role="status"
      aria-live="assertive"
      aria-atomic="true"
    >
      <span class="material-symbols-outlined td-offline-banner__icon" aria-hidden="true">
        cloud_off
      </span>
      <span class="td-offline-banner__text">
        You are offline. Some cached data is still available, but changes cannot be saved until you reconnect.
      </span>
    </div>
  </Transition>
</template>

<style scoped>
.td-offline-banner {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
  padding: var(--td-space-2) var(--td-space-4);
  background: var(--td-color-warning-light);
  border-bottom: 1px solid var(--td-color-warning);
  color: var(--td-color-warning);
  font-size: var(--td-font-sm);
  font-weight: 500;
  z-index: 50;
  flex-shrink: 0;
}

.td-offline-banner__icon {
  font-size: 18px;
  flex-shrink: 0;
}

.td-offline-banner__text {
  line-height: 1.4;
}

/* Transition */
.offline-banner-enter-active,
.offline-banner-leave-active {
  transition: transform 0.2s ease, opacity 0.2s ease;
}

.offline-banner-enter-from,
.offline-banner-leave-to {
  transform: translateY(-100%);
  opacity: 0;
}
</style>
