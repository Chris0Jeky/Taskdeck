<script setup lang="ts">
import { useWorkspaceHelp, type WorkspaceHelpTopic } from '../../composables/useWorkspaceHelp'

const props = withDefaults(defineProps<{
  topic: WorkspaceHelpTopic
  title: string
  description: string
  eyebrow?: string
  dismissLabel?: string
  replayLabel?: string
  hiddenMessage?: string
}>(), {
  eyebrow: 'What is this?',
  dismissLabel: 'Hide this guide',
  replayLabel: 'Show page guide',
  hiddenMessage: 'This page guide is hidden.',
})

const { isVisible, dismiss, replay } = useWorkspaceHelp(props.topic)
</script>

<template>
  <section :data-help-topic="topic" class="td-help-callout">
    <template v-if="isVisible">
      <div class="td-help-callout__header">
        <div class="td-help-callout__copy">
          <span class="td-help-callout__eyebrow">{{ eyebrow }}</span>
          <h2 class="td-help-callout__title">{{ title }}</h2>
          <p class="td-help-callout__description">{{ description }}</p>
        </div>
        <button class="td-btn td-btn--ghost td-btn--sm" type="button" @click="dismiss">
          {{ dismissLabel }}
        </button>
      </div>

      <div v-if="$slots.default" class="td-help-callout__body">
        <slot />
      </div>

      <div v-if="$slots.actions" class="td-help-callout__actions">
        <slot name="actions" />
      </div>
    </template>

    <div v-else class="td-help-callout__dismissed">
      <span>{{ hiddenMessage }}</span>
      <button class="td-btn td-btn--secondary td-btn--sm" type="button" @click="replay">
        {{ replayLabel }}
      </button>
    </div>
  </section>
</template>

<style scoped>
.td-help-callout {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
  padding: var(--td-space-4);
  border: 1px solid color-mix(in srgb, var(--td-color-primary) 18%, var(--td-border-default));
  border-radius: var(--td-radius-lg);
  background:
    linear-gradient(135deg, color-mix(in srgb, var(--td-color-primary) 9%, var(--td-surface-container-high)), transparent 52%),
    var(--td-surface-primary);
}

.td-help-callout__header,
.td-help-callout__dismissed {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--td-space-3);
}

.td-help-callout__copy {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-help-callout__eyebrow {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-color-primary);
}

.td-help-callout__title {
  margin: 0;
  font-size: var(--td-font-lg);
  color: var(--td-text-primary);
}

.td-help-callout__description {
  margin: 0;
  color: var(--td-text-secondary);
  line-height: 1.6;
}

.td-help-callout__body {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
  line-height: 1.6;
}

.td-help-callout__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
}

.td-help-callout__dismissed {
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
  line-height: 1.5;
}

@media (max-width: 768px) {
  .td-help-callout__header,
  .td-help-callout__dismissed {
    flex-direction: column;
  }
}
</style>
