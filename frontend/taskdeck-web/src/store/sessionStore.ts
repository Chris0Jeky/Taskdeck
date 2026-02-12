import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi } from '../api/authApi'
import { useToastStore } from './toastStore'
import type { LoginRequest, RegisterRequest, ChangePasswordRequest, SessionState } from '../types/auth'

const TOKEN_KEY = 'taskdeck_token'
const SESSION_KEY = 'taskdeck_session'

export const useSessionStore = defineStore('session', () => {
  const toast = useToastStore()

  const token = ref<string | null>(null)
  const userId = ref<string | null>(null)
  const username = ref<string | null>(null)
  const email = ref<string | null>(null)
  const expiresAt = ref<string | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const isAuthenticated = computed(() => !!token.value)

  const sessionState = computed<SessionState>(() => ({
    token: token.value,
    userId: userId.value,
    username: username.value,
    email: email.value,
    isAuthenticated: isAuthenticated.value,
    expiresAt: expiresAt.value,
  }))

  function setSession(data: { token: string; userId: string; username: string; email: string }) {
    token.value = data.token
    userId.value = data.userId
    username.value = data.username
    email.value = data.email
    try {
      const parts = data.token.split('.')
      const payload = JSON.parse(atob(parts[1] ?? ''))
      expiresAt.value = payload.exp ? new Date(payload.exp * 1000).toISOString() : null
    } catch {
      expiresAt.value = null
    }
    localStorage.setItem(TOKEN_KEY, data.token)
    localStorage.setItem(SESSION_KEY, JSON.stringify({
      userId: data.userId,
      username: data.username,
      email: data.email,
    }))
  }

  function clearSession() {
    token.value = null
    userId.value = null
    username.value = null
    email.value = null
    expiresAt.value = null
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(SESSION_KEY)
  }

  function restoreSession() {
    const savedToken = localStorage.getItem(TOKEN_KEY)
    const savedSession = localStorage.getItem(SESSION_KEY)
    if (savedToken && savedSession) {
      try {
        const session = JSON.parse(savedSession)
        token.value = savedToken
        userId.value = session.userId
        username.value = session.username
        email.value = session.email
        const parts = savedToken.split('.')
        const payload = JSON.parse(atob(parts[1] ?? ''))
        expiresAt.value = payload.exp ? new Date(payload.exp * 1000).toISOString() : null
        if (payload.exp && Date.now() / 1000 > payload.exp) {
          clearSession()
        }
      } catch {
        clearSession()
      }
    }
  }

  async function login(credentials: LoginRequest) {
    try {
      loading.value = true
      error.value = null
      const response = await authApi.login(credentials)
      setSession(response)
      toast.success('Logged in successfully')
      return response
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Login failed')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function register(request: RegisterRequest) {
    try {
      loading.value = true
      error.value = null
      const response = await authApi.register(request)
      setSession(response)
      toast.success('Registration successful')
      return response
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Registration failed')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function changePassword(request: ChangePasswordRequest) {
    try {
      loading.value = true
      error.value = null
      await authApi.changePassword(request)
      toast.success('Password changed successfully')
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Failed to change password')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  function logout() {
    clearSession()
    toast.info('Logged out')
  }

  function getErrorMessage(err: unknown, fallback: string): string {
    if (typeof err !== 'object' || err === null) return fallback
    const typed = err as { response?: { data?: { message?: unknown } }; message?: unknown }
    const responseMessage = typed.response?.data?.message
    if (typeof responseMessage === 'string' && responseMessage.trim().length > 0) return responseMessage
    if (typeof typed.message === 'string' && typed.message.trim().length > 0) return typed.message
    return fallback
  }

  return {
    token,
    userId,
    username,
    email,
    expiresAt,
    loading,
    error,
    isAuthenticated,
    sessionState,
    login,
    register,
    changePassword,
    logout,
    restoreSession,
    clearSession,
  }
})
