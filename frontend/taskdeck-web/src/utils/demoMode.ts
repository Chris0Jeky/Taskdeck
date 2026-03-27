/**
 * Demo mode activates when the app is deployed without a backend
 * (VITE_API_BASE_URL is empty). In this mode, auth is bypassed
 * and the user can explore the UI with mock data.
 */

const DEMO_SESSION_KEY = 'taskdeck_demo'

export const isDemoMode: boolean = import.meta.env.VITE_API_BASE_URL === ''

export function isDemoSessionActive(): boolean {
  return isDemoMode && localStorage.getItem(DEMO_SESSION_KEY) === '1'
}

export function activateDemoSession(): void {
  localStorage.setItem(DEMO_SESSION_KEY, '1')
}

export function clearDemoSession(): void {
  localStorage.removeItem(DEMO_SESSION_KEY)
}

export const DEMO_USER = {
  id: 'demo-user-0000-0000-000000000000',
  username: 'demo',
  email: 'demo@taskdeck.local',
  defaultRole: 0,
} as const
