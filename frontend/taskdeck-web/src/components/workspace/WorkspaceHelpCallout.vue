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
          <span class="td-help-callout__eyebrow tk-eyebrow">{{ eyebrow }}</span>
          <h2 class="td-help-callout__title tk-h3">{{ title }}</h2>
          <p class="td-help-callout__description tk-lede">{{ description }}</p>
        </div>
        <button
          class="td-help-callout__btn td-help-callout__btn--ghost td-help-callout__dismiss"
          type="button"
          @click="dismiss"
        >
          {{ dismissLabel }}
        </button>
      </div>

      <div v-if="$slots.default" class="td-help-callout__body tk-body">
        <slot />
      </div>

      <div v-if="$slots.actions" class="td-help-callout__actions">
        <slot name="actions" />
      </div>
    </template>

    <div v-else class="td-help-callout__dismissed">
      <span class="td-help-callout__hidden-message tk-body">{{ hiddenMessage }}</span>
      <button
        class="td-help-callout__btn td-help-callout__btn--secondary td-help-callout__replay"
        type="button"
        @click="replay"
      >
        {{ replayLabel }}
      </button>
    </div>
  </section>
</template>

<style scoped>
/* =========================================================
   Dual-render: this callout is shared by 8 Legacy-shell views but renders
   inside the canonical Paper shell (body carries `.paper` / `.paper-night`).

   - Surface/chrome uses Paper tokens with an Obsidian `--td-*` fallback
     (`var(--paper-card, var(--td-surface-primary))`), the pattern already used
     by the sibling WorkspaceSetupModal. Paper vars are scoped to the body
     class, so Legacy ("off") resolves to the fallback with no JS branching.
   - Typography under Paper is carried by the global `tk-*` utilities on the
     template. The Legacy type ramp is therefore guarded by
     `body:not(.paper):not(.paper-night)` so exactly one branch is ever live
     and the two never fight over cascade order.
   ========================================================= */

/* ---- Layout (mode agnostic) ---- */
.td-help-callout {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
  padding: var(--td-space-4);
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

.td-help-callout__title,
.td-help-callout__description {
  margin: 0;
}

.td-help-callout__body {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-help-callout__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
}

/* ---- Surface: Paper first, Legacy fallback ---- */
.td-help-callout {
  border: 1px solid var(--line, var(--td-border-default));
  border-radius: var(--r-2, var(--td-radius-lg));
  /* Restrained ember accent: a single inked rule, not a tinted band. */
  border-inline-start: 2px solid var(--ember, var(--td-color-primary));
  background: var(--paper-card, var(--td-surface-primary));
  box-shadow: var(--shadow-card, none);
  color: var(--ink, var(--td-text-primary));
}

/* ---- Type: Paper branch (tk-* carries family/size; ember carries the eyebrow) ---- */
.paper .td-help-callout__eyebrow,
.paper-night .td-help-callout__eyebrow {
  color: var(--ember);
}

/* ---- Type: Legacy branch ---- */
body:not(.paper):not(.paper-night) .td-help-callout__eyebrow {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--td-color-primary);
}

body:not(.paper):not(.paper-night) .td-help-callout__title {
  font-size: var(--td-font-lg);
  color: var(--td-text-primary);
}

body:not(.paper):not(.paper-night) .td-help-callout__description {
  color: var(--td-text-secondary);
  line-height: 1.6;
}

body:not(.paper):not(.paper-night) .td-help-callout__body {
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
  line-height: 1.6;
}

body:not(.paper):not(.paper-night) .td-help-callout__hidden-message {
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
  line-height: 1.5;
}

/* ---- Buttons: local, so the Legacy `.td-btn` utility layer no longer leaks
       its Obsidian palette into the Paper shell. ---- */
.td-help-callout__btn {
  flex: none;
  display: inline-flex;
  align-items: center;
  gap: var(--td-space-2);
  padding: var(--td-space-2) var(--td-space-3);
  border: 1px solid var(--line, var(--td-border-default));
  border-radius: var(--r-2, var(--td-radius-md));
  background: transparent;
  font-family: var(--sans, inherit);
  font-size: var(--td-font-sm);
  color: var(--ink-2, var(--td-text-secondary));
  cursor: pointer;
  transition: background 140ms ease, border-color 140ms ease, color 140ms ease;
}

.td-help-callout__btn:hover {
  background: var(--ember-bloom, var(--td-surface-container-high));
  border-color: var(--ember, var(--td-border-focus));
  color: var(--ember-ink, var(--td-text-primary));
}

.td-help-callout__btn--ghost {
  border-color: transparent;
}

.td-help-callout__btn--ghost:hover {
  border-color: var(--ember, var(--td-border-focus));
}

.td-help-callout__btn--secondary {
  background: var(--paper-2, var(--td-surface-container-highest));
  color: var(--ink, var(--td-text-primary));
}

@media (max-width: 768px) {
  .td-help-callout__header,
  .td-help-callout__dismissed {
    flex-direction: column;
  }
}
</style>
