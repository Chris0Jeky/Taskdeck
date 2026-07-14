/**
 * Automated accessibility checks using axe-core via @axe-core/playwright.
 *
 * Runs against the canonical Paper workspace views to catch WCAG 2.1 AA violations.
 * This is a baseline — not a substitute for manual screen reader testing.
 */
import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'a11y')
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
    // color-contrast is disabled because axe-core cannot statically resolve CSS custom
    // properties (--td-* design tokens). Color contrast must be validated manually or
    // via browser DevTools accessibility audit when design tokens change.
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

test('Paper Home view has no WCAG 2.1 AA violations', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByTestId('paper-home')).toBeVisible()
  await expectNoAxeViolations(page, 'PaperHomeView')
})

test('Paper Today view has no WCAG 2.1 AA violations', async ({ page }) => {
  await page.goto('/workspace/today')
  await expect(page.locator('.paper-today')).toBeVisible()
  await expectNoAxeViolations(page, 'PaperTodayView')
})

test('Paper Inbox view has no WCAG 2.1 AA violations', async ({ page }) => {
  await page.goto('/workspace/inbox')
  await expect(page.getByTestId('paper-inbox-capture')).toBeVisible()
  await expectNoAxeViolations(page, 'PaperInboxView')
})

test('Paper Review view has no WCAG 2.1 AA violations', async ({ page }) => {
  await page.goto('/workspace/review')
  await expect(page.getByTestId('paper-review-view')).toBeVisible()
  await expectNoAxeViolations(page, 'PaperReviewView')
})

test('Paper Board view has no WCAG 2.1 AA violations', async ({ page, request }) => {
  const boardId = await createBoardWithColumn(request, auth, 'a11y-board', {
    boardNamePrefix: 'Paper A11y',
    description: 'Paper board accessibility regression',
    columnNamePrefix: 'Backlog',
  })
  await page.goto(`/workspace/boards/${boardId}`)
  await expect(page.getByTestId('paper-board-lanes')).toBeVisible()
  await expectNoAxeViolations(page, 'PaperBoardView')
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
  await expect(page.getByTestId('paper-home')).toBeVisible()

  const skipLink = page.locator('a.td-skip-link')
  await expect(skipLink).toHaveAttribute('href', '#td-main-content')

  // The skip link should be visually hidden until focused
  await skipLink.focus()
  await expect(skipLink).toBeVisible()

  // The target should exist
  const mainContent = page.locator('#td-main-content')
  await expect(mainContent).toBeAttached()
})
