<script setup lang="ts">
import type { OidcProviderInfo, RegistrationAvailability } from '../types/auth'
import { computed, ref, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useSessionStore } from '../store/sessionStore'
import { authApi } from '../api/authApi'
import { sanitizeInternalRedirect } from '../utils/navigation'
import { isDemoMode } from '../utils/demoMode'
import { normalizeRegistrationAvailability } from '../utils/registrationAvailability'

const router = useRouter()
const route = useRoute()
const session = useSessionStore()

const username = ref('')
const password = ref('')
const formError = ref<string | null>(null)
const submitting = ref(false)
const githubAvailable = ref(false)
const oidcProviders = ref<OidcProviderInfo[]>([])
const registration = ref<RegistrationAvailability | null>(null)
// Fail closed: until we have a validated provider payload we treat registration as
// unknown and never expose the Register link. `registrationChecked` flips true once the
// provider probe resolves or fails, avoiding a flash of the wrong footer state.
const registrationChecked = ref(false)
const oauthExchanging = ref(false)

const registrationAvailable = computed(() => registration.value?.isRegistrationAvailable === true)

function navigateAfterLogin() {
  const redirectRaw = (route.query.redirect as string) || '/workspace/home'
  const redirect = sanitizeInternalRedirect(redirectRaw)
  router.push(redirect)
}

function enterDemo() {
  session.loginAsDemo()
  navigateAfterLogin()
}

async function handleSubmit() {
  formError.value = null
  if (!username.value.trim() || !password.value) {
    formError.value = 'Please enter both username and password.'
    return
  }
  try {
    submitting.value = true
    await session.login({ usernameOrEmail: username.value.trim(), password: password.value })
    navigateAfterLogin()
  } catch {
    formError.value = session.error || 'Login failed. Please try again.'
  } finally {
    submitting.value = false
  }
}

function startGitHubLogin() {
  const apiBase = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api'
  const redirect = [route.query.redirect].flat()[0]
  const returnUrl = redirect
    ? `/login?redirect=${encodeURIComponent(redirect)}`
    : '/login'
  window.location.href = `${apiBase}/auth/github/login?returnUrl=${encodeURIComponent(returnUrl)}`
}

function startOidcLogin(providerName: string) {
  const apiBase = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api'
  const redirect = [route.query.redirect].flat()[0]
  const returnUrl = redirect
    ? `/login?redirect=${encodeURIComponent(redirect)}`
    : '/login'
  window.location.href = `${apiBase}/auth/oidc/${encodeURIComponent(providerName)}/login?returnUrl=${encodeURIComponent(returnUrl)}`
}

async function handleOAuthCode(code: string, provider: string | undefined) {
  oauthExchanging.value = true
  formError.value = null
  try {
    // Both GitHub OAuth and OIDC callbacks share the same short-lived code store.
    // Use the OIDC exchange endpoint for OIDC providers; GitHub exchange otherwise.
    if (provider && provider !== 'github') {
      await session.exchangeOidcCode(code)
    } else {
      await session.exchangeOAuthCode(code)
    }
    navigateAfterLogin()
  } catch {
    formError.value = session.error || 'Sign-in failed. Please try again.'
  } finally {
    oauthExchanging.value = false
  }
}

onMounted(async () => {
  // In demo mode, never process OAuth codes or check providers
  if (isDemoMode) {
    // Clean any stray oauth_code from the URL in case someone crafts a link
    if (route.query.oauth_code) {
      await router.replace({ path: route.path, query: { ...route.query, oauth_code: undefined } })
    }
    return
  }

  // Check for OAuth/OIDC code in query params (returned from callback)
  // Safely extract first value — route.query values can be string | string[]
  const oauthCode = [route.query.oauth_code].flat()[0]
  if (oauthCode) {
    const oauthProvider = [route.query.oauth_provider].flat()[0] ?? undefined
    // Clean the code from the URL to prevent reuse on refresh — await to ensure
    // the URL is updated before the exchange call (prevents confusing errors on refresh)
    await router.replace({ path: route.path, query: { ...route.query, oauth_code: undefined, oauth_provider: undefined } })
    await handleOAuthCode(oauthCode, oauthProvider)
    return
  }

  // Check available auth providers (non-blocking)
  try {
    const providers = await authApi.getProviders()
    githubAvailable.value = providers.gitHub === true
    oidcProviders.value = Array.isArray(providers.oidc) ? providers.oidc : []
    // Fail closed: a missing/malformed/older `registration` payload normalizes to null,
    // so the Register link only appears when availability is explicitly confirmed.
    registration.value = normalizeRegistrationAvailability(providers?.registration)
  } catch {
    // Silently ignore — provider buttons simply won't appear, and registration stays
    // unknown (null) so the Register link is withheld.
    registration.value = null
  } finally {
    registrationChecked.value = true
  }
})
</script>

