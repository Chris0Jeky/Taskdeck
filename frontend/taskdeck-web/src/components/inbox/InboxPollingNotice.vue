<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const props = defineProps<{
  problems?: Record<string, 'retrying' | 'unavailable'>
  paused?: boolean
  watchedIds?: Set<string>
}>()
const emit = defineEmits<{ refresh: [] }>()
const problems = computed(() => Object.values(props.problems ?? {}))
const message = computed(() => props.paused ? 'paused'
  : problems.value.includes('unavailable') ? 'unavailable'
    : problems.value.includes('retrying') ? 'retrying'
      : props.watchedIds?.size ? 'waiting' : null)
</script>

<template>
  <div v-if="message" class="td-inline-alert" role="status" aria-live="polite" data-testid="inbox-polling-notice">
    <p>{{ t(`inbox.polling.${message}`) }}</p>
    <button v-if="problems.length && !props.paused" class="td-btn td-btn--secondary" @click="emit('refresh')">
      {{ t('inbox.polling.refresh') }}
    </button>
  </div>
</template>
