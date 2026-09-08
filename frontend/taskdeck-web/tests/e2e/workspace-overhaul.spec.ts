import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'
import { listBoardCards, waitForCardWithTitle, waitForProposalCreated } from './support/captureFlow'
import { expectApplyConfirmDialog } from './support/applyConfirm'

async function setup(page: Page, request: APIRequestContext, scope: string) {
  const auth = await registerAndAttachSession(page, request, `overhaul-${scope}`)
  await page.addInitScript(() => {
    if (!localStorage.getItem('td.workspace.layout.v1')) {
      localStorage.setItem('td.workspace.layout.v1', JSON.stringify({ experience: 'studio', presentation: 'studio' }))
    }
    localStorage.setItem('td.paper.mode.v2', 'grove')
  })
  const boardId = await createBoardWithColumn(request, auth, String(Date.now()), {
    boardNamePrefix: 'Product studio', description: 'A place to turn questions into useful work.', columnNamePrefix: 'Next',
  })
  return { auth, boardId, headers: { Authorization: `Bearer ${auth.token}` } }
}

async function seedCard(request: APIRequestContext, auth: AuthResult, boardId: string, blocked = false) {
  const headers = { Authorization: `Bearer ${auth.token}` }
  const boardResponse = await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })
  await assertOk(boardResponse, 'read board')
  const board = await boardResponse.json() as { columns: { id: string }[] }
  const response = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
    headers, data: { boardId, columnId: board.columns[0]!.id, title: 'Make room for a better first step', description: 'Explore the possibilities without losing the original thought.' },
  })
  await assertOk(response, 'create thinking card')
  const card = await response.json() as { id: string; title: string }
  if (blocked) await assertOk(await request.patch(`${API_BASE_URL}/boards/${boardId}/cards/${card.id}`, {
    headers, data: { isBlocked: true, blockReason: 'Waiting for a decision about the next step.' },
  }), 'mark card blocked')
  return card
}

test('keeps Home capture and saved thinking across all experience combinations', async ({ page, request }) => {
  test.setTimeout(90_000)
  const { auth, boardId, headers } = await setup(page, request, 'thinking')
  const card = await seedCard(request, auth, boardId)
  await page.goto('/workspace/home')
  const thought = page.getByLabel('LEAVE A THOUGHT HERE')
  await thought.fill('A question worth keeping exactly as written.')
  for (const experience of ['classic', 'companion', 'unified', 'studio']) {
    await page.getByLabel('Workspace experience', { exact: true }).selectOption(experience)
  }
  await expect(thought).toHaveValue('A question worth keeping exactly as written.')
  const captureSaved = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/capture/items'))
  await page.getByRole('button', { name: 'Save to Inbox', exact: true }).click()
  await assertOk(await captureSaved, 'save original Home thought')
  await expect(thought).toHaveValue('')
  await page.screenshot({ path: '../../artifacts/overhaul/studio-home.png', fullPage: true })

  await page.goto(`/workspace/boards/${boardId}/cards/${card.id}/thinking`)
  await page.getByRole('button', { name: '+ note', exact: true }).click()
  const title = page.getByLabel('Layer 1 title', { exact: true })
  await title.fill('Keep the original possibility')
  await page.getByLabel('Layer 1 details').fill('There may be more than one useful way forward.')
  await title.evaluate(el => el.setAttribute('data-mounted-marker', 'original-input'))
  for (const experience of ['classic', 'studio', 'companion', 'unified']) {
    await page.getByLabel('Workspace experience', { exact: true }).selectOption(experience)
    for (const presentation of ['zen', 'studio', 'control']) {
      await page.getByLabel('Workspace presentation', { exact: true }).selectOption(presentation)
      await expect(title).toHaveValue('Keep the original possibility')
      await expect(title).toHaveAttribute('data-mounted-marker', 'original-input')
    }
  }
  await page.getByRole('button', { name: '+ options', exact: true }).click()
  await page.getByRole('button', { name: '+ Add option', exact: true }).click()
  await page.getByLabel('options item 1', { exact: true }).fill('Explore a small experiment')
  await page.getByRole('button', { name: '+ Add option', exact: true }).click()
  await page.getByLabel('options item 2', { exact: true }).fill('Keep the current approach')
  await page.getByLabel('Choose option 1', { exact: true }).check()
  await page.getByRole('button', { name: 'Path', exact: true }).click()
  await page.getByRole('button', { name: 'Save thinking', exact: true }).click()
  await expect(page.getByText('Thinking saved', { exact: true })).toBeVisible()
  await page.reload()
  await expect(title).toHaveValue('Keep the original possibility')
  await expect(page.getByLabel('options item 2', { exact: true })).toHaveValue('Keep the current approach')

  // A concurrent server save must not erase the local draft or silently win.
  const endpoint = `${API_BASE_URL}/boards/${boardId}/cards/${card.id}/thinking`
  const saved = await (await request.get(endpoint, { headers })).json()
  await assertOk(await request.put(endpoint, { headers, data: { expectedRevision: saved.revision, layers: saved.layers } }), 'concurrent thinking save')
  await title.fill('My unsaved continuation')
  await page.getByRole('button', { name: 'Save thinking', exact: true }).click()
  await expect(page.getByRole('alert')).toContainText('Someone saved a newer version')
  await expect(title).toHaveValue('My unsaved continuation')
  await page.getByRole('navigation', { name: 'Card context' }).getByRole('link', { name: 'Memory', exact: true }).click()
  await expect(page.getByRole('dialog', { name: 'Leave unsaved thinking?' })).toBeVisible()
  await page.getByRole('button', { name: 'Keep editing', exact: true }).click()
  await expect(title).toHaveValue('My unsaved continuation')
  await page.getByRole('button', { name: 'Load saved version…', exact: true }).click()
  await page.getByRole('button', { name: 'Discard draft and load', exact: true }).click()
  await expect(title).toHaveValue('Keep the original possibility')
  await page.getByRole('button', { name: 'Path', exact: true }).click()
  await page.screenshot({ path: '../../artifacts/overhaul/unified-thinking.png', fullPage: true })
})

