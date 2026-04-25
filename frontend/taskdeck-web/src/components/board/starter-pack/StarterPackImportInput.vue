<script setup lang="ts">
import type { ManifestValidationError } from '../../../types/starter-packs'

defineProps<{
  importJsonText: string
  importValidating: boolean
  importValidationErrors: ManifestValidationError[]
  importErrorMessage: string | null
}>()

defineEmits<{
  (e: 'update:importJsonText', value: string): void
  (e: 'validate'): void
  (e: 'file-upload', event: Event): void
  (e: 'clear-feedback'): void
}>()
</script>

<template>
  <section class="sp-section-border p-6">
    <label for="import-json-textarea" class="sp-label mb-2 block text-sm font-medium">
      Manifest JSON
    </label>
    <textarea
      id="import-json-textarea"
      :value="importJsonText"
      placeholder='Paste starter pack manifest JSON here, or use "Upload file" below...'
      rows="14"
      class="sp-input mb-3 w-full rounded-md px-3 py-2 font-mono text-xs focus:outline-none"
      data-testid="import-json-textarea"
      @input="$emit('update:importJsonText', ($event.target as HTMLTextAreaElement).value); $emit('clear-feedback')"
    ></textarea>

    <div class="flex flex-wrap gap-2">
      <button
        type="button"
        :disabled="importValidating"
        class="sp-btn-secondary rounded-md px-4 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-60"
        data-testid="import-validate-btn"
        @click="$emit('validate')"
      >
        {{ importValidating ? 'Validating...' : 'Validate' }}
      </button>
      <label
        class="sp-btn-upload cursor-pointer rounded-md px-4 py-2 text-sm font-medium transition-colors"
      >
        Upload file
        <input
          type="file"
          accept=".json,application/json"
          class="hidden"
          data-testid="import-file-input"
          @change="$emit('file-upload', $event)"
        />
      </label>
    </div>

    <div v-if="importValidationErrors.length > 0" class="sp-error-box mt-4 rounded-md p-3" data-testid="import-validation-errors">
      <p class="sp-text-error mb-2 text-xs font-semibold uppercase tracking-wide">Validation Errors</p>
      <ul class="max-h-48 space-y-1 overflow-y-auto text-xs">
        <li v-for="(err, index) in importValidationErrors" :key="`verr-${index}`">
          <span class="font-semibold">{{ err.path }}</span> - {{ err.message }}
        </li>
      </ul>
    </div>

    <div v-if="importErrorMessage" class="sp-error-box mt-4 rounded-md p-3 text-sm">
      {{ importErrorMessage }}
    </div>
  </section>
</template>

<style scoped>
@import './starter-pack-tokens.css';
</style>
