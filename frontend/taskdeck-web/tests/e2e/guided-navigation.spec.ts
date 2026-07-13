import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'

const GUIDED_EVIDENCE_PATH = '../../docs/product/evidence/gen-10/guided-navigation.png'
const WORKBENCH_EVIDENCE_PATH = '../../docs/product/evidence/gen-10/workbench-navigation.png'

async function expectNoAxeViolations(page: Page, context: string) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    // Axe cannot resolve Taskdeck's CSS custom-property contrast values.
    .disableRules(['color-contrast'])
    .analyze()

  expect(
    results.violations.map(violation => ({
      id: violation.id,
      impact: violation.impact,
      nodes: violation.nodes.length,
    })),
    `Expected no axe violations on ${context}`,
  ).toEqual([])
}

test.beforeEach(async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 1050 })
  await page.addInitScript(() => {
    window.localStorage.setItem('td.paper.mode.v2', 'paper')
  })
  await registerAndAttachSession(page, request, 'guided-navigation')
})

test('guided navigation keeps technical routes behind Advanced while other modes stay unchanged', async ({ page }) => {
  await page.goto('/workspace/home')

  const modeSelector = page.getByRole('combobox', { name: 'Workspace mode' })
  const sidebar = page.locator('[data-paper-sidebar]')
  const advanced = page.getByTestId('paper-guided-advanced-navigation')

  await expect(modeSelector).toHaveValue('guided')
  await expect(sidebar.locator('[data-group="workbench"]')).toContainText('More tools')
  for (const path of ['/workspace/agents', '/workspace/metrics', '/workspace/integrations', '/workspace/ops/cli', '/workspace/settings/api-keys']) {
    await expect(sidebar.locator(`a[href="${path}"]`)).toHaveCount(0)
  }
  await expect(sidebar.locator('a[href="/workspace/settings/profile"]')).toBeVisible()
  await expect(sidebar.locator('a[href="/workspace/settings/appearance"]')).toBeVisible()

  // Hiding sidebar navigation must not remove the established command escape hatch.
  await page.getByRole('button', { name: 'Open command palette' }).click()
  const palette = page.getByRole('dialog', { name: 'Command palette' })
  await expect(palette.getByRole('option', { name: /Metrics/ })).toBeVisible()
  await page.keyboard.press('Escape')

  const advancedToggle = page.getByTestId('paper-guided-advanced-toggle')
  await expect(advancedToggle).toHaveAttribute('aria-expanded', 'false')
  await advancedToggle.click()
  await expect(advancedToggle).toHaveAttribute('aria-expanded', 'true')
  for (const label of ['Agents', 'Metrics', 'Cohorts', 'Integrations', 'Ops', 'Endpoints', 'Logs', 'API Keys']) {
    await expect(advanced.getByText(label, { exact: true })).toBeVisible()
  }
  await expectNoAxeViolations(page, 'guided navigation')

  if (process.env.TASKDECK_CAPTURE_GEN10_EVIDENCE === '1') {
    await sidebar.screenshot({ path: GUIDED_EVIDENCE_PATH })
  }

  await page.getByTestId('paper-switch-to-workbench').click()
  await expect(modeSelector).toHaveValue('workbench')
  await expect(advanced).toHaveCount(0)
  await expect(sidebar.locator('[data-group="workbench"]')).toContainText('Workbench tools')
  for (const path of ['/workspace/metrics', '/workspace/integrations', '/workspace/ops/cli', '/workspace/settings/api-keys']) {
    await expect(sidebar.locator(`a[href="${path}"]`)).toBeVisible()
  }
  await expectNoAxeViolations(page, 'workbench navigation')

  if (process.env.TASKDECK_CAPTURE_GEN10_EVIDENCE === '1') {
    await sidebar.screenshot({ path: WORKBENCH_EVIDENCE_PATH })
  }

  await modeSelector.selectOption('agent')
  await expect(modeSelector).toHaveValue('agent')
  await expect(advanced).toHaveCount(0)
  await expect(sidebar.locator('a[href="/workspace/metrics"]')).toBeVisible()
  await expect(sidebar.locator('a[href="/workspace/integrations"]')).toBeVisible()
  await expect(sidebar.locator('a[href="/workspace/settings/api-keys"]')).toBeVisible()
})
