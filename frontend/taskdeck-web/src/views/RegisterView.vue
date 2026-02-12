<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useSessionStore } from '../store/sessionStore'

const router = useRouter()
const session = useSessionStore()

const username = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const formError = ref<string | null>(null)
const submitting = ref(false)

async function handleSubmit() {
  formError.value = null
  if (!username.value.trim() || !email.value.trim() || !password.value) {
    formError.value = 'Please fill in all fields.'
    return
  }
  if (password.value !== confirmPassword.value) {
    formError.value = 'Passwords do not match.'
    return
  }
  if (password.value.length < 6) {
    formError.value = 'Password must be at least 6 characters.'
    return
  }
  try {
    submitting.value = true
    await session.register({
      username: username.value.trim(),
      email: email.value.trim(),
      password: password.value,
    })
    router.push('/workspace/boards')
  } catch {
    formError.value = session.error || 'Registration failed. Please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="td-auth-page">
    <div class="td-auth-card">
      <h1 class="td-auth-title">Create an account</h1>

      <form @submit.prevent="handleSubmit" class="td-auth-form">
        <div v-if="formError" class="td-auth-error" role="alert">
          {{ formError }}
        </div>

        <div class="td-form-group">
          <label for="reg-username" class="td-label">Username</label>
          <input
            id="reg-username"
            v-model="username"
            type="text"
            class="td-input"
            placeholder="Choose a username"
            autocomplete="username"
            required
          />
        </div>

        <div class="td-form-group">
          <label for="reg-email" class="td-label">Email</label>
          <input
            id="reg-email"
            v-model="email"
            type="email"
            class="td-input"
            placeholder="Enter your email"
            autocomplete="email"
            required
          />
        </div>

        <div class="td-form-group">
          <label for="reg-password" class="td-label">Password</label>
          <input
            id="reg-password"
            v-model="password"
            type="password"
            class="td-input"
            placeholder="Create a password"
            autocomplete="new-password"
            required
          />
        </div>

        <div class="td-form-group">
          <label for="reg-confirm" class="td-label">Confirm Password</label>
          <input
            id="reg-confirm"
            v-model="confirmPassword"
            type="password"
            class="td-input"
            placeholder="Confirm your password"
            autocomplete="new-password"
            required
          />
        </div>

        <button type="submit" class="td-btn td-btn--primary" :disabled="submitting">
          {{ submitting ? 'Creating account...' : 'Create account' }}
        </button>
      </form>

      <p class="td-auth-footer">
        Already have an account?
        <router-link to="/login" class="td-link">Sign in</router-link>
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
