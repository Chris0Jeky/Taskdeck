/**
 * E2E: Onboarding and First-Run Scenarios
 *
 * Covers the initial experience for new users including:
 * - Empty state CTAs on Today, Boards, and Inbox views
 * - Starter pack application from Paper Home's guided setup
 * - Help-text visibility for first-time users across views
 * - Setup dialog validation (empty name, template selection)
 */

import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'onboarding')
})

// --- Empty state CTAs across views ---

test('fresh user boards view should show empty state with New Board CTA', async ({ page }) => {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  // The boards list area should contain guidance text or an empty state indicator
  const emptyIndicator = page
    .getByText(/no boards/i)
    .or(page.getByText(/get started/i))
    .or(page.getByText(/create.*board/i))
    .first()
  await expect(emptyIndicator).toBeVisible({ timeout: 10_000 })
})

test('fresh user inbox should show empty state with guidance', async ({ page }) => {
  await page.goto('/workspace/inbox')
  await expect(page.getByRole('heading', { name: "What's on your mind, quickly?" })).toBeVisible()

  await expect(page.getByText('No captures yet')).toBeVisible({ timeout: 10_000 })

  await expect(page.getByText('Nothing flows to the board without your approval.')).toBeVisible()
})

test('fresh user today view should show onboarding steps', async ({ page }) => {
  await page.goto('/workspace/today')
  await expect(page.getByRole('heading', { name: 'Today, at a glance.' })).toBeVisible()

  await expect(page.getByText('captures to triage')).toBeVisible()
  await expect(page.getByText('Open Review for live proposals.')).toBeVisible()
})

// --- Setup dialog validation ---

test('setup dialog should require a board name before creation', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByTestId('paper-home')).toBeVisible()

  await page.getByRole('button', { name: 'Start guided setup' }).click()
  const setupDialog = page.getByRole('dialog', { name: 'Workspace setup' })
  await expect(setupDialog).toBeVisible()

  // Leave the board name empty and select a template
  await setupDialog.getByRole('radio', { name: /Engineering sprint/i }).check()

  // The Create Board button should be disabled or clicking it should not navigate
  const createButton = setupDialog.getByRole('button', { name: 'Create Board' })
  await expect(createButton).toBeDisabled()
})

// --- Starter pack template creates board with expected structure ---

test('starter pack engineering template should create board with Backlog and Review columns', async ({ page }) => {
  const boardName = `Starter Pack ${Date.now()}`

  await page.goto('/workspace/home')
  await expect(page.getByTestId('paper-home')).toBeVisible()

  await page.getByRole('button', { name: 'Start guided setup' }).click()
  const setupDialog = page.getByRole('dialog', { name: 'Workspace setup' })
  await expect(setupDialog).toBeVisible()

  await setupDialog.getByPlaceholder('For example: Product Sprint').fill(boardName)
  await setupDialog.getByRole('radio', { name: /Engineering sprint/i }).check()
  await setupDialog.getByRole('button', { name: 'Create Board' }).click()

  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()

  // Engineering sprint template should create standard columns
  await expect(page.getByRole('heading', { name: 'Backlog', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Review', exact: true })).toBeVisible()
})
