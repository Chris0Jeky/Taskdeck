<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useSessionStore } from '../store/sessionStore'
import { authApi } from '../api/authApi'
import { sanitizeInternalRedirect } from '../utils/navigation'
import { isDemoMode } from '../utils/demoMode'

const router = useRouter()
const route = useRoute()
const session = useSessionStore()

const username = ref('')
const password = ref('')
const formError = ref<string | null>(null)
const submitting = ref(false)
const githubAvailable = ref(false)
const oauthExchanging = ref(false)

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

async function handleOAuthCode(code: string) {
  oauthExchanging.value = true
  formError.value = null
  try {
    await session.exchangeOAuthCode(code)
    navigateAfterLogin()
  } catch {
    formError.value = session.error || 'GitHub sign-in failed. Please try again.'
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

  // Check for OAuth code in query params (returned from GitHub callback)
  // Safely extract first value — route.query values can be string | string[]
  const oauthCode = [route.query.oauth_code].flat()[0]
  if (oauthCode) {
    // Clean the code from the URL to prevent reuse on refresh — await to ensure
    // the URL is updated before the exchange call (prevents confusing errors on refresh)
    await router.replace({ path: route.path, query: { ...route.query, oauth_code: undefined } })
    await handleOAuthCode(oauthCode)
    return
  }

  // Check if GitHub OAuth is available (non-blocking)
  try {
    const providers = await authApi.getProviders()
    githubAvailable.value = providers.gitHub === true
  } catch {
    // Silently ignore — GitHub button simply won't appear
  }
})
</script>

<template>
  <div class="td-auth-page">
    <div class="td-auth-card">
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
          <p>Completing GitHub sign-in...</p>
        </div>

        <template v-else>
          <div v-if="githubAvailable" class="td-oauth-section">
            <button
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

          <p class="td-auth-footer">
            Don't have an account?
            <router-link to="/register" class="td-link">Register</router-link>
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
  background: var(--td-surface-base);
  padding: var(--td-space-4);
}

.td-auth-card {
  background: var(--td-glass-bg);
  backdrop-filter: blur(var(--td-glass-blur));
  border: 0.5px solid var(--td-border-ghost);
  border-radius: var(--td-radius-xl);
  box-shadow: var(--td-shadow-lg);
  padding: var(--td-space-8);
  width: 100%;
  max-width: 400px;
}

.td-auth-title {
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-2xl);
  font-weight: 800;
  letter-spacing: -0.03em;
  text-align: center;
  margin-bottom: var(--td-space-6);
  color: var(--td-text-primary);
}

.td-auth-form {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-auth-error {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
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
  color: var(--td-text-secondary);
}

.td-input {
  padding: var(--td-space-2) var(--td-space-3);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-base);
  background: var(--td-surface-container);
  color: var(--td-text-primary);
  transition: border-color var(--td-transition-fast);
}

.td-input::placeholder {
  color: var(--td-text-tertiary);
}

.td-input:focus {
  outline: none;
  border-color: var(--td-border-focus);
  box-shadow: var(--td-focus-ring);
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
  background: var(--td-color-ember-glow);
  color: var(--td-text-inverse);
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
  color: var(--td-text-secondary);
}

.td-link {
  color: var(--td-color-primary);
  text-decoration: none;
  font-weight: 500;
}

.td-link:hover {
  text-decoration: underline;
  color: var(--td-color-ember-glow);
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
