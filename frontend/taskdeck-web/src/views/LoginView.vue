<script setup lang="ts">
import { ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useSessionStore } from '../store/sessionStore'
import { sanitizeInternalRedirect } from '../utils/navigation'
import { isDemoMode } from '../utils/demoMode'

const router = useRouter()
const route = useRoute()
const session = useSessionStore()

const username = ref('')
const password = ref('')
const formError = ref<string | null>(null)
const submitting = ref(false)

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