<template>
  <div class="td-auth-page">
    <div class="td-auth-card">
      <p class="td-auth-eyebrow">Taskdeck · review before action</p>
      <h1 class="td-auth-title">Sign in to Taskdeck</h1>

      <div v-if="isDemoMode" class="td-demo-entry">
        <p class="td-demo-entry__description">
          This is a static demo of Taskdeck. No backend is connected &mdash; explore the UI freely.
        </p>
        <button class="td-btn td-btn--primary td-demo-entry__btn" type="button" @click="enterDemo">
          Enter Demo
        </button>
      </div>

      <template v-if="!isDemoMode">
        <div v-if="oauthExchanging" class="td-oauth-exchanging">
          <p>Completing sign-in...</p>
        </div>

        <template v-else>
          <div v-if="githubAvailable || oidcProviders.length > 0" class="td-oauth-section">
            <button
              v-if="githubAvailable"
              type="button"
              class="td-btn td-btn--github"
              @click="startGitHubLogin"
              :disabled="submitting"
            >
              <svg class="td-github-icon" viewBox="0 0 16 16" width="20" height="20" aria-hidden="true">
                <path fill="currentColor" d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z"/>
              </svg>
              Sign in with GitHub
            </button>

            <button
              v-for="provider in oidcProviders"
              :key="provider.name"
              type="button"
              class="td-btn td-btn--oidc"
              @click="startOidcLogin(provider.name)"
              :disabled="submitting"
            >
              Sign in with {{ provider.displayName }}
            </button>

            <div class="td-auth-divider">
              <span class="td-auth-divider__line"></span>
              <span class="td-auth-divider__text">or</span>
              <span class="td-auth-divider__line"></span>
            </div>
          </div>

          <form @submit.prevent="handleSubmit" class="td-auth-form">
            <div v-if="formError" class="td-auth-error" role="alert">
              {{ formError }}
            </div>

            <div class="td-form-group">
              <label for="login-username" class="td-label">Username or Email</label>
              <input
                id="login-username"
                v-model="username"
                type="text"
                class="td-input"
                placeholder="Enter your username or email"
                autocomplete="username"
                required
              />
            </div>

            <div class="td-form-group">
              <label for="login-password" class="td-label">Password</label>
              <input
                id="login-password"
                v-model="password"
                type="password"
                class="td-input"
                placeholder="Enter your password"
                autocomplete="current-password"
                required
              />
            </div>

            <button type="submit" class="td-btn td-btn--primary" :disabled="submitting">
              {{ submitting ? 'Signing in...' : 'Sign in' }}
            </button>
          </form>

          <p v-if="registrationChecked" class="td-auth-footer">
            <template v-if="registrationAvailable">
              Don't have an account?
              <router-link to="/register" class="td-link">Register</router-link>
            </template>
            <template v-else>
              Registration is closed on this Taskdeck instance.
            </template>
          </p>
        </template>
      </template>
    </div>
  </div>
</template>

<style scoped>
.td-auth-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  min-height: 100dvh;
  background: var(--paper, var(--td-surface-base));
  color: var(--ink, var(--td-text-primary));
  padding: var(--td-space-4);
}

.td-auth-card {
  background: var(--paper-card, var(--td-glass-bg));
  backdrop-filter: blur(var(--td-glass-blur));
  border: 1px solid var(--line, var(--td-border-ghost));
  border-radius: var(--r-2, var(--td-radius-xl));
  box-shadow: var(--shadow-lift, var(--td-shadow-lg));
  padding: var(--td-space-8);
  width: 100%;
  max-width: 400px;
}

.td-auth-eyebrow {
  margin: 0 0 var(--td-space-2);
  color: var(--ember, var(--td-color-primary));
  font-family: var(--mono, ui-monospace, monospace);
  font-size: 0.7rem;
  letter-spacing: 0.08em;
  text-align: center;
  text-transform: uppercase;
}

