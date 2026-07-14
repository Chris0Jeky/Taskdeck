/**
 * Automated accessibility checks using axe-core via @axe-core/playwright.
 *
 * Runs against the canonical Paper workspace views to catch WCAG 2.1 AA violations.
 * This is a baseline — not a substitute for manual screen reader testing.
 */
import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'a11y')
})

/** Run the full axe-core WCAG 2.1 A/AA ruleset on a settled page. */
async function expectNoAxeViolations(
  page: import('@playwright/test').Page,
  context?: string,
) {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()

  const violations = results.violations.map((v) => ({
    id: v.id,
    impact: v.impact,
    description: v.description,
    nodes: v.nodes.map((node) => ({
      target: node.target,
      failureSummary: node.failureSummary,
    })),
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
  await expect(page.getByTestId('paper-home-first-board')).toBeVisible()
  await expectNoAxeViolations(page, 'PaperHomeView')
})

test('Paper Today view has no WCAG 2.1 AA violations', async ({ page }) => {
  await page.goto('/workspace/today')
  await expect(page.getByRole('heading', { name: 'Today, at a glance.' })).toBeVisible()
  await expectNoAxeViolations(page, 'PaperTodayView')
})

test('Paper Inbox view has no WCAG 2.1 AA violations', async ({ page }) => {
  await page.goto('/workspace/inbox')
  await expect(page.getByTestId('paper-inbox-capture')).toBeVisible()
  await expect(page.getByText('A pen and a phrase. Drop a thought above to start.')).toBeVisible()
  await expectNoAxeViolations(page, 'PaperInboxView')
})

test('Paper Review view has no WCAG 2.1 AA violations', async ({ page }) => {
  await page.goto('/workspace/review')
  const emptyReview = page.getByTestId('paper-review-empty')
  await expect(emptyReview.getByText(/Loading proposals/)).toHaveCount(0)
  await expect(emptyReview.getByRole('heading', { name: 'Nothing waiting. Good.' })).toBeVisible()
  await expectNoAxeViolations(page, 'PaperReviewView')
})

test('Paper Board view has no WCAG 2.1 AA violations', async ({ page, request }) => {
  const seed = `a11y-board-${Date.now()}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Paper A11y',
    description: 'Paper board accessibility regression',
    columnNamePrefix: 'Backlog',
  })
  const headers = { Authorization: `Bearer ${auth.token}` }
  const columnsResponse = await request.get(`${API_BASE_URL}/boards/${boardId}/columns`, { headers })
  await assertOk(columnsResponse, `list columns for Paper a11y board ${boardId}`)
  const columns = await columnsResponse.json() as Array<{ id: string }>
  expect(columns).toHaveLength(1)

  const cardTitle = `Paper accessibility card ${seed}`
  const cardResponse = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
    headers,
    data: {
      boardId,
      columnId: columns[0]!.id,
      title: cardTitle,
      description: 'Keyboard and drag affordance coverage',
      position: 0,
    },
  })
  await assertOk(cardResponse, `create Paper a11y card '${cardTitle}'`)

  await page.goto(`/workspace/boards/${boardId}`)
  await expect(page.getByTestId('paper-board-lanes')).toBeVisible()
  await expect(page.locator('.paper-board-card').filter({ hasText: cardTitle })).toBeVisible()
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
  await expect(page.getByTestId('paper-home-first-board')).toBeVisible()

  const skipLink = page.locator('a.td-skip-link')
  await expect(skipLink).toHaveAttribute('href', '#td-main-content')

  await page.keyboard.press('Tab')
  await expect(skipLink).toBeFocused()
  await expect(skipLink).toBeVisible()

  const mainContent = page.locator('#td-main-content')
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/#td-main-content$/)
  await expect(mainContent).toBeFocused()
})
