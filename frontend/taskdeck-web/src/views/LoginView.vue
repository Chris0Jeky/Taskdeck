<script setup lang="ts">
import { ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useSessionStore } from '../store/sessionStore'

const router = useRouter()
const route = useRoute()
const session = useSessionStore()

const username = ref('')
const password = ref('')
const formError = ref<string | null>(null)
const submitting = ref(false)

async function handleSubmit() {
  formError.value = null
  if (!username.value.trim() || !password.value) {
    formError.value = 'Please enter both username and password.'
    return
  }
  try {
    submitting.value = true
    await session.login({ usernameOrEmail: username.value.trim(), password: password.value })
    const redirectRaw = (route.query.redirect as string) || '/workspace/boards'
    const redirect = redirectRaw.startsWith('/') ? redirectRaw : '/workspace/boards'
    router.push(redirect)
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
    </div>
  </div>
</template>

<style scoped>
.td-auth-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  background: var(--td-surface-secondary);
  padding: var(--td-space-4);
}

.td-auth-card {
  background: var(--td-surface-primary);
  border-radius: var(--td-radius-xl);
  box-shadow: var(--td-shadow-lg);
  padding: var(--td-space-8);
  width: 100%;
  max-width: 400px;
}

.td-auth-title {
  font-size: var(--td-font-2xl);
  font-weight: 700;
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
  transition: border-color var(--td-transition-fast);
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
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-btn--primary:hover:not(:disabled) {
  background: var(--td-color-primary-hover);
}

.td-btn:disabled {
  opacity: 0.6;
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
}
</style>
