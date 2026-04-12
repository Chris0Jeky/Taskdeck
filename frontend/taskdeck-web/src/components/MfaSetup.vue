<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { authApi } from '../api/authApi'
import { getErrorMessage } from '../utils/errorMessage'
import type { MfaStatus, MfaSetupResponse } from '../types/auth'

const status = ref<MfaStatus | null>(null)
const setupResponse = ref<MfaSetupResponse | null>(null)
const verifyCode = ref('')
const disableCode = ref('')
const loading = ref(false)
const error = ref<string | null>(null)
const successMessage = ref<string | null>(null)
const showDisableForm = ref(false)

async function loadStatus() {
  try {
    status.value = await authApi.getMfaStatus()
  } catch (e) {
    error.value = getErrorMessage(e, 'Failed to load MFA status')
  }
}

async function startSetup() {
  error.value = null
  successMessage.value = null
  loading.value = true
  try {
    setupResponse.value = await authApi.setupMfa()
  } catch (e) {
    error.value = getErrorMessage(e, 'Failed to start MFA setup')
  } finally {
    loading.value = false
  }
}

async function confirmSetup() {
  if (!verifyCode.value.trim()) {
    error.value = 'Please enter the 6-digit code from your authenticator app'
    return
  }
  error.value = null
  loading.value = true
  try {
    await authApi.confirmMfa({ code: verifyCode.value.trim() })
    successMessage.value = 'MFA has been enabled successfully'
    setupResponse.value = null
    verifyCode.value = ''
    await loadStatus()
  } catch (e) {
    error.value = getErrorMessage(e, 'Invalid verification code')
  } finally {
    loading.value = false
  }
}

async function disableMfa() {
  if (!disableCode.value.trim()) {
    error.value = 'Please enter a verification code to disable MFA'
    return
  }
  error.value = null
  loading.value = true
  try {
    await authApi.disableMfa({ code: disableCode.value.trim() })
    successMessage.value = 'MFA has been disabled'
    disableCode.value = ''
    showDisableForm.value = false
    await loadStatus()
  } catch (e) {
    error.value = getErrorMessage(e, 'Failed to disable MFA')
  } finally {
    loading.value = false
  }
}

onMounted(loadStatus)
</script>

<template>
  <div class="td-mfa-setup">
    <h3 class="td-mfa-setup__title">Two-Factor Authentication</h3>

    <div v-if="error" class="td-mfa-setup__error" role="alert">{{ error }}</div>
    <div v-if="successMessage" class="td-mfa-setup__success" role="status">{{ successMessage }}</div>

    <!-- Status: not available -->
    <div v-if="status && !status.isSetupAvailable" class="td-mfa-setup__info">
      MFA setup is not available on this instance. Contact your administrator.
    </div>

    <!-- Status: not enabled, no setup in progress -->
    <div v-else-if="status && !status.isEnabled && !setupResponse">
      <p class="td-mfa-setup__description">
        Add an extra layer of security to your account by enabling two-factor authentication
        with a TOTP authenticator app.
      </p>
      <button
        class="td-btn td-btn--primary"
        :disabled="loading"
        @click="startSetup"
      >
        {{ loading ? 'Setting up...' : 'Enable MFA' }}
      </button>
    </div>

    <!-- Setup in progress -->
    <div v-else-if="setupResponse" class="td-mfa-setup__wizard">
      <p class="td-mfa-setup__step">
        1. Add this secret to your authenticator app (Google Authenticator, Authy, etc.)
      </p>
      <div class="td-mfa-setup__secret-container">
        <code class="td-mfa-setup__secret">{{ setupResponse.sharedSecret }}</code>
        <p class="td-mfa-setup__hint">
          Copy and paste this secret into your authenticator app.
        </p>
        <details class="td-mfa-setup__provisioning">
          <summary>Show provisioning URI</summary>
          <code class="td-mfa-setup__provisioning-uri">{{ setupResponse.qrCodeUri }}</code>
        </details>
      </div>

      <p class="td-mfa-setup__step">2. Save these recovery codes in a safe place:</p>
      <div class="td-mfa-setup__recovery-codes">
        <code
          v-for="code in setupResponse.recoveryCodes"
          :key="code"
          class="td-mfa-setup__recovery-code"
        >{{ code }}</code>
      </div>

      <p class="td-mfa-setup__step">3. Enter the 6-digit code from your authenticator app:</p>
      <div class="td-mfa-setup__verify-form">
        <label for="mfa-setup-code" class="td-visually-hidden">
          Setup verification code
        </label>
        <input
          id="mfa-setup-code"
          v-model="verifyCode"
          type="text"
          inputmode="numeric"
          pattern="[0-9]*"
          maxlength="6"
          placeholder="000000"
          class="td-input td-mfa-setup__code-input"
          autocomplete="one-time-code"
        />
        <button
          class="td-btn td-btn--primary"
          :disabled="loading || verifyCode.length !== 6"
          @click="confirmSetup"
        >
          {{ loading ? 'Verifying...' : 'Confirm' }}
        </button>
      </div>
    </div>

    <!-- MFA enabled -->
    <div v-else-if="status?.isEnabled">
      <p class="td-mfa-setup__status-enabled">
        Two-factor authentication is <strong>enabled</strong>.
      </p>

      <div v-if="!showDisableForm">
        <button
          class="td-btn td-btn--danger"
          @click="showDisableForm = true"
        >
          Disable MFA
        </button>
      </div>

      <div v-else class="td-mfa-setup__disable-form">
        <p>Enter a verification code to disable MFA:</p>
        <div class="td-mfa-setup__verify-form">
          <label for="mfa-disable-code" class="td-visually-hidden">
            Disable verification code
          </label>
          <input
            id="mfa-disable-code"
            v-model="disableCode"
            type="text"
            inputmode="numeric"
            pattern="[0-9]*"
            maxlength="6"
            placeholder="000000"
            class="td-input td-mfa-setup__code-input"
            autocomplete="one-time-code"
          />
          <button
            class="td-btn td-btn--danger"
            :disabled="loading || disableCode.length < 6"
            @click="disableMfa"
          >
            {{ loading ? 'Disabling...' : 'Confirm Disable' }}
          </button>
          <button
            class="td-btn td-btn--secondary"
            @click="showDisableForm = false; disableCode = ''"
          >
            Cancel
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-mfa-setup {
  max-width: 500px;
}

