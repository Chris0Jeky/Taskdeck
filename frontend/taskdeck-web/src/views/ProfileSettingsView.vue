<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useSessionStore } from '../store/sessionStore'
import { useFeatureFlagStore } from '../store/featureFlagStore'
import { useTelemetryStore } from '../store/telemetryStore'
import { authApi } from '../api/authApi'
import type { FeatureFlags } from '../types/feature-flags'
import type { LinkedAccount } from '../types/auth'
import { getErrorDisplay } from '../composables/useErrorMapper'
import { normalizeBoardRole } from '../utils/roles'
import { isDemoMode } from '../utils/demoMode'

const router = useRouter()
const route = useRoute()
const session = useSessionStore()
const featureFlags = useFeatureFlagStore()
const telemetry = useTelemetryStore()

const currentPassword = ref('')
const newPassword = ref('')
const confirmNewPassword = ref('')
const passwordError = ref<string | null>(null)
const passwordSuccess = ref(false)
const submitting = ref(false)

// Account linking state
const githubAvailable = ref(false)
const linkedAccounts = ref<LinkedAccount[]>([])
const linkingGitHub = ref(false)
const linkError = ref<string | null>(null)
const linkSuccess = ref<string | null>(null)
const unlinking = ref(false)

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

const isGitHubLinked = computed(() =>
  linkedAccounts.value.some(a => a.provider === 'GitHub')
)