test('retains separate approval and apply gates while switching experiences', async ({ page, request }) => {
  test.setTimeout(90_000)
  const { auth, boardId } = await setup(page, request, 'capture')
  const cardTitle = `A useful next step ${Date.now()}`
  await page.goto(`/workspace/inbox?boardId=${boardId}`)
  await page.getByTestId('paper-composer-body').fill(`- [ ] ${cardTitle}`)
  const saved = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/capture/items'))
  await page.getByRole('button', { name: /^Capture/ }).click()
  const response = await saved
  await assertOk(response, 'capture through Studio')
  const capture = await response.json() as { id: string }
  await page.getByLabel('Workspace experience', { exact: true }).selectOption('companion')
  await page.locator('.paper-triage__row').filter({ hasText: cardTitle }).getByRole('button', { name: 'Ask AI', exact: true }).click()
  const triaged = await waitForProposalCreated(request, auth, capture.id)
  const proposalId = triaged.provenance!.proposalId
  expect(await listBoardCards(request, auth, boardId)).toHaveLength(0)
  await page.goto('/workspace/review')
  await page.getByLabel('Workspace experience', { exact: true }).selectOption('unified')
  await expect(page.getByRole('heading', { level: 1, name: `Capture triage: ${cardTitle}` })).toBeVisible({ timeout: 15_000 })
  const approved = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith(`/proposals/${proposalId}/approve`))
  await page.getByTestId('decision-apply').click()
  await assertOk(await approved, 'approve in Unified')
  expect(await listBoardCards(request, auth, boardId)).toHaveLength(0)
  await page.getByLabel('Workspace experience', { exact: true }).selectOption('classic')
  const executed = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith(`/proposals/${proposalId}/execute`))
  await expectApplyConfirmDialog(page, () => page.getByTestId('decision-apply').click())
  await assertOk(await executed, 'explicitly apply in Classic')
  const card = await waitForCardWithTitle(request, auth, boardId, cardTitle)
  await page.goto(`/workspace/boards/${boardId}`)
  await expect(page.getByRole('button', { name: `Card ${card.title}`, exact: true })).toBeVisible()
})

