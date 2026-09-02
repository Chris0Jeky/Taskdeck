import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi } from '../api/authApi'
import { usersApi } from '../api/usersApi'
import { useToastStore } from './toastStore'
import { getErrorMessage } from '../utils/errorMessage'
import type { LoginRequest, RegisterRequest, ChangePasswordRequest, SessionState, AuthResponse } from '../types/auth'
import { getTokenExpiryIso, isTokenExpired } from '../utils/jwt'
import { isDemoMode, isDemoSessionActive, activateDemoSession, clearDemoSession, DEMO_USER } from '../utils/demoMode'
import * as tokenStorage from '../utils/tokenStorage'
import { logWarn } from '../utils/errorReporting'
import { proposalDisplayNames } from '../composables/useProposalDisplayNames'
import { purgeLegacyApiCaches } from '../pwa/legacyApiCache'
import { getErrorDetails } from '../composables/useErrorMapper'

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
  let loginFailureToastId: string | null = null

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
    tokenStorage.setSession({
      userId: snapshot.userId,
      username: snapshot.username,
      email: snapshot.email,
      defaultRole: typeof snapshot.defaultRole === 'number' ? snapshot.defaultRole : undefined,
    })
  }

  /**
   * Why a session was not established. The caller needs the distinction: an
   * unusable token and a browser that could not clear the retired offline cache
   * have different recoveries.
   */
  type SetSessionOutcome = 'established' | 'invalid-token' | 'cache-boundary'

  async function setSession(data: AuthResponse): Promise<SetSessionOutcome> {
    if (!tokenStorage.isValidJwtStructure(data.token)) {
      logWarn('Received token with invalid JWT structure — session not persisted.')
      return 'invalid-token'
    }

    // A replacement token is usable only after the legacy cache namespace is
    // gone, including a refresh for the same user.
    if (!(await purgeLegacyApiCaches())) {
      return 'cache-boundary'
    }

    if (userId.value !== data.user.id) proposalDisplayNames.reset()
    token.value = data.token
    userId.value = data.user.id
    username.value = data.user.username
    email.value = data.user.email
    defaultRole.value = data.user.defaultRole
    expiresAt.value = getTokenExpiryIso(data.token)

    tokenStorage.setToken(data.token)
    persistSessionSnapshot({
      userId: data.user.id,
      username: data.user.username,
      email: data.user.email,
      defaultRole: data.user.defaultRole,
    })
    return 'established'
  }

  function clearSession() {
    proposalDisplayNames.reset()
    isDemo.value = false
    token.value = null
    userId.value = null
    username.value = null
    email.value = null
    defaultRole.value = null
    expiresAt.value = null
    tokenStorage.clearAll()
    clearDemoSession()
    // Credential removal is synchronous. A following identity establishment
    // awaits this deduplicated purge before it can issue authenticated reads.
    void purgeLegacyApiCaches()
  }

  // Raised before a credential is sent, and for a token this client cannot use at
  // all: nothing has committed yet, or nothing about the browser will change, so
  // retrying is the right advice.
  const SESSION_NOT_ESTABLISHED = 'Unable to establish a session safely. Please retry.'
  // Raised only when the server has already acted and the browser could not clear
  // the retired offline cache. Retrying the same call would fail on a duplicate
  // username or a consumed invite, so the recovery is a reload and a sign-in.
  const CACHE_BOUNDARY_AFTER_COMMIT =
    'Signed in on the server, but this browser could not clear a retired offline cache. Reload the page and sign in again.'
  const CACHE_BOUNDARY_AFTER_REGISTER =
    'Your account was created, but this browser could not clear a retired offline cache. Reload the page and sign in.'

  function requireEstablished(outcome: SetSessionOutcome, afterCommitMessage: string): void {
    if (outcome === 'established') return
    throw new Error(outcome === 'cache-boundary' ? afterCommitMessage : SESSION_NOT_ESTABLISHED)
  }

  async function requireLegacyApiCachePurge(): Promise<void> {
    if (!await purgeLegacyApiCaches()) {
      throw new Error(SESSION_NOT_ESTABLISHED)
    }
  }

  async function hydrateDefaultRoleFromProfile(restoredUserId: string, restoredToken: string) {
    try {
      const user = await usersApi.getUser(restoredUserId)
      if (token.value !== restoredToken || userId.value !== restoredUserId) {
        return
      }

      if (typeof user.defaultRole !== 'number') {
        logWarn('Session restore role hydration skipped: profile response did not include a numeric defaultRole.')
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
      logWarn('Session restore role hydration failed.', e)
    }
  }

  function setDemoSession() {
    if (userId.value !== DEMO_USER.id) proposalDisplayNames.reset()
    tokenStorage.clearAll()
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

  async function restoreSession() {
    if (isDemoMode && isDemoSessionActive()) {
      setDemoSession()
      return
    }

    const savedToken = tokenStorage.getToken()
    const session = tokenStorage.getSession()
    if (savedToken && session) {
      if (isTokenExpired(savedToken)) {
        clearSession()
        return
      }

      if (!await purgeLegacyApiCaches()) {
        clearSession()
        return
      }

      if (userId.value !== session.userId) proposalDisplayNames.reset()
      token.value = savedToken
      userId.value = session.userId
      username.value = session.username
      email.value = session.email
      defaultRole.value = typeof session.defaultRole === 'number' ? session.defaultRole : null
      expiresAt.value = getTokenExpiryIso(savedToken)
      void hydrateDefaultRoleFromProfile(session.userId, savedToken)
    } else if (savedToken && !session) {
      // Token exists but session metadata is missing or corrupt — clean up
      tokenStorage.clearAll()
    }
  }

  function clearLoginFailureReceipt() {
    if (!loginFailureToastId) return
    toast.remove(loginFailureToastId)
    loginFailureToastId = null
  }

  function replaceLoginFailureReceipt(message: string, cause: unknown) {
    clearLoginFailureReceipt()

    const details = getErrorDetails(cause)
    loginFailureToastId = details
      ? toast.error(message, 0, { details })
      : toast.error(message)
  }

  async function login(credentials: LoginRequest) {
    try {
      loading.value = true
      error.value = null
      await requireLegacyApiCachePurge()
      const response = await authApi.login(credentials)
      requireEstablished(await setSession(response), CACHE_BOUNDARY_AFTER_COMMIT)
      clearLoginFailureReceipt()
      toast.success('Logged in successfully')
      return response
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'Login failed')
      error.value = msg
      replaceLoginFailureReceipt(msg, e)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function register(request: RegisterRequest) {
    try {
      loading.value = true
      error.value = null
      await requireLegacyApiCachePurge()
      const response = await authApi.register(request)
      requireEstablished(await setSession(response), CACHE_BOUNDARY_AFTER_REGISTER)
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

  async function exchangeOAuthCode(code: string) {
    try {
      loading.value = true
      error.value = null
      await requireLegacyApiCachePurge()
      const response = await authApi.exchangeOAuthCode(code)
      requireEstablished(await setSession(response), CACHE_BOUNDARY_AFTER_COMMIT)
      toast.success('Signed in with GitHub')
      return response
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'GitHub sign-in failed')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  async function exchangeOidcCode(code: string) {
    try {
      loading.value = true
      error.value = null
      await requireLegacyApiCachePurge()
      const response = await authApi.exchangeOidcCode(code)
      requireEstablished(await setSession(response), CACHE_BOUNDARY_AFTER_COMMIT)
      toast.success('Signed in successfully')
      return response
    } catch (e: unknown) {
      const msg = getErrorMessage(e, 'SSO sign-in failed')
      error.value = msg
      toast.error(msg)
      throw e
    } finally {
      loading.value = false
    }
  }

  /**
   * Silently refresh the session by calling the backend token refresh endpoint.
   * Updates all session state and persistence through the canonical `setSession` path.
   * Throws on failure so callers can handle the error (e.g. show a warning toast).
   */
  async function refreshSession(): Promise<void> {
    const response = await authApi.refreshToken()
    requireEstablished(
      await setSession(response),
      'Refreshed on the server, but this browser could not clear a retired offline cache. Reload the page and sign in again.',
    )
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
    exchangeOAuthCode,
    exchangeOidcCode,
    refreshSession,
    logout,
    restoreSession,
    clearSession,
    requireUserId,
  }
})
