<script setup lang="ts">
import { computed, ref } from 'vue'
import { useSessionStore } from '../store/sessionStore'
import { useFeatureFlagStore } from '../store/featureFlagStore'
import type { FeatureFlags } from '../types/feature-flags'
import { getErrorDisplay } from '../composables/useErrorMapper'
import { normalizeBoardRole } from '../utils/roles'

const session = useSessionStore()
const featureFlags = useFeatureFlagStore()

const currentPassword = ref('')
const newPassword = ref('')
const confirmNewPassword = ref('')
const passwordError = ref<string | null>(null)
const passwordSuccess = ref(false)
const submitting = ref(false)

const roleLabel = computed(() => (
  session.defaultRole === null ? 'Unknown' : normalizeBoardRole(session.defaultRole)
))

const opsCapabilitySummary = computed(() => {
  switch (roleLabel.value) {
    case 'Owner':
    case 'Admin':
      return 'Can run all default Ops CLI templates.'
    case 'Editor':
      return 'Can run editor-safe Ops templates; admin templates are restricted.'
    default:
      return 'Ops CLI template access is limited; request elevated access for admin templates.'
  }
})

async function handleChangePassword() {
  passwordError.value = null
  passwordSuccess.value = false
  if (!currentPassword.value || !newPassword.value) {
    passwordError.value = 'Please fill in all password fields.'
    return
  }
  if (newPassword.value !== confirmNewPassword.value) {
    passwordError.value = 'New passwords do not match.'
    return
  }
  if (newPassword.value.length < 6) {
    passwordError.value = 'New password must be at least 6 characters.'
    return
  }
  try {
    submitting.value = true
    const userId = session.requireUserId('password changes')
    await session.changePassword({
      userId,
      currentPassword: currentPassword.value,
      newPassword: newPassword.value,
    })
    passwordSuccess.value = true
    currentPassword.value = ''
    newPassword.value = ''
    confirmNewPassword.value = ''
  } catch (e: unknown) {
    passwordError.value = getErrorDisplay(e, session.error || 'Failed to change password.').message
  } finally {
    submitting.value = false
  }
}

const flagLabels: Record<keyof FeatureFlags, string> = {
  newShell: 'New Shell Layout',
  newAuth: 'Auth & Permissions',
  newAccess: 'Board Access Management',
  newActivity: 'Activity & Audit Views',
  newOps: 'Ops Console',
  newAutomation: 'Automation & Queue',
  newArchive: 'Archive & Export/Import',
  devTools: 'Dev Tools (Internal)',
}
</script>

<template>
  <div class="td-settings">
    <h1 class="td-page-title">Settings</h1>

    <!-- Profile Section -->
    <section class="td-settings__section">
      <h2 class="td-section-title">Profile</h2>
      <div class="td-info-grid">
        <div class="td-info-row">
          <span class="td-info-label">Username</span>
          <span class="td-info-value">{{ session.username || '—' }}</span>
        </div>
        <div class="td-info-row">
          <span class="td-info-label">Email</span>
          <span class="td-info-value">{{ session.email || '—' }}</span>
        </div>
        <div class="td-info-row">
          <span class="td-info-label">User ID</span>
          <span class="td-info-value td-mono">{{ session.userId || '—' }}</span>
        </div>
        <div class="td-info-row">
          <span class="td-info-label">Role</span>
          <span class="td-info-value">{{ roleLabel }}</span>
        </div>
        <div class="td-info-row td-info-row--stacked">
          <span class="td-info-label">Ops Access</span>
          <span class="td-info-value">{{ opsCapabilitySummary }}</span>
        </div>
      </div>
    </section>

    <!-- Change Password Section -->
    <section class="td-settings__section">
      <h2 class="td-section-title">Change Password</h2>
      <form @submit.prevent="handleChangePassword" class="td-settings__form">
        <div v-if="passwordError" class="td-alert td-alert--error" role="alert">{{ passwordError }}</div>
        <div v-if="passwordSuccess" class="td-alert td-alert--success" role="status">Password changed successfully.</div>

        <div class="td-form-group">
          <label for="current-pw" class="td-label">Current Password</label>
          <input id="current-pw" v-model="currentPassword" type="password" class="td-input" autocomplete="current-password" />
        </div>
        <div class="td-form-group">
          <label for="new-pw" class="td-label">New Password</label>
          <input id="new-pw" v-model="newPassword" type="password" class="td-input" autocomplete="new-password" />
        </div>
        <div class="td-form-group">
          <label for="confirm-pw" class="td-label">Confirm New Password</label>
          <input id="confirm-pw" v-model="confirmNewPassword" type="password" class="td-input" autocomplete="new-password" />
        </div>
        <button type="submit" class="td-btn td-btn--primary" :disabled="submitting">
          {{ submitting ? 'Changing...' : 'Change Password' }}
        </button>
      </form>
    </section>

    <!-- Feature Flags Section -->
    <section class="td-settings__section">
      <h2 class="td-section-title">Feature Flags</h2>
      <p class="td-section-desc">Toggle feature flags to enable or disable new features.</p>
      <div class="td-flags-grid">
        <div v-for="(label, key) in flagLabels" :key="key" class="td-flag-row">
          <label :for="`flag-${key}`" class="td-flag-label">{{ label }}</label>
          <input
            :id="`flag-${key}`"
            type="checkbox"
            :checked="featureFlags.isEnabled(key)"
            @change="featureFlags.setFlag(key, ($event.target as HTMLInputElement).checked)"
            class="td-checkbox"
          />
        </div>
      </div>
      <button class="td-btn td-btn--secondary" @click="featureFlags.resetAll()">Reset All Flags</button>
    </section>
  </div>