.td-mfa-setup__title {
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-lg);
  font-weight: 700;
  margin-bottom: var(--td-space-4);
  color: var(--td-text-primary);
}

.td-mfa-setup__description {
  color: var(--td-text-secondary);
  margin-bottom: var(--td-space-4);
  line-height: 1.5;
}

.td-mfa-setup__error {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  margin-bottom: var(--td-space-3);
}

.td-mfa-setup__success {
  background: var(--td-color-success-light, #e6f9e6);
  color: var(--td-color-success, #2d7d2d);
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  margin-bottom: var(--td-space-3);
}

.td-mfa-setup__info {
  color: var(--td-text-tertiary);
  font-size: var(--td-font-sm);
}

.td-mfa-setup__wizard {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-3);
}

.td-mfa-setup__step {
  font-weight: 600;
  color: var(--td-text-primary);
  font-size: var(--td-font-sm);
}

.td-mfa-setup__secret-container {
  background: var(--td-surface-container);
  padding: var(--td-space-4);
  border-radius: var(--td-radius-md);
  text-align: center;
}

.td-mfa-setup__secret {
  font-size: var(--td-font-base);
  letter-spacing: 0.1em;
  word-break: break-all;
  user-select: all;
  color: var(--td-text-primary);
}

.td-mfa-setup__hint {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  margin-top: var(--td-space-2);
}

.td-mfa-setup__provisioning {
  margin-top: var(--td-space-3);
  text-align: left;
}

.td-mfa-setup__provisioning summary {
  cursor: pointer;
  color: var(--td-text-secondary);
  font-size: var(--td-font-xs);
}

.td-mfa-setup__provisioning-uri {
  display: block;
  margin-top: var(--td-space-2);
  font-size: var(--td-font-xs);
  line-height: 1.4;
  word-break: break-all;
  user-select: all;
}

.td-mfa-setup__recovery-codes {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: var(--td-space-2);
  background: var(--td-surface-container);
  padding: var(--td-space-3);
  border-radius: var(--td-radius-md);
}

.td-mfa-setup__recovery-code {
  font-family: monospace;
  font-size: var(--td-font-sm);
  padding: var(--td-space-1) var(--td-space-2);
  background: var(--td-surface-base);
  border-radius: var(--td-radius-sm);
  text-align: center;
  user-select: all;
}

.td-mfa-setup__verify-form {
  display: flex;
  gap: var(--td-space-2);
  align-items: center;
  flex-wrap: wrap;
}

.td-mfa-setup__code-input {
  width: 120px;
  text-align: center;
  font-size: var(--td-font-lg);
  letter-spacing: 0.2em;
}

.td-mfa-setup__status-enabled {
  color: var(--td-text-primary);
  margin-bottom: var(--td-space-4);
}

.td-mfa-setup__disable-form {
  margin-top: var(--td-space-3);
}

.td-btn--danger {
  background: var(--td-color-error);
  color: white;
  padding: var(--td-space-2) var(--td-space-4);
  border: none;
  border-radius: var(--td-radius-md);
  font-weight: 600;
  cursor: pointer;
}

.td-btn--danger:hover:not(:disabled) {
  filter: brightness(1.1);
}

.td-btn--danger:disabled {
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

.td-visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