.td-auth-title {
  font-family: var(--serif, 'Manrope', system-ui, sans-serif);
  font-size: var(--td-font-2xl);
  font-weight: 800;
  letter-spacing: -0.03em;
  text-align: center;
  margin-bottom: var(--td-space-6);
  color: var(--ink-deep, var(--td-text-primary));
}

.td-auth-form {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-auth-error {
  background: var(--overdue-tint, var(--td-color-error-light));
  color: var(--overdue, var(--td-color-error));
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
}

.td-form-group {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-label {
  font-size: var(--td-font-sm);
  font-weight: 500;
  color: var(--ink-2, var(--td-text-secondary));
}

.td-input {
  padding: var(--td-space-2) var(--td-space-3);
  border: 1px solid var(--line, var(--td-border-default));
  border-radius: var(--r-1, var(--td-radius-md));
  font-size: var(--td-font-base);
  background: var(--paper, var(--td-surface-container));
  color: var(--ink, var(--td-text-primary));
  transition: border-color var(--td-transition-fast);
}

.td-input::placeholder {
  color: var(--faint, var(--td-text-tertiary));
}

.td-input:focus {
  outline: none;
  border-color: var(--ember, var(--td-border-focus));
  /* Whole-property fallback: Legacy (no Paper class) keeps its canonical multi-shadow
     focus ring. Substituting --td-focus-ring into a color slot would invalidate the
     whole declaration and drop the ring, so scope the ember bloom ring to Paper only. */
  box-shadow: var(--td-focus-ring);
}
.paper .td-input:focus,
.paper-night .td-input:focus {
  box-shadow: 0 0 0 2px var(--ember-bloom);
}

.td-btn {
  padding: var(--td-space-2) var(--td-space-4);
  border: none;
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-base);
  font-weight: 600;
  cursor: pointer;
  transition: all var(--td-transition-fast);
}

.td-btn--primary {
  background: var(--ember, var(--td-color-ember-glow));
  /* On Paper, --td-on-ember gives a theme-aware on-ember text colour that clears
     4.5:1 on the ember button (base + hover). Legacy falls back to --td-text-inverse. */
  color: var(--td-on-ember, var(--td-text-inverse));
}

.td-btn--primary:hover:not(:disabled) {
  filter: brightness(1.1);
}

.td-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.td-auth-footer {
  text-align: center;
  margin-top: var(--td-space-4);
  font-size: var(--td-font-sm);
  color: var(--ink-2, var(--td-text-secondary));
}

.td-link {
  color: var(--ember, var(--td-color-primary));
  text-decoration: none;
  font-weight: 500;
}

.td-link:hover {
  text-decoration: underline;
  color: var(--ember-deep, var(--td-color-ember-glow));
}

.td-oauth-exchanging {
  text-align: center;
  padding: var(--td-space-6) 0;
  color: var(--td-text-secondary);
  font-size: var(--td-font-base);
}

.td-oauth-section {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
  margin-bottom: var(--td-space-2);
}

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

.td-btn--github:hover:not(:disabled) {
  background: #2f363d;
}

.td-btn--github:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.td-github-icon {
  flex-shrink: 0;
}

.td-btn--oidc {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  padding: var(--td-space-2) var(--td-space-4);
  background: var(--td-surface-container);
  color: var(--td-text-primary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-base);
  font-weight: 600;
  cursor: pointer;
  transition: all var(--td-transition-fast);
}

.td-btn--oidc:hover:not(:disabled) {
  background: var(--td-surface-elevated);
  border-color: var(--td-border-focus);
}

.td-btn--oidc:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.td-auth-divider {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
}

.td-auth-divider__line {
  flex: 1;
  height: 1px;
  background: var(--td-border-default);
}

.td-auth-divider__text {
  font-size: var(--td-font-sm);
  color: var(--td-text-tertiary);
  text-transform: lowercase;
}

.td-demo-entry {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
  text-align: center;
}

.td-demo-entry__description {
  color: var(--td-text-secondary);
  font-size: var(--td-font-base);
  line-height: 1.6;
}

.td-demo-entry__btn {
  width: 100%;
  padding: var(--td-space-3) var(--td-space-4);
  font-size: var(--td-font-lg);
}
</style>
