<script setup lang="ts">
defineEmits<{
  (e: 'close'): void
}>()
</script>

<template>
  <div class="flex items-start justify-between mb-4">
    <h2 class="text-2xl font-semibold text-on-surface">Edit Card</h2>
    <button
      @click="$emit('close')"
      class="text-on-surface-variant hover:text-on-surface transition-colors"
      aria-label="Close card editor"
    >
      <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
      </svg>
    </button>
  </div>
</template>
