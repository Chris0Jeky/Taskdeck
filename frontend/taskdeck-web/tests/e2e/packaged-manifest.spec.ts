import { writeFileSync } from 'node:fs'
import type { ConsoleMessage, Request, Response } from '@playwright/test'
import { expect, test } from '@playwright/test'

const packagedOrigin = requireLoopbackOrigin('TASKDECK_PACKAGED_BASE_URL')
const failurePath = requiredEnv('TASKDECK_PACKAGED_FAILURE_PATH')
const phase = requiredEnv('TASKDECK_PACKAGED_JOURNEY_PHASE')
const manifestPath = '/manifest.webmanifest'
let checkpoint = 'manifest_setup'

test('untouched Windows package loads its same-origin manifest under CSP', async ({ page }) => {
  test.setTimeout(30_000)
  let manifestRequestFailed = false
  let manifestCspViolation = false

  const isPackagedManifest = (rawUrl: string): boolean => {
    const url = new URL(rawUrl)
    return url.origin === packagedOrigin && url.pathname === manifestPath
  }

  const onRequestFailed = (request: Request): void => {
    if (isPackagedManifest(request.url())) {
      manifestRequestFailed = true
    }
  }
  const onConsole = (message: ConsoleMessage): void => {
    const text = message.text().toLowerCase()
    if (
      text.includes('manifest')
      && (text.includes('content security policy') || text.includes('content-security-policy'))
    ) {
      manifestCspViolation = true
    }
  }

  page.on('requestfailed', onRequestFailed)
  page.on('console', onConsole)

  try {
    checkpoint = 'manifest_response'
    const manifestResponsePromise = page.waitForResponse(
      response => isPackagedManifest(response.url()),
      { timeout: 15_000 },
    )
    const navigationResponse = await page.goto('/')
    expect(navigationResponse?.ok()).toBe(true)

    const manifestResponse = await manifestResponsePromise
    assertBrowserManifestResponse(manifestResponse)

    checkpoint = 'manifest_link'
    await expect(page.locator('link[rel="manifest"]')).toHaveAttribute('href', manifestPath)

    // Let Chromium surface any deferred CSP console issue before recording success.
    await page.waitForTimeout(250)
    checkpoint = 'manifest_csp'
    expect(manifestRequestFailed).toBe(false)
    expect(manifestCspViolation).toBe(false)
  } catch {
    writeSanitizedFailure()
    throw new Error(`[packaged desktop] ${phase} failed at a sanitized manifest checkpoint.`)
  } finally {
    page.off('requestfailed', onRequestFailed)
    page.off('console', onConsole)
  }
})

function assertBrowserManifestResponse(response: Response): void {
  checkpoint = 'manifest_status'
  expect(response.ok()).toBe(true)
  expect(response.status()).toBe(200)
  expect(response.request().method()).toBe('GET')
  expect(response.request().resourceType()).toBe('manifest')
}

function writeSanitizedFailure(): void {
  try {
    writeFileSync(
      failurePath,
      `${JSON.stringify({ schemaVersion: 2, phase, outcome: 'failed', checkpoint, httpStatus: null })}\n`,
      { encoding: 'utf8', flag: 'wx' },
    )
  } catch {
    // Another packaged spec may already own the bounded failure receipt.
  }
}

function requireLoopbackOrigin(name: string): string {
  const value = requiredEnv(name)
  const parsed = new URL(value)
  if (
    parsed.protocol !== 'http:'
    || !['127.0.0.1', 'localhost', '[::1]'].includes(parsed.hostname)
    || !parsed.port
    || parsed.pathname !== '/'
    || parsed.search
    || parsed.hash
  ) {
    throw new Error(`[packaged desktop] ${name} must be a loopback HTTP origin.`)
  }
  return parsed.origin
}

function requiredEnv(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`[packaged desktop] ${name} is required.`)
  }
  return value
}
