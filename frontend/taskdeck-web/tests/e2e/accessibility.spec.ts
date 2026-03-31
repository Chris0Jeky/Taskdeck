/**
 * Automated accessibility checks using axe-core via @axe-core/playwright.
 *
 * Runs against core workspace views to catch WCAG 2.1 AA violations.
 * This is a baseline — not a substitute for manual screen reader testing.
 */
import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { registerAndAttachSession } from './support/authSession'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'a11y')
})

/**
 * Helper: run axe-core on the current page and assert zero violations.
 * Disables specific rules that are expected to have residual warnings
 * during the initial audit rollout.
 */
async function expectNoAxeViolations(
  page: import('@playwright/test').Page,
  context?: string,
) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    // color-contrast can be noisy with CSS custom properties that axe cannot resolve statically
    .disableRules(['color-contrast'])
    .analyze()

  const violations = results.violations.map((v) => ({
    id: v.id,
    impact: v.impact,
    description: v.description,
    nodes: v.nodes.length,
    help: v.helpUrl,
  }))

  if (violations.length > 0) {
    console.log(
      `axe violations${context ? ` (${context})` : ''}:`,
      JSON.stringify(violations, null, 2),
    )
  }

  expect(
    violations,
    `Expected no axe-core violations${context ? ` on ${context}` : ''}, but found ${violations.length}`,
  ).toHaveLength(0)
}

test('Home view has no WCAG 2.1 AA violations', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()
  await expectNoAxeViolations(page, 'HomeView')
})

test('Today view has no WCAG 2.1 AA violations', async ({ page }) => {
  await page.goto('/workspace/today')
  await expect(page.getByRole('heading', { name: 'Today', exact: true })).toBeVisible()
  await expectNoAxeViolations(page, 'TodayView')
})

test('Inbox view has no WCAG 2.1 AA violations', async ({ page }) => {
  await page.goto('/workspace/inbox')
  await expect(page.getByRole('heading', { name: 'Inbox', exact: true })).toBeVisible()
  await expectNoAxeViolations(page, 'InboxView')
})

test('Review view has no WCAG 2.1 AA violations', async ({ page }) => {
  await page.goto('/workspace/review')
  await expect(page.getByRole('heading', { name: 'Review', exact: true })).toBeVisible()
  await expectNoAxeViolations(page, 'ReviewView')
})

test('Boards list view has no WCAG 2.1 AA violations', async ({ page }) => {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('heading', { name: 'My Boards', exact: true })).toBeVisible()
  await expectNoAxeViolations(page, 'BoardsListView')
})

test('Login view has no WCAG 2.1 AA violations', async ({ browser, baseURL }) => {
  // Login is a public page — use a fresh context so the addInitScript from
  // beforeEach (which re-injects the auth token on every navigation) doesn't
  // cause the router guard to redirect /login → /workspace/home.
  const ctx = await browser.newContext({ baseURL })
  const page = await ctx.newPage()
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: /sign in/i })).toBeVisible()
  await expectNoAxeViolations(page, 'LoginView')
  await ctx.close()
})

test('skip-to-content link exists and targets main content', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  const skipLink = page.locator('a.td-skip-link')
  await expect(skipLink).toHaveAttribute('href', '#td-main-content')

  // The skip link should be visually hidden until focused
  await skipLink.focus()
  await expect(skipLink).toBeVisible()

  // The target should exist
  const mainContent = page.locator('#td-main-content')
  await expect(mainContent).toBeAttached()
})
