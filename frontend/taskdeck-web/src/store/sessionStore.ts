import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi } from '../api/authApi'
import { usersApi } from '../api/usersApi'
import { useToastStore } from './toastStore'
import { getErrorMessage } from '../utils/errorMessage'
import type { LoginRequest, RegisterRequest, ChangePasswordRequest, SessionState, AuthResponse } from '../types/auth'
import { getTokenExpiryIso, isTokenExpired } from '../utils/jwt'
import { isDemoMode, isDemoSessionActive, activateDemoSession, clearDemoSession, DEMO_USER } from '../utils/demoMode'

const TOKEN_KEY = 'taskdeck_token'
const SESSION_KEY = 'taskdeck_session'

interface PersistedSession {
  userId: string
  username: string
  email: string
  defaultRole?: number
}

export const useSessionStore = defineStore('session', () => {
  const toast = useToastStore()

  const token = ref<string | null>(null)
  const userId = ref<string | null>(null)
  const username = ref<string | null>(null)
  const email = ref<string | null>(null)
  const defaultRole = ref<number | null>(null)
  const expiresAt = ref<string | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const isDemo = ref(false)

  const isAuthenticated = computed(() => {
    if (isDemo.value) return true
    if (!token.value) return false
    return !isTokenExpired(token.value)
  })

  const sessionState = computed<SessionState>(() => ({
    token: token.value,
    userId: userId.value,
    username: username.value,
    email: email.value,
    defaultRole: defaultRole.value,
    isAuthenticated: isAuthenticated.value,
    expiresAt: expiresAt.value,
  }))

  function persistSessionSnapshot(snapshot: { userId: string; username: string; email: string; defaultRole: number | null }) {
    localStorage.setItem(SESSION_KEY, JSON.stringify(snapshot))
  }

  function setSession(data: AuthResponse) {
    token.value = data.token
    userId.value = data.user.id
    username.value = data.user.username
    email.value = data.user.email
    defaultRole.value = data.user.defaultRole
    expiresAt.value = getTokenExpiryIso(data.token)

    localStorage.setItem(TOKEN_KEY, data.token)
    persistSessionSnapshot({
      userId: data.user.id,
      username: data.user.username,
      email: data.user.email,
      defaultRole: data.user.defaultRole,
    })
  }

  function clearSession() {
    isDemo.value = false
    token.value = null
    userId.value = null
    username.value = null
    email.value = null
    defaultRole.value = null
    expiresAt.value = null
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(SESSION_KEY)
    clearDemoSession()
  }

  async function hydrateDefaultRoleFromProfile(restoredUserId: string, restoredToken: string) {
    try {
      const user = await usersApi.getUser(restoredUserId)
      if (token.value !== restoredToken || userId.value !== restoredUserId) {
        return
      }

      if (typeof user.defaultRole !== 'number') {
        console.warn('Session restore role hydration skipped: profile response did not include a numeric defaultRole.')
        return
      }

      defaultRole.value = user.defaultRole
      persistSessionSnapshot({
        userId: restoredUserId,
        username: username.value ?? user.username,
        email: email.value ?? user.email,
        defaultRole: user.defaultRole,
      })
    } catch (e) {
      console.warn('Session restore role hydration failed.', e)
    }
  }

  function setDemoSession() {
    isDemo.value = true
    token.value = null
    userId.value = DEMO_USER.id
    username.value = DEMO_USER.username
    email.value = DEMO_USER.email
    defaultRole.value = DEMO_USER.defaultRole
    expiresAt.value = null
    activateDemoSession()
  }

  function loginAsDemo() {
    setDemoSession()
    toast.success('Welcome to the Taskdeck demo')
  }

  function restoreSession() {
    if (isDemoMode && isDemoSessionActive()) {
      setDemoSession()
      return
    }

    const savedToken = localStorage.getItem(TOKEN_KEY)
    const savedSession = localStorage.getItem(SESSION_KEY)
    if (savedToken && savedSession) {
      try {
        const session = JSON.parse(savedSession) as PersistedSession
        if (
          typeof session.userId !== 'string'
          || typeof session.username !== 'string'
          || typeof session.email !== 'string'
        ) {
          clearSession()
          return
        }

        token.value = savedToken
        userId.value = session.userId
        username.value = session.username
        email.value = session.email
        defaultRole.value = typeof session.defaultRole === 'number' ? session.defaultRole : null
        expiresAt.value = getTokenExpiryIso(savedToken)
        if (isTokenExpired(savedToken)) {
          clearSession()
          return
        }

        void hydrateDefaultRoleFromProfile(session.userId, savedToken)
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

  function requireUserId(context = 'this action'): string {
    if (userId.value) return userId.value
    const message = `You must be logged in to use ${context}.`
    error.value = message
    throw new Error(message)
  }

  return {
    token,
    userId,
    username,
    email,
    defaultRole,
    expiresAt,
    loading,
    error,
    isAuthenticated,
    isDemo,
    sessionState,
    login,
    loginAsDemo,
    register,
    changePassword,
    logout,
    restoreSession,
    clearSession,
    requireUserId,
  }
})
