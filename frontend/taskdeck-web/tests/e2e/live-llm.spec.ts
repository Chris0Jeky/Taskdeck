import { expect, test } from '@playwright/test'
import { parseTrueishEnv } from '../../scripts/demo-shared.mjs'
import { registerAndAttachSession } from './support/authSession'

test.describe('live llm chat', () => {
  test.skip(
    !parseTrueishEnv(process.env.TASKDECK_RUN_LIVE_LLM_TESTS),
    'Set TASKDECK_RUN_LIVE_LLM_TESTS=1 to run the opt-in live-provider probe.',
  )

  test('first chat turn should use a live provider and answer the prompt directly', async ({ page, request }) => {
    await registerAndAttachSession(page, request, 'live-llm')

    await page.goto('/workspace/automations/chat')
    await expect(page.locator('[data-llm-health-state="configured"]')).toBeVisible()
    await expect(page.getByText('Live LLM configured')).toBeVisible()

    await page.getByRole('button', { name: 'Verify LLM' }).click()
    await expect(page.locator('[data-llm-health-state="verified"]')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('Live LLM verified')).toBeVisible()

    const probeToken = `LIVE_LLM_PROBE_${Date.now()}`

    await page.getByPlaceholder('Session title').fill(`Live LLM ${Date.now()}`)
    await page.getByRole('button', { name: 'Create Session' }).click()

    await page.getByPlaceholder('Describe an automation instruction...').fill(
      `Reply with exactly two lines. Line 1: ${probeToken}. Line 2: Tuesday.`,
    )
    await page.getByRole('button', { name: 'Send Message' }).click()

    const assistantMessage = page
      .locator('.td-message')
      .filter({ has: page.locator('.td-message-role', { hasText: 'Assistant' }) })
      .last()
    const assistantContent = assistantMessage.locator('.td-message-content')
    await expect(assistantContent).toContainText(probeToken, { timeout: 30_000 })
    await expect(assistantContent).toContainText('Tuesday', { timeout: 30_000 })
    await expect(assistantMessage).not.toHaveAttribute('data-message-type', 'degraded')
  })
})
