<script setup lang="ts">
import { computed, useId } from 'vue'
import { formatEstimatedEffort, parseEstimatedEffort } from '../../utils/estimatedEffort'

defineProps<{ readOnly?: boolean }>()
const hours = defineModel<string>('hours', { required: true })
const minutes = defineModel<string>('minutes', { required: true })
const id = useId()
const estimate = computed(() => parseEstimatedEffort(hours.value, minutes.value))
function clear() {
  hours.value = ''
  minutes.value = ''
}
</script>

<template>
  <fieldset class="space-y-2" aria-label="Estimated effort">
    <legend class="text-sm font-medium text-on-surface-variant">Estimated effort <span class="font-normal">(optional)</span></legend>
    <div v-if="!readOnly" class="flex flex-wrap items-end gap-2">
      <div>
        <label :for="`${id}-hours`" class="block text-xs text-on-surface-variant mb-1">Hours</label>
        <input :id="`${id}-hours`" v-model="hours" type="text" inputmode="numeric" pattern="[0-9]*"
          :aria-invalid="!!estimate.error" :aria-describedby="`${id}-hint`" data-testid="estimate-hours"
          class="w-24 rounded-md border border-outline-variant/40 bg-surface-container-high px-3 py-2 text-on-surface focus:outline-none focus:ring-2 focus:ring-primary" />
      </div>
      <div>
        <label :for="`${id}-minutes`" class="block text-xs text-on-surface-variant mb-1">Minutes</label>
        <input :id="`${id}-minutes`" v-model="minutes" type="text" inputmode="numeric" pattern="[0-9]*"
          :aria-invalid="!!estimate.error" :aria-describedby="`${id}-hint`" data-testid="estimate-minutes"
          class="w-24 rounded-md border border-outline-variant/40 bg-surface-container-high px-3 py-2 text-on-surface focus:outline-none focus:ring-2 focus:ring-primary" />
      </div>
      <button v-if="hours || minutes" type="button" class="rounded-md border border-outline-variant/40 px-3 py-2 text-sm text-on-surface-variant hover:bg-surface-container-high"
        @click="clear">Clear estimate</button>
    </div>
    <p :id="`${id}-hint`" class="text-xs" :class="estimate.error ? 'text-error' : 'text-on-surface-variant'"
      :role="estimate.error ? 'alert' : 'status'" data-testid="estimate-summary">
      {{ estimate.error ?? formatEstimatedEffort(estimate.value) }}<template v-if="!readOnly && !estimate.error && estimate.value === null">. Leave empty when unknown.</template>
    </p>
  </fieldset>
</template>
