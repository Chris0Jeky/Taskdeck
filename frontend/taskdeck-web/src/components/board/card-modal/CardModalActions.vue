<script setup lang="ts">
defineProps<{
  isFormValid: boolean
}>()

defineEmits<{
  (e: 'save'): void
  (e: 'close'): void
  (e: 'delete-click'): void
}>()
</script>

<template>
  <div class="mt-6 flex items-center justify-between">
    <button
      @click="$emit('delete-click')"
      type="button"
      class="px-4 py-2 text-sm font-medium text-error hover:text-error/80 hover:bg-error/10 border border-error/40 rounded-md transition-colors"
    >
      Delete Card
    </button>
    <div class="flex gap-2">
      <button
        @click="$emit('close')"
        type="button"
        class="px-4 py-2 text-sm font-medium text-on-surface-variant hover:bg-surface-container-high border border-outline-variant/40 rounded-md transition-colors"
      >
        Cancel
      </button>
      <button
        @click="$emit('save')"
        :disabled="!isFormValid"
        type="button"
        class="px-4 py-2 text-sm font-medium text-on-primary-container bg-primary-container hover:brightness-110 disabled:opacity-40 disabled:cursor-not-allowed rounded-md transition-all"
      >
        Save Changes
      </button>
    </div>
  </div>
</template>
