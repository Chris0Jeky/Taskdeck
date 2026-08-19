<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
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
  ollama: 'Ollama Local LLM',
}
</script>

<template>
  <div class="paper-profile">
    <header class="paper-profile__hero">
      <span class="tk-eyebrow paper-profile__eyebrow">Settings</span>
      <h1 class="tk-h1 paper-profile__title">Settings</h1>
    </header>

    <!-- Profile Section -->
    <section class="paper-profile__panel">
      <h2 class="tk-h3 paper-profile__panel-title">Profile</h2>
      <div class="paper-profile__info-grid">
        <div class="paper-profile__info-row">
          <span class="paper-profile__info-label">Username</span>
          <span class="paper-profile__info-value">{{ session.username || '—' }}</span>
        </div>
        <div class="paper-profile__info-row">
          <span class="paper-profile__info-label">Email</span>
          <span class="paper-profile__info-value">{{ session.email || '—' }}</span>
        </div>
        <div class="paper-profile__info-row">
          <span class="paper-profile__info-label">User ID</span>
          <span class="paper-profile__info-value paper-profile__mono">{{ session.userId || '—' }}</span>
        </div>
        <div class="paper-profile__info-row">
          <span class="paper-profile__info-label">Role</span>
          <span class="paper-profile__info-value">{{ roleLabel }}</span>
        </div>
        <div class="paper-profile__info-row paper-profile__info-row--stacked">
          <span class="paper-profile__info-label">Ops Access</span>
          <span class="paper-profile__info-value">{{ opsCapabilitySummary }}</span>
        </div>
      </div>
    </section>

    <!-- Linked Accounts Section -->
    <section v-if="githubAvailable && !isDemoMode" class="paper-profile__panel">
      <h2 class="tk-h3 paper-profile__panel-title">Linked Accounts</h2>
      <p class="paper-profile__panel-desc">Connect your GitHub account to sign in with GitHub.</p>

      <div v-if="linkError" class="paper-profile__alert paper-profile__alert--error" role="alert">{{ linkError }}</div>
      <div v-if="linkSuccess" class="paper-profile__alert paper-profile__alert--success" role="status">{{ linkSuccess }}</div>
      <div v-if="linkingGitHub" class="paper-profile__link-loading">Linking GitHub account...</div>

      <div v-if="isGitHubLinked && gitHubAccount" class="paper-profile__linked-account">
        <div class="paper-profile__linked-account-info">
          <img
            v-if="gitHubAccount.avatarUrl"
            :src="gitHubAccount.avatarUrl"
            alt="GitHub avatar"
            class="paper-profile__linked-account-avatar"
          />
          <div class="paper-profile__linked-account-details">
            <span class="tk-eyebrow paper-profile__linked-account-provider">GitHub</span>
            <span class="paper-profile__linked-account-name">{{ gitHubAccount.displayName || gitHubAccount.providerUserId }}</span>
          </div>
        </div>
        <button
          type="button"
          class="paper-profile__danger-btn"
          :disabled="unlinking"
          @click="handleUnlinkGitHub"
        >
          {{ unlinking ? 'Unlinking...' : 'Unlink' }}
        </button>
      </div>

      <div v-else-if="!linkingGitHub" class="paper-profile__link-action">
        <PaperHLBtn class="paper-profile__github-btn" @click="startGitHubLink">
          <template #icon>
            <svg class="paper-profile__github-icon" viewBox="0 0 16 16" width="18" height="18" aria-hidden="true">
              <path fill="currentColor" d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z"/>
            </svg>
          </template>
          Link GitHub Account
        </PaperHLBtn>
      </div>
    </section>

    <!-- Change Password Section -->
    <section class="paper-profile__panel">
      <h2 class="tk-h3 paper-profile__panel-title">Change Password</h2>
      <form @submit.prevent="handleChangePassword" class="paper-profile__form">
        <div v-if="passwordError" class="paper-profile__alert paper-profile__alert--error" role="alert">{{ passwordError }}</div>
        <div v-if="passwordSuccess" class="paper-profile__alert paper-profile__alert--success" role="status">Password changed successfully.</div>

        <div class="paper-profile__form-group">
          <label for="current-pw" class="paper-profile__label">Current Password</label>
          <input id="current-pw" v-model="currentPassword" type="password" class="paper-profile__input" autocomplete="current-password" />
        </div>
        <div class="paper-profile__form-group">
          <label for="new-pw" class="paper-profile__label">New Password</label>
          <input id="new-pw" v-model="newPassword" type="password" class="paper-profile__input" autocomplete="new-password" />
        </div>
        <div class="paper-profile__form-group">
          <label for="confirm-pw" class="paper-profile__label">Confirm New Password</label>
          <input id="confirm-pw" v-model="confirmNewPassword" type="password" class="paper-profile__input" autocomplete="new-password" />
        </div>
        <div class="paper-profile__actions">
          <PaperHLBtn type="submit" variant="ember" :disabled="submitting">
            {{ submitting ? 'Changing...' : 'Change Password' }}
          </PaperHLBtn>
        </div>
      </form>
    </section>

    <!-- Telemetry & Privacy Section -->
    <section class="paper-profile__panel">
      <h2 class="tk-h3 paper-profile__panel-title">Telemetry &amp; Privacy</h2>
      <p class="paper-profile__panel-desc">
        Taskdeck can collect anonymous usage data to help improve the product.
        No personal information, card content, board names, or user-generated text is ever collected.
        Telemetry is <strong>off by default</strong> and requires your explicit opt-in.
      </p>
      <p v-if="telemetry.privacySignalActive" class="paper-profile__status paper-profile__status--dnt">
        Your browser has Do Not Track or Global Privacy Control enabled.
        Telemetry consent is not auto-restored across sessions. You may still opt in below.
      </p>
      <div class="paper-profile__flag-row">
        <label for="telemetry-consent" class="paper-profile__flag-label">
          Enable anonymous telemetry
        </label>
        <input
          id="telemetry-consent"
          type="checkbox"
          :checked="telemetry.consentGiven"
          @change="telemetry.setConsent(($event.target as HTMLInputElement).checked)"
          class="paper-profile__checkbox"
        />
      </div>
      <p v-if="telemetry.consentGiven" class="paper-profile__status paper-profile__status--on">
        Telemetry is enabled. Anonymous usage events will be sent periodically.
      </p>
      <p v-else class="paper-profile__status paper-profile__status--off">
        Telemetry is disabled. No usage data is collected or sent.
      </p>
      <details class="paper-profile__details">
        <summary class="paper-profile__summary">What data is collected?</summary>
        <ul class="paper-profile__list">
          <li>Page navigation events (which pages are visited, not content)</li>
          <li>Feature usage counts (captures, proposals, board loads)</li>
          <li>Error codes (no error messages or stack traces)</li>
          <li>Workspace mode and app version</li>
          <li>Anonymous session ID (rotated on each app restart)</li>
        </ul>
        <p class="paper-profile__note">
          We never collect: card titles, board names, usernames, emails, passwords,
          file paths, or any user-generated content.
        </p>
      </details>
    </section>

    <!-- Feature Flags Section -->
    <section class="paper-profile__panel">
      <h2 class="tk-h3 paper-profile__panel-title">Feature Flags</h2>
      <p class="paper-profile__panel-desc">Toggle feature flags to enable or disable new features.</p>
      <div class="paper-profile__flags-grid">
        <div v-for="(label, key) in flagLabels" :key="key" class="paper-profile__flag-row">
          <label :for="`flag-${key}`" class="paper-profile__flag-label">{{ label }}</label>
          <input
            :id="`flag-${key}`"
            type="checkbox"
            :checked="featureFlags.isEnabled(key)"
            @change="featureFlags.setFlag(key, ($event.target as HTMLInputElement).checked)"
            class="paper-profile__checkbox"
          />
        </div>
      </div>
      <div class="paper-profile__actions">
        <PaperHLBtn @click="featureFlags.resetAll()">Reset All Flags</PaperHLBtn>
      </div>
    </section>
  </div>
