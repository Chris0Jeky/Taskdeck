/**
 * API base URL resolution and static-demo detection.
 *
 * Local Vite (`.env` → `http://localhost:5000/api`) keeps talking to a real
 * local backend. GitHub Pages and other static hosts must never fall through
 * to that loopback default: empty `VITE_API_BASE_URL`, an explicit
 * `VITE_DEMO_MODE` flag, or a Pages origin still pointing at loopback all
 * select demo/static mode instead.
 */

export const LOCAL_DEV_API_BASE_URL = 'http://localhost:5000/api'

export interface DemoModeDetectionInput {
  apiBase?: string
  hostname?: string
  demoFlag?: boolean
}

function isTruthyEnvFlag(value: unknown): boolean {
  const normalized = String(value ?? '').trim().toLowerCase()
  return normalized === '1' || normalized === 'true' || normalized === 'yes'
}

export function readViteApiBaseUrl(): string {
  return (import.meta.env.VITE_API_BASE_URL ?? '').toString().trim()
}

export function readViteDemoFlag(): boolean {
  return isTruthyEnvFlag(import.meta.env.VITE_DEMO_MODE)
}

export function readWindowHostname(): string {
  if (typeof window === 'undefined' || !window.location) return ''
  return (window.location.hostname ?? '').toString().trim().toLowerCase()
}

export function isStaticHostedDemoOrigin(hostname: string): boolean {
  const host = hostname.trim().toLowerCase()
  if (!host) return false
  return host === 'github.io' || host.endsWith('.github.io') || host.endsWith('.pages.dev')
}

export function isLoopbackApiBase(apiBase: string): boolean {
  const trimmed = apiBase.trim()
  if (!trimmed) return false
  // Same-origin relative bases (`/api`, `/Taskdeck/api`) are never loopback.
  if (trimmed.startsWith('/') && !trimmed.startsWith('//')) return false
  try {
    const url = new URL(trimmed)
    const host = url.hostname.toLowerCase()
    return host === 'localhost' || host === '127.0.0.1' || host === '::1'
  } catch {
    return /^(https?:\/\/)?(localhost|127\.0\.0\.1|\[::1\])\b/i.test(trimmed)
  }
}

export function shouldUseDemoMode(input: DemoModeDetectionInput = {}): boolean {
  const demoFlag = input.demoFlag ?? readViteDemoFlag()
  if (demoFlag) return true

  const apiBase = input.apiBase ?? readViteApiBaseUrl()
  if (apiBase === '') return true

  const hostname = input.hostname ?? readWindowHostname()
  return isStaticHostedDemoOrigin(hostname) && isLoopbackApiBase(apiBase)
}

/**
 * Axios `baseURL` for this build. Demo/static mode returns `''` so a missed
 * mock cannot fall through to `localhost:5000`. Local-dev still defaults to
 * the loopback API when `VITE_API_BASE_URL` is unset.
 */
export function resolveApiBaseUrl(input: DemoModeDetectionInput = {}): string {
  if (shouldUseDemoMode(input)) return ''
  const apiBase = input.apiBase ?? readViteApiBaseUrl()
  return apiBase || LOCAL_DEV_API_BASE_URL
}
