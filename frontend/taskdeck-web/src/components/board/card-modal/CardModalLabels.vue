<script setup lang="ts">
import type { Label } from '../../../types/board'

defineProps<{
  labels: Label[]
}>()

const selectedLabelIds = defineModel<string[]>('selectedLabelIds', { required: true })
</script>

<template>
  <div>
    <p class="block text-sm font-medium text-on-surface-variant mb-2">
      Labels
    </p>
    <div v-if="labels.length > 0" class="flex flex-col gap-2">
      <label
        v-for="label in labels"
        :key="label.id"
        class="inline-flex items-center gap-2 px-3 py-1.5 rounded-md text-sm font-medium transition-all cursor-pointer"
        :class="[
          selectedLabelIds.includes(label.id)
            ? 'text-white ring-2 ring-offset-2 ring-primary td-dynamic-bg'
            : 'text-on-surface bg-surface-container-high hover:bg-surface-container-highest',
        ]"
        :style="selectedLabelIds.includes(label.id) ? { '--td-dynamic-color': label.colorHex } : undefined"
      >
        <input
          :id="`label-${label.id}`"
          v-model="selectedLabelIds"
          type="checkbox"
          :value="label.id"
          class="w-4 h-4 text-primary border-outline-variant rounded"
        />
        <!-- Color swatch always visible so users can identify labels before selecting -->
        <span
          class="inline-block w-3 h-3 rounded-full flex-shrink-0"
          :class="label.colorHex ? 'td-dynamic-bg' : 'bg-outline-variant'"
          :style="label.colorHex ? { '--td-dynamic-color': label.colorHex } : undefined"
          aria-hidden="true"
        />
        <span>{{ label.name }}</span>
      </label>
    </div>
    <p v-else class="text-sm text-on-surface-variant italic">No labels available</p>
  </div>
</template>
