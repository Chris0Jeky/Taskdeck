/**
 * Manual audit pack — opt-in headed Playwright suite for operator-visible debugging.
 *
 * Covers the core Home -> Inbox/Capture -> Review -> Board loop with screenshots
 * at each milestone, plus selected advanced checks (command palette, filter panel,
 * board settings lifecycle).
 *
 * Usage:
 *   npm run test:e2e:audit:headed
 *
 * Live-provider probes (optional):
 *   TASKDECK_RUN_LIVE_LLM_TESTS=1 npm run test:e2e:audit:headed
 *
 * This pack is NOT part of required CI. It is intended for local operator audits,
 * pre-release sanity checks, and visual debugging sessions.
 *
 * Gated behind TASKDECK_RUN_AUDIT=1. The npm script sets this automatically:
 *   npm run test:e2e:audit:headed
 */

import { expect, test } from '@playwright/test'
import { parseTrueishEnv } from '../../scripts/demo-shared.mjs'
import { registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import {
  createCaptureItem,
  listBoardCards,
  waitForCardWithTitle,
  waitForProposalCreated,
} from './support/captureFlow'

const runAudit = parseTrueishEnv(process.env.TASKDECK_RUN_AUDIT)

test.use({
  screenshot: 'on',
  trace: 'retain-on-failure',
  launchOptions: {
    slowMo: 250,
  },
})

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'audit')
})

test.describe('Core loop: Home -> Inbox/Capture -> Review -> Board', () => {
  test.skip(!runAudit, 'Set TASKDECK_RUN_AUDIT=1 or use npm run test:e2e:audit:headed')
  test('full capture-triage-review-apply loop with screenshots', async ({ page, request }, testInfo) => {
    // Step 1: Home landing
    await page.goto('/workspace/home')
    await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('01-home.png'), fullPage: true })

    // Step 2: Create board with column via API
    const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
    const boardId = await createBoardWithColumn(request, auth, seed, {
      boardNamePrefix: 'Audit Board',
      description: 'manual audit e2e board',
      columnNamePrefix: 'Inbox',
    })

    // Step 3: Create capture item via API
    const checklistTaskTitle = `Audit card ${seed}`
    const captureText = `- [ ] ${checklistTaskTitle}`
    const createdCapture = await createCaptureItem(request, auth, boardId, captureText)
    const captureId = createdCapture.id

    // Step 4: Navigate to Inbox and verify capture item
    await page.goto('/workspace/inbox')
    await expect(page.getByRole('heading', { name: 'Inbox', level: 1 })).toBeVisible()
    const captureRow = page.locator('[data-testid="inbox-item"]').filter({ hasText: checklistTaskTitle }).first()
    await expect(captureRow).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('02-inbox-with-capture.png'), fullPage: true })

    // Step 5: Triage capture item
    await captureRow.click()
    const triageButton = page.locator('.td-inbox-detail__actions button').filter({ hasText: 'Start Triage' }).first()
    await expect(triageButton).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('03-inbox-detail-pre-triage.png'), fullPage: true })
    await triageButton.click()

    // Step 6: Wait for proposal creation
    const triagedCapture = await waitForProposalCreated(request, auth, captureId)
    const proposalId = triagedCapture.provenance?.proposalId
    expect(proposalId).toBeTruthy()

    // Verify no card created yet (proposal-first)
    const cardsAfterTriage = await listBoardCards(request, auth, boardId)
    expect(cardsAfterTriage.length).toBe(0)

    // Step 7: Navigate to proposal in Review
    await page.getByRole('button', { name: 'Refresh Detail' }).click()
    const openProposalButton = page.getByRole('button', { name: 'Open in Review' })
    await expect(openProposalButton).toBeVisible()
    await openProposalButton.click()

    await expect(page).toHaveURL(new RegExp(`/workspace/review\\?boardId=${boardId}#proposal-${proposalId}`))
    const proposalCard = page.locator(`#proposal-${proposalId}`)
    await expect(proposalCard).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('04-review-proposal.png'), fullPage: true })

    // Step 8: Approve proposal
    await proposalCard.getByRole('button', { name: 'Approve for board' }).click()
    await expect(proposalCard.getByText('Approved')).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('05-review-approved.png'), fullPage: true })

    // Step 9: Apply proposal to board
    page.once('dialog', (dialog) => dialog.accept())
    await proposalCard.getByRole('button', { name: 'Apply to board' }).click()
    await expect(proposalCard).not.toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('06-review-applied.png'), fullPage: true })

    // Step 10: Verify card on board
    const createdCard = await waitForCardWithTitle(request, auth, boardId, checklistTaskTitle)

    await page.goto(`/workspace/boards/${boardId}`)
    const card = page.locator('[data-card-id]').filter({ hasText: createdCard.title }).first()
    await expect(card).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('07-board-with-card.png'), fullPage: true })

    // Step 11: Open card and verify provenance links
    await card.getByRole('heading', { name: createdCard.title, exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Edit Card' })).toBeVisible()
    await expect(page.getByText('Capture Origin')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Open Capture' })).toHaveAttribute(
      'href',
      `/workspace/inbox?boardId=${boardId}#capture-${captureId}`,
    )
    await expect(page.getByRole('link', { name: 'Open Proposal' })).toHaveAttribute(
      'href',
      `/workspace/review?boardId=${boardId}#proposal-${proposalId}`,
    )
    await page.screenshot({ path: testInfo.outputPath('08-card-provenance.png'), fullPage: true })
  })
})

