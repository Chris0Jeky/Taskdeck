<script setup lang="ts">
import { ref } from 'vue'
import { authApi } from '../api/authApi'
import { getErrorMessage } from '../utils/errorMessage'

const props = defineProps<{
  visible: boolean
  actionLabel?: string
}>()

const emit = defineEmits<{
  verified: []
  cancel: []
}>()

const code = ref('')
const loading = ref(false)
const error = ref<string | null>(null)

async function verify() {
  if (!code.value.trim() || code.value.length < 6) {
    error.value = 'Please enter a 6-digit verification code'
    return
  }
  error.value = null
  loading.value = true
  try {
    await authApi.verifyMfa({ code: code.value.trim() })
    code.value = ''
    emit('verified')
  } catch (e) {
    error.value = getErrorMessage(e, 'Invalid verification code')
  } finally {
    loading.value = false
  }
}

function cancel() {
  code.value = ''
  error.value = null
  emit('cancel')
}
</script>

<template>
  <Teleport to="body">
    <div v-if="visible" class="td-mfa-modal__overlay" @click.self="cancel">
      <div class="td-mfa-modal" role="dialog" aria-modal="true" aria-labelledby="mfa-modal-title">
        <h2 id="mfa-modal-title" class="td-mfa-modal__title">Verification Required</h2>
        <p class="td-mfa-modal__description">
          {{ actionLabel || 'This action' }} requires two-factor verification.
          Enter the code from your authenticator app.
        </p>

        <div v-if="error" class="td-mfa-modal__error" role="alert">{{ error }}</div>

        <form @submit.prevent="verify" class="td-mfa-modal__form">
          <input
            v-model="code"
            type="text"
            inputmode="numeric"
            pattern="[0-9]*"
            maxlength="6"
            placeholder="000000"
            class="td-input td-mfa-modal__code-input"
            autocomplete="one-time-code"
            autofocus
          />
          <div class="td-mfa-modal__actions">
            <button
              type="submit"
              class="td-btn td-btn--primary"
              :disabled="loading || code.length < 6"
            >
              {{ loading ? 'Verifying...' : 'Verify' }}
            </button>
            <button
              type="button"
              class="td-btn td-btn--secondary"
              @click="cancel"
            >
              Cancel
            </button>
          </div>
        </form>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.td-mfa-modal__overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.td-mfa-modal {
  background: var(--td-glass-bg);
  backdrop-filter: blur(var(--td-glass-blur));
  border: 0.5px solid var(--td-border-ghost);
  border-radius: var(--td-radius-xl);
  box-shadow: var(--td-shadow-lg);
  padding: var(--td-space-6);
  width: 100%;
  max-width: 380px;
}

.td-mfa-modal__title {
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-xl);
  font-weight: 700;
  margin-bottom: var(--td-space-2);
  color: var(--td-text-primary);
}

.td-mfa-modal__description {
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
  margin-bottom: var(--td-space-4);
  line-height: 1.5;
}

.td-mfa-modal__error {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
  padding: var(--td-space-2);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  margin-bottom: var(--td-space-3);
}

.td-mfa-modal__form {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4);
}

.td-mfa-modal__code-input {
  width: 100%;
  text-align: center;
  font-size: var(--td-font-2xl);
  letter-spacing: 0.3em;
  padding: var(--td-space-3);
}

.td-mfa-modal__actions {
  display: flex;
  gap: var(--td-space-2);
  justify-content: flex-end;
}

.td-btn--primary {
  background: var(--td-color-ember-glow);
  color: var(--td-text-inverse);
  padding: var(--td-space-2) var(--td-space-4);
  border: none;
  border-radius: var(--td-radius-md);
  font-weight: 600;
  cursor: pointer;
}

.td-btn--primary:hover:not(:disabled) {
  filter: brightness(1.1);
}

.td-btn--primary:disabled,
.td-btn--secondary:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.td-btn--secondary {
  background: var(--td-surface-container);
  color: var(--td-text-secondary);
  padding: var(--td-space-2) var(--td-space-4);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  font-weight: 600;
  cursor: pointer;
}

.td-btn--secondary:hover:not(:disabled) {
  background: var(--td-surface-elevated);
}
</style>