const gitHubAccount = computed(() =>
  linkedAccounts.value.find(a => a.provider === 'GitHub')
)

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
    session.requireUserId('password changes')
    await session.changePassword({
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

function startGitHubLink() {
  const apiBase = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api'
  // Return to the profile settings page after OAuth completes.
  // Note: mode=link is NOT passed — the backend derives link/login mode from
  // server-side auth state (JWT presence) to prevent user-controlled bypass.
  const returnUrl = '/workspace/settings/profile'
  window.location.href = `${apiBase}/auth/github/login?returnUrl=${encodeURIComponent(returnUrl)}`
}

async function handleLinkCode(code: string) {
  linkingGitHub.value = true
  linkError.value = null
  linkSuccess.value = null
  try {
    const linked = await authApi.linkGitHub(code)
    linkedAccounts.value.push(linked)
    linkSuccess.value = `GitHub account linked successfully (${linked.displayName || linked.providerUserId})`
  } catch (e: unknown) {
    linkError.value = getErrorDisplay(e, 'Failed to link GitHub account.').message
  } finally {
    linkingGitHub.value = false
  }
}

async function handleUnlinkGitHub() {
  unlinking.value = true
  linkError.value = null
  linkSuccess.value = null
  try {
    await authApi.unlinkGitHub()
    linkedAccounts.value = linkedAccounts.value.filter(a => a.provider !== 'GitHub')
    linkSuccess.value = 'GitHub account unlinked successfully'
  } catch (e: unknown) {
    linkError.value = getErrorDisplay(e, 'Failed to unlink GitHub account.').message
  } finally {
    unlinking.value = false
  }
}

async function loadLinkedAccounts() {
  if (isDemoMode) return
  try {
    linkedAccounts.value = await authApi.getLinkedAccounts()
  } catch {
    // Non-blocking — just won't show linked accounts
  }
}

async function loadProviders() {
  if (isDemoMode) return
  try {
    const providers = await authApi.getProviders()
    githubAvailable.value = providers.gitHub === true
  } catch {
    // Silently ignore
  }
}

onMounted(async () => {
  // Check for OAuth link code in query params
  const linkCode = [route.query.oauth_link_code].flat()[0]
  if (linkCode) {
    await router.replace({ path: route.path, query: { ...route.query, oauth_link_code: undefined } })
    await handleLinkCode(linkCode)
  }

  // Load linked accounts and provider availability in parallel
  await Promise.all([loadLinkedAccounts(), loadProviders()])
})

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

    <!-- Linked Accounts Section -->
    <section v-if="githubAvailable && !isDemoMode" class="td-settings__section">
      <h2 class="td-section-title">Linked Accounts</h2>
      <p class="td-section-desc">Connect your GitHub account to sign in with GitHub.</p>

      <div v-if="linkError" class="td-alert td-alert--error" role="alert">{{ linkError }}</div>
      <div v-if="linkSuccess" class="td-alert td-alert--success" role="status">{{ linkSuccess }}</div>
      <div v-if="linkingGitHub" class="td-link-loading">Linking GitHub account...</div>

      <div v-if="isGitHubLinked && gitHubAccount" class="td-linked-account">
        <div class="td-linked-account__info">
          <img
            v-if="gitHubAccount.avatarUrl"
            :src="gitHubAccount.avatarUrl"
            alt="GitHub avatar"
            class="td-linked-account__avatar"
          />
          <div class="td-linked-account__details">
            <span class="td-linked-account__provider">GitHub</span>
            <span class="td-linked-account__name">{{ gitHubAccount.displayName || gitHubAccount.providerUserId }}</span>
          </div>
        </div>
        <button
          type="button"
          class="td-btn td-btn--danger-outline"
          :disabled="unlinking"
          @click="handleUnlinkGitHub"
        >
          {{ unlinking ? 'Unlinking...' : 'Unlink' }}
        </button>
      </div>

      <div v-else-if="!linkingGitHub" class="td-link-action">
        <button
          type="button"
          class="td-btn td-btn--github"
          @click="startGitHubLink"
        >
          <svg class="td-github-icon" viewBox="0 0 16 16" width="20" height="20" aria-hidden="true">
            <path fill="currentColor" d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z"/>
          </svg>
          Link GitHub Account
        </button>
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

    <!-- Telemetry & Privacy Section -->
    <section class="td-settings__section">
      <h2 class="td-section-title">Telemetry &amp; Privacy</h2>
      <p class="td-section-desc">
        Taskdeck can collect anonymous usage data to help improve the product.
        No personal information, card content, board names, or user-generated text is ever collected.
        Telemetry is <strong>off by default</strong> and requires your explicit opt-in.
      </p>
      <p v-if="telemetry.privacySignalActive" class="td-telemetry-status td-telemetry-status--dnt">
        Your browser has Do Not Track or Global Privacy Control enabled.
        Telemetry consent is not auto-restored across sessions. You may still opt in below.
      </p>
      <div class="td-flag-row">
        <label for="telemetry-consent" class="td-flag-label">
          Enable anonymous telemetry
        </label>
        <input
          id="telemetry-consent"
          type="checkbox"
          :checked="telemetry.consentGiven"
          @change="telemetry.setConsent(($event.target as HTMLInputElement).checked)"
          class="td-checkbox"
        />
      </div>
      <p v-if="telemetry.consentGiven" class="td-telemetry-status td-telemetry-status--on">
        Telemetry is enabled. Anonymous usage events will be sent periodically.
      </p>
      <p v-else class="td-telemetry-status td-telemetry-status--off">
        Telemetry is disabled. No usage data is collected or sent.
      </p>
      <details class="td-telemetry-details">
        <summary class="td-telemetry-summary">What data is collected?</summary>
        <ul class="td-telemetry-list">
          <li>Page navigation events (which pages are visited, not content)</li>
          <li>Feature usage counts (captures, proposals, board loads)</li>
          <li>Error codes (no error messages or stack traces)</li>
          <li>Workspace mode and app version</li>
          <li>Anonymous session ID (rotated on each app restart)</li>
        </ul>
        <p class="td-telemetry-note">
          We never collect: card titles, board names, usernames, emails, passwords,
          file paths, or any user-generated content.
        </p>
      </details>
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
.td-telemetry-status { font-size: var(--td-font-sm); margin-top: var(--td-space-3); padding: var(--td-space-2) var(--td-space-3); border-radius: var(--td-radius-md); }
.td-telemetry-status--on { background: var(--td-color-success-light); color: var(--td-color-success); }
.td-telemetry-status--off { background: var(--td-surface-tertiary); color: var(--td-text-secondary); }
.td-telemetry-status--dnt { background: var(--td-color-warning-light, #fef3cd); color: var(--td-color-warning, #856404); }
.td-telemetry-details { margin-top: var(--td-space-4); }
.td-telemetry-summary { cursor: pointer; font-size: var(--td-font-sm); color: var(--td-text-secondary); font-weight: 500; }
.td-telemetry-list { font-size: var(--td-font-sm); color: var(--td-text-secondary); padding-left: var(--td-space-6); margin-top: var(--td-space-2); list-style: disc; }
.td-telemetry-list li { margin-bottom: var(--td-space-1); }
.td-telemetry-note { font-size: var(--td-font-xs); color: var(--td-text-tertiary); margin-top: var(--td-space-2); font-style: italic; }

/* GitHub button styling */
.td-btn--github {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--td-space-2);
  width: 100%;
  padding: var(--td-space-2) var(--td-space-4);
  background: #24292f;
  color: #ffffff;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-base);
  font-weight: 600;
  cursor: pointer;
  transition: all var(--td-transition-fast);
}
.td-btn--github:hover:not(:disabled) { background: #2f363d; }
.td-btn--github:disabled { opacity: 0.4; cursor: not-allowed; }
.td-github-icon { flex-shrink: 0; }

/* Danger outline button for unlinking */
.td-btn--danger-outline {
  background: transparent;
  color: var(--td-color-error);
  border: 1px solid var(--td-color-error);
  padding: var(--td-space-1) var(--td-space-3);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  font-weight: 600;
  cursor: pointer;
  transition: all var(--td-transition-fast);
}
.td-btn--danger-outline:hover:not(:disabled) { background: var(--td-color-error-light); }
.td-btn--danger-outline:disabled { opacity: 0.6; cursor: not-allowed; }

/* Linked account display */
.td-linked-account {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--td-space-3);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-container);
}
.td-linked-account__info { display: flex; align-items: center; gap: var(--td-space-3); }
.td-linked-account__avatar { width: 32px; height: 32px; border-radius: 50%; }
.td-linked-account__details { display: flex; flex-direction: column; }
.td-linked-account__provider { font-size: var(--td-font-xs); color: var(--td-text-tertiary); text-transform: uppercase; letter-spacing: 0.05em; }
.td-linked-account__name { font-size: var(--td-font-sm); font-weight: 500; color: var(--td-text-primary); }
.td-link-action { margin-top: var(--td-space-2); }
.td-link-loading { padding: var(--td-space-3); text-align: center; color: var(--td-text-secondary); font-size: var(--td-font-sm); }
</style>