test.describe('Advanced checks', () => {
  test.skip(!runAudit, 'Set TASKDECK_RUN_AUDIT=1 or use npm run test:e2e:audit:headed')

  test('command palette search navigates to inbox', async ({ page }, testInfo) => {
    await page.goto('/workspace/boards')
    await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

    await page.keyboard.press('Control+K')
    const palette = page.getByRole('dialog', { name: 'Command palette' })
    await expect(palette).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('09-command-palette.png'), fullPage: true })

    const paletteInput = palette.getByPlaceholder('Type a command or search...')
    await paletteInput.fill('inbox')
    await paletteInput.press('Enter')

    await expect(page).toHaveURL(/\/workspace\/inbox$/)
    await page.screenshot({ path: testInfo.outputPath('10-inbox-from-palette.png'), fullPage: true })
  })

  test('capture hotkey opens modal and saves item', async ({ page }, testInfo) => {
    await page.goto('/workspace/boards')
    await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

    const captureText = `Audit capture ${Date.now()}`

    await page.keyboard.press('Control+Shift+C')
    const captureModal = page.getByRole('dialog', { name: 'Capture item' })
    await expect(captureModal).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('11-capture-modal.png'), fullPage: true })

    await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').fill(captureText)
    await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').press('Control+Enter')

    await expect(page).toHaveURL(/\/workspace\/inbox$/)
    await expect(page.locator('.td-inbox-row__excerpt').first()).toContainText(captureText)
    await page.screenshot({ path: testInfo.outputPath('12-inbox-after-capture.png'), fullPage: true })
  })

  test('board create, column, card, and filter panel', async ({ page }, testInfo) => {
    const boardName = `Audit Filter Board ${Date.now()}`
    const columnName = `To Do ${Date.now()}`
    const matchingCard = `Alpha ${Date.now()}`
    const hiddenCard = `Beta ${Date.now()}`

    // Create board
    await page.goto('/workspace/boards')
    await page.getByRole('button', { name: '+ New Board' }).click()
    await page.getByPlaceholder('Board name').fill(boardName)
    await page.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
    await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('13-new-board.png'), fullPage: true })

    // Add column
    await page.getByRole('button', { name: '+ Add Column' }).click()
    await page.getByPlaceholder('Column name').fill(columnName)
    await page.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(page.getByRole('heading', { name: columnName, exact: true })).toBeVisible()

    // Add cards
    const column = page.locator('[data-column-id]')
      .filter({ has: page.getByRole('heading', { name: columnName, exact: true }) })
      .first()

    for (const cardTitle of [matchingCard, hiddenCard]) {
      await column.getByRole('button', { name: 'Add Card' }).click()
      await column.getByPlaceholder('Enter card title...').fill(cardTitle)
      const createCardResponse = page.waitForResponse((response) =>
        response.request().method() === 'POST'
        && /\/api\/boards\/[a-f0-9-]+\/cards$/i.test(response.url())
        && response.ok())
      await column.getByRole('button', { name: 'Add', exact: true }).click()
      await createCardResponse
      await expect(page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()).toBeVisible()
    }

    await page.screenshot({ path: testInfo.outputPath('14-board-with-cards.png'), fullPage: true })

    // Filter panel
    await page.keyboard.press('f')
    await expect(page.getByRole('heading', { name: 'Filter Cards' })).toBeVisible()
    await page.getByPlaceholder('Search cards...').fill(matchingCard)
    await expect(page.locator('[data-card-id]:visible')).toHaveCount(1)
    await expect(page.locator('[data-card-id]').filter({ hasText: matchingCard })).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('15-filter-active.png'), fullPage: true })
  })
})

test.describe('Live LLM provider probe', () => {
  test.skip(!runAudit, 'Set TASKDECK_RUN_AUDIT=1 or use npm run test:e2e:audit:headed')
  test.skip(
    !parseTrueishEnv(process.env.TASKDECK_RUN_LIVE_LLM_TESTS),
    'Set TASKDECK_RUN_LIVE_LLM_TESTS=1 to run the opt-in live-provider probe.',
  )

  test('live LLM health check and first chat turn', async ({ page }, testInfo) => {
    await page.goto('/workspace/automations/chat')
    await expect(page.locator('[data-llm-health-state="configured"]')).toBeVisible()
    await expect(page.getByText('Live LLM configured')).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('16-llm-configured.png'), fullPage: true })

    await page.getByRole('button', { name: 'Verify LLM' }).click()
    await expect(page.locator('[data-llm-health-state="verified"]')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('Live LLM verified')).toBeVisible()
    await page.screenshot({ path: testInfo.outputPath('17-llm-verified.png'), fullPage: true })

    const probeToken = `AUDIT_LLM_PROBE_${Date.now()}`

    await page.getByPlaceholder('Session title').fill(`Audit LLM ${Date.now()}`)
    await page.getByRole('button', { name: 'Create Session' }).click()

    await page.getByPlaceholder('Describe an automation instruction...').fill(
      `Reply with exactly two lines. Line 1: ${probeToken}. Line 2: Wednesday.`,
    )
    await page.getByRole('button', { name: 'Send Message' }).click()

    const assistantMessage = page
      .locator('.td-message')
      .filter({ has: page.locator('.td-message-role', { hasText: 'Assistant' }) })
      .last()
    const assistantContent = assistantMessage.locator('.td-message-content')
    await expect(assistantContent).toContainText(probeToken, { timeout: 30_000 })
    await page.screenshot({ path: testInfo.outputPath('18-llm-response.png'), fullPage: true })
  })
})
