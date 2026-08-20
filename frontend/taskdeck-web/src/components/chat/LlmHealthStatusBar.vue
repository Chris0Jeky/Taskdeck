<script setup lang="ts">
import { computed } from 'vue'
import type { ChatProviderHealth } from '../../types/chat'

const props = defineProps<{
  chatHealth: ChatProviderHealth | null
  loadingHealth: boolean
  chatHealthLoadError: string | null
}>()

const maxDisplayedProbeLatencyMs = 300_000

const llmProbeLatencyMs = computed(() => {
  const health = props.chatHealth
  if (!health || health.isMock || !health.isProbed) {
    return null
  }

  const latency = health.probeLatencyMs
  if (
    typeof latency !== 'number'
    || !Number.isSafeInteger(latency)
    || latency < 1
    || latency > maxDisplayedProbeLatencyMs
  ) {
    return null
  }

  return latency
})

const llmHealthState = computed(() => {
  if (props.loadingHealth) {
    return 'loading'
  }

  if (props.chatHealthLoadError) {
    return 'error'
  }

  if (!props.chatHealth) {
    return 'unknown'
  }

  if (props.chatHealth.isMock) {
    return 'mock'
  }

  const vs = props.chatHealth.verificationStatus
  if (vs === 'verified') {
    return 'verified'
  }

  if (vs === 'failed') {
    return 'failed'
  }

  if (props.chatHealth.isAvailable) {
    return 'configured'
  }

  return 'unavailable'
})

const llmStatusTitle = computed(() => {
  switch (llmHealthState.value) {
    case 'loading':
      return 'Checking LLM status'
    case 'verified':
      return 'Live LLM verified'
    case 'configured':
      return 'Live LLM configured'
    case 'failed':
      return 'LLM verification failed'
    case 'mock':
      return 'Live LLM not active'
    case 'unavailable':
      return 'Live LLM unavailable'
    case 'error':
      return 'LLM status unavailable'
    default:
      return 'LLM status unknown'
  }
})

const llmStatusCopy = computed(() => {
  if (llmHealthState.value === 'loading') {
    return 'Resolving the current provider before manual chat work starts.'
  }

  if (llmHealthState.value === 'error') {
    return props.chatHealthLoadError ?? 'Taskdeck could not resolve provider status for this chat surface.'
  }

  if (!props.chatHealth) {
    return 'Taskdeck has not reported provider health yet.'
  }

  const providerLabel = props.chatHealth.model
    ? `${props.chatHealth.providerName} (${props.chatHealth.model})`
    : props.chatHealth.providerName

  if (llmHealthState.value === 'verified') {
    return `${providerLabel} is live and responding. The probe confirmed reachability.`
  }

  if (llmHealthState.value === 'configured') {
    return `Taskdeck is configured to use ${providerLabel}, but this health check does not prove the upstream provider accepted a live request yet. Use Verify LLM to confirm reachability.`
  }

  if (llmHealthState.value === 'failed') {
    return props.chatHealth.errorMessage
      ? `${providerLabel} verification failed: ${props.chatHealth.errorMessage}`
      : `${providerLabel} verification failed. The probe could not confirm reachability.`
  }

  if (llmHealthState.value === 'mock') {
    return `Taskdeck is currently using the Mock provider. Responses stay deterministic and do not prove a live LLM hookup.`
  }

  return props.chatHealth.errorMessage
    ? `${providerLabel} is not ready: ${props.chatHealth.errorMessage}`
    : `${providerLabel} is not ready for live requests.`
})

const llmStatusMeta = computed(() => {
  if (!props.chatHealth || llmHealthState.value === 'loading' || llmHealthState.value === 'error') {
    return null
  }

  return props.chatHealth.model
    ? `${props.chatHealth.providerName} | ${props.chatHealth.model}`
    : props.chatHealth.providerName
})
</script>

<template>
  <section
    class="td-chat-status"
    :class="`td-chat-status--${llmHealthState}`"
    :data-llm-health-state="llmHealthState"
    :data-llm-probe-latency-ms="llmProbeLatencyMs"
  >
    <div>
      <h2 class="td-chat-status__title">{{ llmStatusTitle }}</h2>
      <p class="td-chat-status__copy">{{ llmStatusCopy }}</p>
    </div>
    <span v-if="llmStatusMeta" class="td-chat-status__meta">
      {{ llmStatusMeta }}
      <span v-if="llmProbeLatencyMs !== null"> | Probe completed in {{ llmProbeLatencyMs }} ms.</span>
    </span>
  </section>
</template>

<style scoped>
.td-chat-status {
  display: flex;
  justify-content: space-between;
  gap: var(--td-space-3);
  align-items: flex-start;
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-3) var(--td-space-4);
  background: var(--td-surface-primary);
}

.td-chat-status--configured {
  border-color: var(--td-color-warning, #b7791f);
  background: color-mix(in srgb, var(--td-surface-primary) 86%, #fff4d6 14%);
}

.td-chat-status--verified {
  border-color: var(--td-color-success, #2f855a);
  background: color-mix(in srgb, var(--td-surface-primary) 80%, #c6f6d5 20%);
}

.td-chat-status--failed {
  border-color: var(--td-color-danger, #c53030);
  background: color-mix(in srgb, var(--td-surface-primary) 86%, #fed7d7 14%);
}

.td-chat-status--mock,
.td-chat-status--unavailable,
.td-chat-status--error {
  border-color: var(--td-color-warning, #b7791f);
  background: color-mix(in srgb, var(--td-surface-primary) 86%, #fff4d6 14%);
}

.td-chat-status__title {
  margin: 0 0 var(--td-space-1);
  font-size: var(--td-font-sm);
  font-weight: 700;
}

.td-chat-status__copy {
  margin: 0;
  color: var(--td-text-secondary);
  line-height: 1.5;
}

.td-chat-status__meta {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  font-family: monospace;
  white-space: nowrap;
}

@media (max-width: 900px) {
  .td-chat-status {
    flex-direction: column;
  }

  .td-chat-status__meta {
    white-space: normal;
  }
}
</style>