</template>

<style scoped>
.td-settings { max-width: 640px; }
.td-page-title { font-size: var(--td-font-2xl); font-weight: 700; margin-bottom: var(--td-space-6); color: var(--td-text-primary); }
.td-settings__section { background: var(--td-surface-primary); border-radius: var(--td-radius-lg); padding: var(--td-space-6); margin-bottom: var(--td-space-4); border: 1px solid var(--td-border-default); }
.td-section-title { font-size: var(--td-font-lg); font-weight: 600; margin-bottom: var(--td-space-4); color: var(--td-text-primary); }
.td-section-desc { font-size: var(--td-font-sm); color: var(--td-text-secondary); margin-bottom: var(--td-space-4); }
.td-info-grid { display: flex; flex-direction: column; gap: var(--td-space-3); }
.td-info-row { display: flex; justify-content: space-between; align-items: center; padding: var(--td-space-2) 0; border-bottom: 1px solid var(--td-border-default); }
.td-info-row--stacked { display: flex; flex-direction: column; align-items: flex-start; justify-content: flex-start; gap: var(--td-space-3); }
.td-info-label { font-size: var(--td-font-sm); color: var(--td-text-secondary); font-weight: 500; }
.td-info-value { font-size: var(--td-font-sm); color: var(--td-text-primary); }
.td-mono { font-family: monospace; font-size: var(--td-font-xs); }
.td-settings__form { display: flex; flex-direction: column; gap: var(--td-space-4); }
.td-alert { padding: var(--td-space-3); border-radius: var(--td-radius-md); font-size: var(--td-font-sm); }
.td-alert--error { background: var(--td-color-error-light); color: var(--td-color-error); }
.td-alert--success { background: var(--td-color-success-light); color: var(--td-color-success); }
.td-form-group { display: flex; flex-direction: column; gap: var(--td-space-1); }
.td-label { font-size: var(--td-font-sm); font-weight: 500; color: var(--td-text-secondary); }
.td-input { padding: var(--td-space-2) var(--td-space-3); border: 1px solid var(--td-border-default); border-radius: var(--td-radius-md); font-size: var(--td-font-base); }
.td-input:focus { outline: none; border-color: var(--td-border-focus); box-shadow: var(--td-focus-ring); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover:not(:disabled) { background: var(--td-color-primary-hover); }
.td-btn--secondary { background: var(--td-surface-tertiary); color: var(--td-text-primary); border: 1px solid var(--td-border-default); margin-top: var(--td-space-4); }
.td-btn--secondary:hover { background: var(--td-surface-hover); }
.td-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.td-flags-grid { display: flex; flex-direction: column; gap: var(--td-space-3); margin-bottom: var(--td-space-4); }
.td-flag-row { display: flex; justify-content: space-between; align-items: center; padding: var(--td-space-2) 0; }
.td-flag-label { font-size: var(--td-font-sm); color: var(--td-text-primary); }
.td-checkbox { width: 18px; height: 18px; cursor: pointer; }
</style>