test('answers an insight privately and preserves memory corrections and archive history', async ({ page, request }) => {
  test.setTimeout(75_000)
  const { auth, boardId, headers } = await setup(page, request, 'memory')
  await seedCard(request, auth, boardId, true)
  const before = await listBoardCards(request, auth, boardId)
  await page.goto(`/workspace/insights?boardId=${boardId}`)
  await page.getByRole('button', { name: 'Analyze now', exact: true }).click()
  const insight = page.locator('.paper-insights__card')
  await expect(insight).toHaveCount(1)
  await page.getByRole('button', { name: 'Analyze now', exact: true }).click()
  await expect(insight).toHaveCount(1)
  await page.getByRole('button', { name: 'Answer privately', exact: true }).click()
  await page.getByLabel('Your private answer', { exact: true }).fill('A small test with the current layout will answer this.')
  await expect(page.getByRole('button', { name: 'Analyze now', exact: true })).toBeDisabled()
  await page.getByRole('button', { name: 'Save memory', exact: true }).click()
  await expect(page.getByText('Saved to private memory. The board was not changed.', { exact: true })).toBeVisible()
  expect(await listBoardCards(request, auth, boardId)).toEqual(before)
  await page.screenshot({ path: '../../artifacts/overhaul/quiet-insights.png', fullPage: true })
  await page.goto(`/workspace/memory?boardId=${boardId}`)
  const memory = page.locator('.paper-memory__card')
  await expect(memory).toHaveCount(1)
  await page.getByRole('button', { name: 'Correct', exact: true }).click()
  await page.getByLabel('Memory', { exact: true }).fill('We tested the layout and chose a smaller next step.')
  await page.getByRole('button', { name: 'Save correction', exact: true }).click()
  await expect(memory.getByText('Original text', { exact: true })).toBeVisible()
  await expect(memory).toContainText('A small test with the current layout will answer this.')
  await memory.locator('summary').click()
  await expect(memory.locator('details')).toHaveAttribute('open', '')
  await page.screenshot({ path: '../../artifacts/overhaul/workspace-memory.png', fullPage: true })
  await page.getByRole('button', { name: 'Archive', exact: true }).click()
  await expect(memory).toHaveCount(0)
  await page.getByLabel('Show archived', { exact: true }).check()
  await expect(memory).toHaveCount(1)
  await page.getByRole('button', { name: 'Restore', exact: true }).click()
  await expect(memory).toHaveCount(0)
  await page.getByLabel('Show archived', { exact: true }).uncheck()
  await expect(memory).toHaveCount(1)
  const saved = await request.get(`${API_BASE_URL}/workspace-memory?boardId=${boardId}`, { headers })
  await assertOk(saved, 'read restored private memory')
  const rows = await saved.json() as { revision: number; originalText: string; history: unknown[] }[]
  expect(rows[0]!.revision).toBe(4)
  expect(rows[0]!.history.length).toBeGreaterThanOrEqual(3)
  expect(await listBoardCards(request, auth, boardId)).toEqual(before)
})

test('makes comparison and Grove themes usable on desktop and narrow screens', async ({ page, request }) => {
  const { boardId } = await setup(page, request, 'responsive')
  await page.goto('/workspace/experiences')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  for (const width of [1440, 768, 375]) {
    await page.setViewportSize({ width, height: 900 })
    for (const experience of ['classic', 'studio', 'companion', 'unified']) {
      await page.getByLabel('Workspace experience', { exact: true }).selectOption(experience)
      await expect(page.getByLabel('Workspace presentation', { exact: true })).toBeVisible()
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1)
    }
  }
  await page.goto(`/workspace/memory?boardId=${boardId}`)
  await page.getByRole('button', { name: 'Add memory', exact: true }).click()
  await page.getByLabel('Title', { exact: true }).fill('A question for the next visit')
  await page.getByLabel('Memory', { exact: true }).fill('Which presentation helps me notice the next useful step?')
  await page.getByLabel('Status', { exact: true }).selectOption('unknown')
  await page.getByRole('button', { name: 'Save memory', exact: true }).click()
  await expect(page.locator('.paper-memory__card')).toHaveCount(1)
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1)
  await page.screenshot({ path: '../../artifacts/overhaul/mobile-memory.png', fullPage: true })
  await page.goto('/workspace/settings/appearance')
  await page.locator('[data-mode="grove-night"]').click()
  await expect(page.locator('body')).toHaveClass(/grove-night/)
  await page.getByLabel('Workspace experience', { exact: true }).selectOption('studio')
  // Use client navigation: init script intentionally seeds Grove for new documents.
  await page.getByRole('link', { name: 'Home', exact: true }).filter({ visible: true }).first().click()
  await expect(page.locator('.overhaul-home')).toBeVisible()
  await page.screenshot({ path: '../../artifacts/overhaul/grove-night-mobile.png', fullPage: true })
})
