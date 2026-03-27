/**
 * Demo mode activates when the app is deployed without a backend
 * (VITE_API_BASE_URL is empty). In this mode, auth is bypassed
 * and the user can explore the UI with mock data.
 */

const DEMO_SESSION_KEY = 'taskdeck_demo'

const normalizedApiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').toString().trim()

export const isDemoMode: boolean = normalizedApiBaseUrl === ''

export function isDemoSessionActive(): boolean {
  return isDemoMode && localStorage.getItem(DEMO_SESSION_KEY) === '1'
}

export function activateDemoSession(): void {
  localStorage.setItem(DEMO_SESSION_KEY, '1')
}

export function clearDemoSession(): void {
  localStorage.removeItem(DEMO_SESSION_KEY)
}

export class DemoModeError extends Error {
  constructor(message = 'This action is view-only in demo mode.') {
    super(message)
    this.name = 'DemoModeError'
  }
}

export const DEMO_USER = {
  id: 'demo-user-0000-0000-000000000000',
  username: 'demo',
  email: 'demo@taskdeck.local',
  defaultRole: 0,
} as const
