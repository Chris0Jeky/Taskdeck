<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
import { useNotificationStore } from '../store/notificationStore'
import { getErrorDisplay } from '../composables/useErrorMapper'

const notifications = useNotificationStore()
const submitting = ref(false)
const inlineError = ref<string | null>(null)

const form = reactive({
  inAppChannelEnabled: true,
  mentionImmediateEnabled: true,
  mentionDigestEnabled: false,
  assignmentImmediateEnabled: true,
  assignmentDigestEnabled: false,
  proposalOutcomeImmediateEnabled: true,
  proposalOutcomeDigestEnabled: false,
})

function applyFromStore() {
  const preferences = notifications.preferences
  if (!preferences) return

  form.inAppChannelEnabled = preferences.inAppChannelEnabled
  form.mentionImmediateEnabled = preferences.mentionImmediateEnabled
  form.mentionDigestEnabled = preferences.mentionDigestEnabled
  form.assignmentImmediateEnabled = preferences.assignmentImmediateEnabled
  form.assignmentDigestEnabled = preferences.assignmentDigestEnabled
  form.proposalOutcomeImmediateEnabled = preferences.proposalOutcomeImmediateEnabled
  form.proposalOutcomeDigestEnabled = preferences.proposalOutcomeDigestEnabled
}

async function loadPreferences() {
  inlineError.value = null
  try {
    await notifications.fetchPreferences()
    applyFromStore()
  } catch (e: unknown) {
    inlineError.value = getErrorDisplay(e, notifications.error || 'Failed to load preferences').message
  }
}

async function savePreferences() {
  inlineError.value = null
  try {
    submitting.value = true
    await notifications.updatePreferences({ ...form })
    applyFromStore()
  } catch (e: unknown) {
    inlineError.value = getErrorDisplay(e, notifications.error || 'Failed to save preferences').message
  } finally {
    submitting.value = false
  }
}

onMounted(() => {
  void loadPreferences()
})
</script>

<template>
  <div class="paper-prefs">
    <header class="paper-prefs__hero">
      <span class="tk-eyebrow paper-prefs__eyebrow">Settings</span>
      <h1 class="tk-h1 paper-prefs__title">Notification Preferences</h1>
      <p class="tk-lede paper-prefs__subtitle">
        Control which events create in-app notifications and whether each event runs on immediate or digest cadence.
      </p>
    </header>

    <div v-if="inlineError" class="paper-prefs__alert" role="alert">
      {{ inlineError }}
    </div>

    <form class="paper-prefs__panel" @submit.prevent="savePreferences">
      <label class="paper-prefs__toggle-row">
        <span>Enable in-app notifications</span>
        <input v-model="form.inAppChannelEnabled" type="checkbox" class="paper-prefs__checkbox" />
      </label>

      <div class="tk-eyebrow paper-prefs__group-label">Mentions</div>
      <label class="paper-prefs__toggle-row">
        <span>Immediate</span>
        <input v-model="form.mentionImmediateEnabled" type="checkbox" class="paper-prefs__checkbox" />
      </label>
      <label class="paper-prefs__toggle-row">
        <span>Digest</span>
        <input v-model="form.mentionDigestEnabled" type="checkbox" class="paper-prefs__checkbox" />
      </label>

      <div class="tk-eyebrow paper-prefs__group-label">Assignments</div>
      <label class="paper-prefs__toggle-row">
        <span>Immediate</span>
        <input v-model="form.assignmentImmediateEnabled" type="checkbox" class="paper-prefs__checkbox" />
      </label>
      <label class="paper-prefs__toggle-row">
        <span>Digest</span>
        <input v-model="form.assignmentDigestEnabled" type="checkbox" class="paper-prefs__checkbox" />
      </label>

      <div class="tk-eyebrow paper-prefs__group-label">Proposal Outcomes</div>
      <label class="paper-prefs__toggle-row">
        <span>Immediate</span>
        <input v-model="form.proposalOutcomeImmediateEnabled" type="checkbox" class="paper-prefs__checkbox" />
      </label>
      <label class="paper-prefs__toggle-row">
        <span>Digest</span>
        <input v-model="form.proposalOutcomeDigestEnabled" type="checkbox" class="paper-prefs__checkbox" />
      </label>

      <div class="paper-prefs__actions">
        <PaperHLBtn
          variant="ember"
          type="submit"
          :disabled="submitting || notifications.loading"
        >
          {{ submitting ? 'Saving...' : 'Save Preferences' }}
        </PaperHLBtn>
      </div>
    </form>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — NotificationPreferencesView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens are defined under `.paper` / `.paper-night` (the canonical shell), so
   var() fallbacks keep the surface legible if the view is ever rendered outside
   the Paper shell (Legacy/Obsidian "off" mode). */

.paper-prefs {
  display: flex;
  flex-direction: column;
  gap: var(--s-5, 20px);
  max-width: 640px;
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

/* ── Hero ── */

.paper-prefs__hero {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
}

.paper-prefs__eyebrow {
  color: var(--mute, #6c6557);
}

.paper-prefs__title {
  margin: 0;
  font-size: var(--t-h2, 32px);
}

.paper-prefs__subtitle {
  margin: 0;
  color: var(--ink-2, #3a352d);
}

/* ── Alert ── */

.paper-prefs__alert {
  padding: var(--s-3, 12px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--overdue, #8c4a26);
  background: var(--overdue-tint, #ecd9c4);
  color: var(--overdue, #8c4a26);
  font-size: var(--t-md, 13.5px);
}

/* ── Panel ── */

.paper-prefs__panel {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
  padding: var(--s-5, 20px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

.paper-prefs__group-label {
  margin-top: var(--s-2, 8px);
  color: var(--mute, #6c6557);
}

.paper-prefs__toggle-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--s-3, 12px);
  color: var(--ink-2, #3a352d);
  font-size: var(--t-md, 13.5px);
  cursor: pointer;
}

.paper-prefs__checkbox {
  width: 16px;
  height: 16px;
  accent-color: var(--ember, #a8421f);
  cursor: pointer;
}

.paper-prefs__actions {
  display: flex;
  margin-top: var(--s-2, 8px);
}
</style>
