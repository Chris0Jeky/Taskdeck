import { expect, test, type APIResponse, type Locator, type Page } from '@playwright/test'
import { API_BASE_URL, attachSessionToPage, registerAndAttachSession, registerUserSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

interface EstimateCard {
  id: string
  title: string
  columnId: string
  estimatedEffortMinutes: number | null
  updatedAt: string
}

async function responseJson<T>(response: APIResponse, operation: string): Promise<T> {
  await assertOk(response, operation)
  return response.json() as Promise<T>
}

function cardOpener(page: Page, theme: 'paper' | 'legacy', card: EstimateCard) {
  const item = page.locator(`[data-card-id="${card.id}"]`)
  return theme === 'paper'
    ? item.getByRole('button', { name: `Card ${card.title}`, exact: true })
    : item.getByRole('heading', { name: card.title, exact: true })
}

async function expectTotals(region: Locator, cards: number, known: string, missing: number) {
  await expect(region.getByText(`${known} known estimate`, { exact: true })).toBeVisible()
  await expect(region.getByText(`${cards} cards · ${cards - missing} estimated`, { exact: true })).toBeVisible()
  await expect(region.getByText(`${missing} missing estimates`, { exact: true })).toBeVisible()
}

for (const theme of ['legacy', 'paper'] as const) {
  test(`${theme}: real estimates preserve unknown and zero, refresh totals, and remain usable on a phone`, async ({ page, request, browser }, testInfo) => {
    test.setTimeout(120_000)
    await page.setViewportSize({ width: 1280, height: 844 })
    const auth = await registerAndAttachSession(page, request, `estimates-${theme}`, { theme })
    const participant = await registerUserSession(request, `estimates-viewer-${theme}`)
    const headers = { Authorization: `Bearer ${auth.token}` }
    const boardId = await createBoardWithColumn(request, auth, `${Date.now()}-${theme}`, {
      boardNamePrefix: 'Estimate browser proof', columnNamePrefix: 'Todo', description: 'Synthetic estimate QA only',
    })
    const boardUrl = `${API_BASE_URL}/boards/${boardId}`
    const cardsUrl = `${boardUrl}/cards`
    const board = await responseJson<{ columns: Array<{ id: string; name: string }> }>(
      await request.get(boardUrl, { headers }), 'Read estimate board')
    const todo = board.columns[0]!
    const doing = await responseJson<{ id: string; name: string }>(await request.post(`${boardUrl}/columns`, {
      headers, data: { name: 'Doing', position: 1, wipLimit: null },
    }), 'Create second estimate column')
    await assertOk(await request.post(`${boardUrl}/access`, {
      headers, data: { boardId, userId: participant.user.id, role: 3 },
    }), 'Grant Viewer participation')
    const readCard = async (id: string) => responseJson<EstimateCard>(
      await request.get(`${cardsUrl}/${id}`, { headers }), 'Read persisted estimate')
    await page.goto(`/workspace/boards/${boardId}`)
    const todoLane = page.locator(`[data-column-id="${todo.id}"]`)

    async function quickCreate(title: string, hours?: string, minutes?: string) {
      await todoLane.locator('[data-action="toggle-add-card"]').click()
      const form = theme === 'paper'
        ? todoLane.getByTestId('paper-card-composer')
        : todoLane.locator('[data-action="add-card-form"]')
      await form.locator('[data-action="add-card-input"]').fill(title)
      if (hours !== undefined || minutes !== undefined) {
        await form.getByText('Add estimate (optional)', { exact: true }).click()
        await form.getByTestId('estimate-hours').fill(hours ?? '')
        await form.getByTestId('estimate-minutes').fill(minutes ?? '')
      }
      const createdResponse = page.waitForResponse(response => response.url() === cardsUrl && response.request().method() === 'POST')
      await form.locator('button[type="submit"]').click()
      const response = await createdResponse
      expect(response.ok()).toBe(true)
      const created = await response.json() as EstimateCard
      await expect(form).toHaveCount(0)
      await expect(cardOpener(page, theme, created)).toBeVisible()
      return created
    }

    const unknown = await quickCreate('Unknown estimate')
    const zero = await quickCreate('Known zero estimate', '', '0')
    const shared = await quickCreate('Shared ninety minute estimate', '1', '30')
    expect((await readCard(unknown.id)).estimatedEffortMinutes).toBeNull()
    expect((await readCard(zero.id)).estimatedEffortMinutes).toBe(0)
    expect((await readCard(shared.id)).estimatedEffortMinutes).toBe(90)

    const editor = page.getByRole('dialog', { name: 'Edit Card', exact: true })
    await cardOpener(page, theme, shared).click()
    await expect(editor.getByTestId('estimate-hours')).toHaveValue('1')
    await expect(editor.getByTestId('estimate-minutes')).toHaveValue('30')
    await page.screenshot({ path: testInfo.outputPath(`${theme}-estimate-editor-desktop.png`) })
    await editor.getByRole('button', { name: 'Clear estimate', exact: true }).click()
    const clearedResponse = page.waitForResponse(response => response.url() === `${cardsUrl}/${shared.id}` && response.request().method() === 'PATCH')
    await editor.getByRole('button', { name: 'Save Changes', exact: true }).click()
    const cleared = await clearedResponse
    expect(cleared.ok()).toBe(true)
    expect(cleared.request().postDataJSON().clearEstimatedEffort).toBe(true)
    await expect(editor).not.toBeVisible()
    expect((await readCard(shared.id)).estimatedEffortMinutes).toBeNull()

    await cardOpener(page, theme, shared).click()
    await expect(editor.getByTestId('estimate-hours')).toHaveValue('')
    await expect(editor.getByTestId('estimate-minutes')).toHaveValue('')
    await expect(editor.getByTestId('estimate-summary')).toContainText('Not estimated')
    await editor.getByTestId('estimate-hours').fill('1')
    await editor.getByTestId('estimate-minutes').fill('30')
    await editor.getByRole('button', { name: 'Save Changes', exact: true }).click()
    await expect(editor).not.toBeVisible()
    expect((await readCard(shared.id)).estimatedEffortMinutes).toBe(90)

    // Use the real assignment controls to create overlapping responsibility.
    await cardOpener(page, theme, shared).click()
    const assignments = editor.getByRole('region', { name: 'Card assignments', exact: true })
    await assignments.getByRole('checkbox', { name: auth.user.username, exact: true }).check()
    await assignments.getByRole('checkbox', { name: participant.user.username, exact: true }).check()
    const assignedResponse = page.waitForResponse(response => response.url() === `${cardsUrl}/${shared.id}/assignments` && response.request().method() === 'PUT')
    await assignments.getByRole('button', { name: 'Save assignments', exact: true }).click()
    expect((await assignedResponse).ok()).toBe(true)
    await expect(assignments.getByRole('button', { name: 'Save assignments', exact: true })).toBeDisabled()
    await editor.getByRole('button', { name: 'Close card editor', exact: true }).click()
    await expect(editor).not.toBeVisible()

    const ownerOnly = await responseJson<EstimateCard>(await request.post(cardsUrl, {
      headers, data: { columnId: todo.id, title: 'Owner estimate', estimatedEffortMinutes: 60 },
    }), 'Create owner estimate')
    await responseJson<EstimateCard>(await request.post(cardsUrl, {
      headers, data: { columnId: doing.id, title: 'Unassigned thirty minute estimate', estimatedEffortMinutes: 30 },
    }), 'Create unassigned estimate')
    await page.reload()
    await cardOpener(page, theme, ownerOnly).click()
    await assignments.getByRole('checkbox', { name: auth.user.username, exact: true }).check()
    const ownerAssignmentResponse = page.waitForResponse(response => response.url() === `${cardsUrl}/${ownerOnly.id}/assignments` && response.request().method() === 'PUT')
    await assignments.getByRole('button', { name: 'Save assignments', exact: true }).click()
    expect((await ownerAssignmentResponse).ok()).toBe(true)
    await expect(assignments.getByRole('button', { name: 'Save assignments', exact: true })).toBeDisabled()
    await editor.getByRole('button', { name: 'Close card editor', exact: true }).click()
    await expect(editor).not.toBeVisible()

    const estimates = page.getByRole('region', { name: 'Board estimates', exact: true })
    await estimates.getByRole('button', { name: 'Estimates', exact: true }).click()
    const total = estimates.getByRole('region', { name: 'Board total', exact: true })
    const unassigned = estimates.getByRole('region', { name: 'Unassigned total', exact: true })
    const columns = estimates.getByRole('region', { name: 'Estimates by column', exact: true })
    const participants = estimates.getByRole('region', { name: 'Estimates by participant', exact: true })
    const columnTotal = (name: string) => columns.getByRole('listitem').filter({ has: page.getByRole('heading', { name, exact: true }) })
    const participantTotal = (name: string) => participants.getByRole('listitem').filter({ has: page.getByRole('heading', { name, exact: true }) })
    await expectTotals(total, 5, '3h', 1)
    await expectTotals(columnTotal(todo.name), 4, '2h 30m', 1)
    await expectTotals(columnTotal(doing.name), 1, '30m', 0)
    await expectTotals(participantTotal(auth.user.username), 2, '2h 30m', 0)
    await expectTotals(participantTotal(participant.user.username), 1, '1h 30m', 0)
    await expectTotals(unassigned, 3, '30m', 1)
    await expect(participants).toContainText('Participant totals overlap')
    await expect(estimates).toContainText('do not measure time worked or capacity')
    const rollup = await responseJson<{ board: { cardCount: number; knownEstimateMinutes: number; missingEstimateCount: number } }>(
      await request.get(`${boardUrl}/estimate-rollups`, { headers }), 'Read real estimate rollup')
    expect(rollup.board).toEqual({ cardCount: 5, knownEstimateMinutes: 180, missingEstimateCount: 1 })
    await testInfo.attach(`${theme}-real-api-estimates-and-rollup`, {
      contentType: 'application/json',
      body: JSON.stringify({
        unknown: (await readCard(unknown.id)).estimatedEffortMinutes,
        zero: (await readCard(zero.id)).estimatedEffortMinutes,
        shared: (await readCard(shared.id)).estimatedEffortMinutes,
        rollup,
      }, null, 2),
    })
    await page.screenshot({ path: testInfo.outputPath(`${theme}-estimate-totals-desktop.png`), fullPage: true })

    async function refreshStaleTotals() {
      await expect(estimates.getByText('Board state changed. Refresh estimates to see the latest totals.', { exact: true })).toBeVisible()
      const refreshed = page.waitForResponse(response => response.url() === `${boardUrl}/estimate-rollups` && response.request().method() === 'GET')
      await estimates.getByRole('button', { name: 'Refresh estimates', exact: true }).click()
      expect((await refreshed).ok()).toBe(true)
      await expect(estimates.getByText('Loading estimates', { exact: false })).toHaveCount(0)
      await expect(estimates.getByText('Board state changed.', { exact: false })).toHaveCount(0)
    }

    await cardOpener(page, theme, ownerOnly).click()
    await editor.getByTestId('estimate-hours').fill('2')
    await editor.getByRole('button', { name: 'Save Changes', exact: true }).click()
    await expect(editor).not.toBeVisible()
    expect((await readCard(ownerOnly.id)).estimatedEffortMinutes).toBe(120)
    await refreshStaleTotals()
    await expectTotals(total, 5, '4h', 1)

    const ownerItem = page.locator(`[data-card-id="${ownerOnly.id}"]`)
    const movedResponse = page.waitForResponse(response => response.url() === `${cardsUrl}/${ownerOnly.id}/move` && response.request().method() === 'POST')
    if (theme === 'legacy') {
      await ownerItem.getByRole('button', { name: 'Move to column', exact: true }).click()
      await ownerItem.getByRole('menuitem', { name: doing.name, exact: true }).click()
    } else {
      await ownerItem.locator('[data-action="drag-card-handle"]').dragTo(page.locator(`[data-column-dnd-id="${doing.id}"]`))
    }
    expect((await movedResponse).ok()).toBe(true)
    expect((await readCard(ownerOnly.id)).columnId).toBe(doing.id)
    await refreshStaleTotals()
    await expectTotals(total, 5, '4h', 1)
    await expectTotals(columnTotal(todo.name), 3, '1h 30m', 1)
    await expectTotals(columnTotal(doing.name), 2, '2h 30m', 0)

    await cardOpener(page, theme, ownerOnly).click()
    await editor.getByRole('button', { name: 'Archive card', exact: true }).click()
    await page.getByRole('dialog', { name: 'Archive card?', exact: true }).getByRole('button', { name: 'Confirm archive', exact: true }).click()
    await expect(editor).not.toBeVisible()
    await refreshStaleTotals()
    await expectTotals(total, 4, '2h', 1)
    await expectTotals(columnTotal(doing.name), 1, '30m', 0)
    await expectTotals(unassigned, 3, '30m', 1)

    await page.setViewportSize({ width: 390, height: 844 })
    await expect(total).toBeVisible()
    expect(await estimates.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true)
    await page.screenshot({ path: testInfo.outputPath(`${theme}-estimate-totals-phone.png`), fullPage: true })
    const estimatesTrigger = estimates.getByRole('button', { name: 'Estimates', exact: true })
    await estimates.getByRole('button', { name: 'Refresh estimates', exact: true }).focus()
    await page.keyboard.press('Escape')
    await expect(estimatesTrigger).toHaveAttribute('aria-expanded', 'false')
    await expect(estimatesTrigger).toBeFocused()

    // A clean phone editor retains usable estimate fields and restores real focus.
    const opener = cardOpener(page, theme, shared)
    if (theme === 'paper') await opener.focus()
    await opener.click()
    await expect(editor.getByTestId('estimate-hours')).toBeVisible()
    await expect(editor.getByTestId('estimate-minutes')).toBeVisible()
    await editor.getByTestId('estimate-minutes').focus()
    await page.screenshot({ path: testInfo.outputPath(`${theme}-estimate-editor-phone.png`) })
    await page.keyboard.press('Escape')
    await expect(editor).not.toBeVisible()
    const focusedCard = theme === 'paper' ? opener : page.locator(`[data-card-id="${shared.id}"]`)
    await expect(focusedCard).toBeFocused()

    const viewerContext = await browser.newContext({ viewport: { width: 390, height: 844 } })
    try {
      const viewerPage = await viewerContext.newPage()
      await attachSessionToPage(viewerPage, participant, { theme })
      await viewerPage.goto(`/workspace/boards/${boardId}`)
      await viewerPage.getByRole('region', { name: 'Board estimates', exact: true }).getByRole('button', { name: 'Estimates', exact: true }).click()
      await expectTotals(viewerPage.getByRole('region', { name: 'Board total', exact: true }), 4, '2h', 1)
      await cardOpener(viewerPage, theme, shared).click()
      const viewerEditor = viewerPage.getByRole('dialog', { name: 'Edit Card', exact: true })
      await expect(viewerEditor.getByTestId('estimate-summary')).toHaveText('1h 30m')
      await expect(viewerEditor.getByTestId('estimate-hours')).toHaveCount(0)
      await expect(viewerEditor.getByTestId('estimate-minutes')).toHaveCount(0)
      await expect(viewerEditor.getByRole('button', { name: 'Clear estimate', exact: true })).toHaveCount(0)
      await expect(viewerEditor.getByRole('button', { name: 'Archive card', exact: true })).toBeDisabled()
      await viewerPage.screenshot({ path: testInfo.outputPath(`${theme}-viewer-estimate-phone.png`) })
    } finally {
      await viewerContext.close()
    }
  })
}
