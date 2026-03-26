import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'

function parseTrueishEnv(value: string | undefined): boolean {
  const normalized = value?.trim().toLowerCase()
  return normalized === '1' || normalized === 'true' || normalized === 'yes' || normalized === 'on'
}

test.describe('live llm chat', () => {
  test.skip(
    !parseTrueishEnv(process.env.TASKDECK_RUN_LIVE_LLM_TESTS),
    'Set TASKDECK_RUN_LIVE_LLM_TESTS=1 to run the opt-in live-provider probe.',
  )

  test('first chat turn should use a live provider and answer the prompt directly', async ({ page, request }) => {
    await registerAndAttachSession(page, request, 'live-llm')

    await page.goto('/workspace/automations/chat')
    await expect(page.locator('[data-llm-health-state="live"]')).toBeVisible()
    await expect(page.getByText('Live LLM ready')).toBeVisible()

    const probeToken = `LIVE_LLM_PROBE_${Date.now()}`

    await page.getByPlaceholder('Session title').fill(`Live LLM ${Date.now()}`)
    await page.getByRole('button', { name: 'Create Session' }).click()

    await page.getByPlaceholder('Describe an automation instruction...').fill(
      `Reply with exactly two lines. Line 1: ${probeToken}. Line 2: Tuesday.`,
    )
    await page.getByRole('button', { name: 'Send Message' }).click()

    const assistantContent = page.locator('.td-message-content').last()
    await expect(assistantContent).toContainText(probeToken, { timeout: 30_000 })
    await expect(assistantContent).toContainText('Tuesday')
    await expect(assistantContent).not.toContainText('Live provider request failed.')
    await expect(assistantContent).not.toContainText('Live provider configuration is invalid.')
    await expect(assistantContent).not.toContainText('Live provider request errored.')
  })
})
