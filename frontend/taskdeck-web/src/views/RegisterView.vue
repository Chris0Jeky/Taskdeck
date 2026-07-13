<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { authApi } from '../api/authApi'
import { useSessionStore } from '../store/sessionStore'
import type { RegistrationAvailability } from '../types/auth'
import { normalizeRegistrationAvailability } from '../utils/registrationAvailability'

const router = useRouter()
const session = useSessionStore()

const username = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const inviteCode = ref('')
const formError = ref<string | null>(null)
const submitting = ref(false)
const registration = ref<RegistrationAvailability | null>(null)
const registrationLoading = ref(true)
const registrationStatusError = ref<string | null>(null)

const registrationClosed = computed(() =>
  registration.value !== null && !registration.value.isRegistrationAvailable,
)
const inviteRequired = computed(() => registration.value?.inviteRequired === true)

async function loadRegistrationAvailability() {
  registrationLoading.value = true
  registrationStatusError.value = null
  try {
    const providers = await authApi.getProviders()
    // Fail closed: a missing/malformed/older `registration` payload normalizes to
    // null, which surfaces the stable "could not check" notice instead of a dead form.
    const normalized = normalizeRegistrationAvailability(providers?.registration)
    registration.value = normalized
    if (normalized === null) {
      registrationStatusError.value = 'Could not check whether this Taskdeck instance accepts new accounts.'
    }
  } catch {
    registration.value = null
    registrationStatusError.value = 'Could not check whether this Taskdeck instance accepts new accounts.'
  } finally {
    registrationLoading.value = false
  }
}

async function handleSubmit() {
  formError.value = null
  if (!registration.value || registrationClosed.value) {
    formError.value = 'Registration is not available on this Taskdeck instance.'
    return
  }
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
  if (inviteRequired.value && !inviteCode.value.trim()) {
    formError.value = 'Enter the invite code supplied by the Taskdeck operator.'
    return
  }
  try {
    submitting.value = true
    await session.register({
      username: username.value.trim(),
      email: email.value.trim(),
      password: password.value,
      ...(inviteRequired.value ? { inviteCode: inviteCode.value.trim() } : {}),
    })
    router.push('/workspace/home')
  } catch {
    formError.value = session.error || 'Registration failed. Please try again.'
  } finally {
    submitting.value = false
  }
}

onMounted(loadRegistrationAvailability)
</script>

<template>
  <div class="td-auth-page">
    <div class="td-auth-card">
      <p class="td-auth-eyebrow">Taskdeck · review before action</p>
      <h1 class="td-auth-title">Create an account</h1>

      <div v-if="registrationLoading" class="td-auth-notice" role="status">
        Checking account availability...
      </div>

      <div v-else-if="registrationStatusError" class="td-auth-notice td-auth-notice--error" role="alert">
        <p>{{ registrationStatusError }}</p>
        <button type="button" class="td-btn td-btn--secondary" @click="loadRegistrationAvailability">
          Try again
        </button>
      </div>

      <div v-else-if="registrationClosed" class="td-auth-notice" role="status">
        <strong>Registration is closed.</strong>
        <p>This Taskdeck instance already has its owner. Ask the operator if you need access.</p>
      </div>

      <form v-else @submit.prevent="handleSubmit" class="td-auth-form">
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

        <div v-if="inviteRequired" class="td-form-group">
          <label for="reg-invite" class="td-label">Invite code</label>
          <input
            id="reg-invite"
            v-model="inviteCode"
            type="text"
            class="td-input"
            placeholder="tdi_..."
            autocomplete="one-time-code"
            spellcheck="false"
            required
          />
          <p class="td-field-help">Use the one-time code supplied by the Taskdeck operator.</p>
        </div>

        <button type="submit" class="td-btn td-btn--primary" :disabled="submitting || !registration">
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
  font-family: var(--serif, 'Manrope', sans-serif);
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
.td-btn--secondary {
  border: 1px solid var(--line, var(--td-border-default));
  background: var(--paper-2, var(--td-surface-container));
  color: var(--ink, var(--td-text-primary));
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
.td-auth-notice {
  padding: var(--td-space-4);
  border: 1px solid var(--line, var(--td-border-default));
  border-radius: var(--r-1, var(--td-radius-md));
  background: var(--paper-2, var(--td-surface-container));
  color: var(--ink-2, var(--td-text-secondary));
  line-height: 1.5;
}
.td-auth-notice p {
  margin: var(--td-space-2) 0 0;
}
.td-auth-notice--error {
  border-color: var(--overdue, var(--td-color-error));
}
.td-auth-notice--error .td-btn {
  margin-top: var(--td-space-3);
}
.td-field-help {
  margin: 0;
  color: var(--mute, var(--td-text-tertiary));
  font-size: var(--td-font-xs);
}
</style>
