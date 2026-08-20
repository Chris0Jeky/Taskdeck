import { defineConfig, devices } from '@playwright/test'

const baseURL = requireHttpOrigin('TASKDECK_PACKAGED_BASE_URL')
const apiBaseUrl = requireHttpOrigin('TASKDECK_E2E_API_BASE_URL', true)

for (const key of [
  'Llm__OpenAi__ApiKey',
  'OPENAI_API_KEY',
  'TASKDECK_DEMO_OPENAI_API_KEY',
  'TASKDECK_RELEASE_OPENAI_API_KEY',
]) {
  if (process.env[key]?.trim()) {
    throw new Error('[packaged desktop] The Playwright child must not receive an OpenAI key.')
  }
}

process.env.TASKDECK_E2E_API_BASE_URL = apiBaseUrl

export default defineConfig({
  testDir: './tests/e2e',
  testMatch: 'packaged-desktop.spec.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 120_000,
  reporter: 'dot',
  use: {
    ...devices['Desktop Chrome'],
    baseURL,
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
})

function requireHttpOrigin(name: string, allowApiPath = false): string {
  const raw = process.env[name]?.trim()
  if (!raw) {
    throw new Error(`[packaged desktop] ${name} is required.`)
  }

  const parsed = new URL(raw)
  if (parsed.protocol !== 'http:' || parsed.search || parsed.hash) {
    throw new Error(`[packaged desktop] ${name} must be an absolute loopback HTTP URL.`)
  }
  if (!['127.0.0.1', 'localhost', '[::1]'].includes(parsed.hostname)) {
    throw new Error(`[packaged desktop] ${name} must use a loopback host.`)
  }
  if (!parsed.port) {
    throw new Error(`[packaged desktop] ${name} must include the actual listening port.`)
  }
  if (!allowApiPath && parsed.pathname !== '/') {
    throw new Error(`[packaged desktop] ${name} must be an origin without a path.`)
  }
  if (allowApiPath && parsed.pathname.replace(/\/+$/, '') !== '/api') {
    throw new Error(`[packaged desktop] ${name} must end in /api.`)
  }

  return allowApiPath ? `${parsed.origin}/api` : parsed.origin
}
