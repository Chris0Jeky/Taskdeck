<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
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
  <div class="td-notification-preferences">
    <h1 class="td-page-title">Notification Preferences</h1>
    <p class="td-description">
      Control which events create in-app notifications and whether each event runs on immediate or digest cadence.
    </p>

    <div v-if="inlineError" class="td-alert td-alert--error" role="alert">
      {{ inlineError }}
    </div>

    <form class="td-panel" @submit.prevent="savePreferences">
      <label class="td-toggle-row">
        <span>Enable in-app notifications</span>
        <input v-model="form.inAppChannelEnabled" type="checkbox" />
      </label>

      <div class="td-section-title">Mentions</div>
      <label class="td-toggle-row">
        <span>Immediate</span>
        <input v-model="form.mentionImmediateEnabled" type="checkbox" />
      </label>
      <label class="td-toggle-row">
        <span>Digest</span>
        <input v-model="form.mentionDigestEnabled" type="checkbox" />
      </label>

      <div class="td-section-title">Assignments</div>
      <label class="td-toggle-row">
        <span>Immediate</span>
        <input v-model="form.assignmentImmediateEnabled" type="checkbox" />
      </label>
      <label class="td-toggle-row">
        <span>Digest</span>
        <input v-model="form.assignmentDigestEnabled" type="checkbox" />
      </label>

      <div class="td-section-title">Proposal Outcomes</div>
      <label class="td-toggle-row">
        <span>Immediate</span>
        <input v-model="form.proposalOutcomeImmediateEnabled" type="checkbox" />
      </label>
      <label class="td-toggle-row">
        <span>Digest</span>
        <input v-model="form.proposalOutcomeDigestEnabled" type="checkbox" />
      </label>

      <button class="td-btn td-btn--primary" type="submit" :disabled="submitting || notifications.loading">
        {{ submitting ? 'Saving...' : 'Save Preferences' }}
      </button>
    </form>
  </div>
</template>

<style scoped>
.td-notification-preferences {
  max-width: 640px;
}

.td-description {
  color: var(--td-text-secondary);
  margin-bottom: var(--td-space-4);
}

.td-alert {
  margin-bottom: var(--td-space-4);
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
}

.td-alert--error {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
}

.td-panel {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
  background: var(--td-surface-primary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  padding: var(--td-space-5);
}

.td-section-title {
  font-weight: 700;
  color: var(--td-text-primary);
  margin-top: var(--td-space-2);
}

.td-toggle-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  color: var(--td-text-secondary);
}

.td-btn {
  margin-top: var(--td-space-3);
  width: fit-content;
  padding: var(--td-space-2) var(--td-space-4);
  border-radius: var(--td-radius-md);
  border: none;
  cursor: pointer;
}

.td-btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}
</style>
