/**
 * Visual regression tests for the Calendar / planning view.
 *
 * The calendar grid reflects the current date — the month label, the
 * "today" highlight, and the day-of-week start alignment all shift as
 * real time advances. Without clock control the baseline would drift
 * monthly (and the "today" highlight daily).
 *
 * We pin the clock to a fixed UTC midnight before the view mounts so
 * every run renders the same month ("April 2026") with the same
 * "today" cell highlighted. This keeps the baseline stable across CI
 * runs without losing coverage of the grid layout.
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-calendar')
})

test('calendar view default state', async ({ page }) => {
  // Pin the clock before navigation so CalendarView's onMounted
  // initializes viewDate from a known value and the "today" cell
  // highlights deterministically. Chosen date is the 15th of a
  // mid-length month so the grid has a balanced layout.
  await page.clock.install({ time: new Date('2026-04-15T12:00:00Z') })

  await page.goto('/workspace/calendar')
  await expect(page.getByRole('heading', { name: 'Calendar', exact: true })).toBeVisible()

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('calendar-default.png')
})