</template>

<style scoped>
/* ── Paper & Graphite — ProfileSettingsView ──
   Styled against the Paper token system (--paper, --ink, --ember families).
   Tokens are defined under `.paper` / `.paper-night` (the canonical shell), so
   var() fallbacks keep the surface legible if the view is ever rendered outside
   the Paper shell (Legacy/Obsidian "off" mode). */

.paper-profile {
  display: flex;
  flex-direction: column;
  gap: var(--s-4, 16px);
  max-width: 640px;
  font-family: var(--sans, system-ui, sans-serif);
  color: var(--ink, #1a1814);
}

/* ── Hero ── */

.paper-profile__hero {
  display: flex;
  flex-direction: column;
  gap: var(--s-2, 8px);
}

.paper-profile__eyebrow {
  color: var(--mute, #6c6557);
}

.paper-profile__title {
  margin: 0;
  font-size: var(--t-h2, 32px);
}

/* ── Panels ── */

.paper-profile__panel {
  display: flex;
  flex-direction: column;
  gap: var(--s-3, 12px);
  padding: var(--s-5, 20px);
  border-radius: var(--r-3, 6px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper-card, #fbf7ee);
  box-shadow: var(--shadow-card, 0 1px 0 #d8d0bf);
}

.paper-profile__panel-title {
  margin: 0;
  font-size: var(--t-lg, 18px);
  color: var(--ink-deep, #0a0908);
}

.paper-profile__panel-desc {
  margin: 0;
  font-size: var(--t-sm, 12px);
  color: var(--ink-2, #3a352d);
  line-height: 1.55;
}

/* ── Read-only info rows ── */

.paper-profile__info-grid {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
}

.paper-profile__info-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--s-3, 12px);
  padding: var(--s-2, 8px) 0;
  border-bottom: 1px solid var(--line-soft, #e3dcc9);
}

.paper-profile__info-row--stacked {
  flex-direction: column;
  align-items: flex-start;
  gap: var(--s-2, 8px);
}

.paper-profile__info-label {
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--mute, #6c6557);
}

.paper-profile__info-value {
  font-size: var(--t-md, 13.5px);
  color: var(--ink, #1a1814);
}

.paper-profile__mono {
  font-family: var(--mono, ui-monospace, monospace);
  font-size: var(--t-sm, 12px);
}

/* ── Alerts & status strips ── */

.paper-profile__alert {
  padding: var(--s-3, 12px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  font-size: var(--t-md, 13.5px);
}

.paper-profile__alert--error {
  border-color: var(--overdue, #8c4a26);
  background: var(--overdue-tint, #ecd9c4);
  color: var(--overdue, #8c4a26);
}

.paper-profile__alert--success {
  border-color: var(--applied, #4a6b3f);
  background: var(--applied-tint, #d8e0ce);
  color: var(--applied, #4a6b3f);
}

.paper-profile__status {
  margin: 0;
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  font-size: var(--t-sm, 12px);
}

.paper-profile__status--on {
  border-color: var(--applied, #4a6b3f);
  background: var(--applied-tint, #d8e0ce);
  color: var(--applied, #4a6b3f);
}

.paper-profile__status--off {
  background: var(--paper-2, #ebe5d8);
  color: var(--mute, #6c6557);
}

.paper-profile__status--dnt {
  border-color: var(--overdue, #8c4a26);
  background: var(--overdue-tint, #ecd9c4);
  color: var(--overdue, #8c4a26);
}

/* ── Forms ── */

.paper-profile__form {
  display: flex;
  flex-direction: column;
  gap: var(--s-4, 16px);
}

.paper-profile__form-group {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
}

.paper-profile__label {
  font-size: var(--t-xs, 10.5px);
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--mute, #6c6557);
}

.paper-profile__input {
  width: 100%;
  padding: var(--s-2, 8px) var(--s-3, 12px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--line, #d8d0bf);
  background: var(--paper, #f3eee5);
  color: var(--ink, #1a1814);
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-md, 13.5px);
  transition: border-color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-profile__input:focus {
  outline: none;
  border-color: var(--ember, #a8421f);
  box-shadow: 0 0 0 2px var(--ember-bloom, #a8421f1a);
}

.paper-profile__actions {
  display: flex;
  gap: var(--s-2, 8px);
  flex-wrap: wrap;
}

/* ── Toggles (telemetry + feature flags) ── */

.paper-profile__flags-grid {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
}

.paper-profile__flag-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--s-3, 12px);
  padding: var(--s-2, 8px) 0;
}

.paper-profile__flag-label {
  font-size: var(--t-md, 13.5px);
  color: var(--ink, #1a1814);
  cursor: pointer;
}

.paper-profile__checkbox {
  width: 16px;
  height: 16px;
  accent-color: var(--ember, #a8421f);
  cursor: pointer;
}

/* ── Telemetry disclosure ── */

.paper-profile__details {
  border-top: 1px solid var(--line-soft, #e3dcc9);
  padding-top: var(--s-3, 12px);
}

.paper-profile__summary {
  cursor: pointer;
  font-size: var(--t-sm, 12px);
  font-weight: 600;
  color: var(--ink-2, #3a352d);
}

.paper-profile__list {
  font-size: var(--t-sm, 12px);
  color: var(--ink-2, #3a352d);
  padding-left: var(--s-6, 24px);
  margin-top: var(--s-2, 8px);
  list-style: disc;
}

.paper-profile__list li {
  margin-bottom: var(--s-1, 4px);
}

.paper-profile__note {
  font-size: var(--t-xs, 10.5px);
  color: var(--mute, #6c6557);
  margin-top: var(--s-2, 8px);
  font-style: italic;
}

/* ── Linked accounts ──
   The GitHub action is a Paper hairline button (PaperHLBtn) with the octocat
   mark drawn in currentColor rather than GitHub's brand-black fill: Paper keeps
   a single accent per surface, and the password form already owns the ember. */

.paper-profile__linked-account {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--s-3, 12px);
  padding: var(--s-3, 12px);
  border: 1px solid var(--line, #d8d0bf);
  border-radius: var(--r-2, 4px);
  background: var(--paper, #f3eee5);
}

.paper-profile__linked-account-info {
  display: flex;
  align-items: center;
  gap: var(--s-3, 12px);
}

.paper-profile__linked-account-avatar {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  border: 1px solid var(--line, #d8d0bf);
}

.paper-profile__linked-account-details {
  display: flex;
  flex-direction: column;
  gap: var(--s-1, 4px);
}

.paper-profile__linked-account-provider {
  color: var(--mute, #6c6557);
}

.paper-profile__linked-account-name {
  font-size: var(--t-md, 13.5px);
  font-weight: 500;
  color: var(--ink-deep, #0a0908);
}

.paper-profile__link-action {
  margin-top: var(--s-1, 4px);
}

.paper-profile__github-icon {
  flex-shrink: 0;
}

.paper-profile__link-loading {
  padding: var(--s-3, 12px);
  text-align: center;
  color: var(--mute, #6c6557);
  font-size: var(--t-sm, 12px);
}

/* Destructive action: ember-deep outline, never a saturated red fill. */
.paper-profile__danger-btn {
  padding: var(--s-1, 4px) var(--s-3, 12px);
  border-radius: var(--r-2, 4px);
  border: 1px solid var(--ember-deep, #7a2e15);
  background: transparent;
  color: var(--ember-deep, #7a2e15);
  font-family: var(--sans, system-ui, sans-serif);
  font-size: var(--t-sm, 12px);
  font-weight: 600;
  cursor: pointer;
  transition: background var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-profile__danger-btn:hover:not(:disabled) {
  background: var(--ember-bloom, #a8421f1a);
}

.paper-profile__danger-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
</style>

