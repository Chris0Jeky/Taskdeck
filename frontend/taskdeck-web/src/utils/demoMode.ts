/**
 * Demo mode activates when the app is deployed without a reachable backend.
 *
 * Detection (first match wins), implemented in `apiBaseUrl.ts`:
 * 1. `VITE_DEMO_MODE` is `1` / `true` / `yes` (Pages may set this explicitly).
 * 2. `VITE_API_BASE_URL` is empty — the GitHub Pages build already does this.
 * 3. The page is on GitHub Pages / `*.pages.dev` AND the configured API base
 *    is still loopback (`localhost` / `127.0.0.1`). That covers a Pages
 *    bundle that accidentally inlined the local-dev default.
 *
 * Local Vite with `.env` pointing at `http://localhost:5000/api` stays live.
 * A later hosted backend on Pages is live as soon as `VITE_API_BASE_URL` is a
 * non-loopback URL. Auth is bypassed in demo mode; the UI uses mock data.
 */

import { shouldUseDemoMode } from './apiBaseUrl'

export { DEMO_TEAMMATE, DEMO_USER } from './demoIdentity'

const DEMO_SESSION_KEY = 'taskdeck_demo'

export const isDemoMode: boolean = shouldUseDemoMode()

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
