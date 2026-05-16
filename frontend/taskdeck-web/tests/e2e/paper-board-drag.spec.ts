import { expect, test, type Page } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'
import { assertOk } from './support/httpAsserts'
import type { APIRequestContext } from '@playwright/test'

async function enablePaperMode(page: Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem('td.paper.mode', 'paper')
  })
}

async function createBoardWith2Columns(
  request: APIRequestContext,
  auth: AuthResult,
  seed: string,
) {
  const response = await request.post(
    `${API_BASE_URL}/import/boards?userId=${encodeURIComponent(auth.user.id)}`,
    {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: {
        name: `Paper Drag ${seed}`,
        description: 'E2E paper drag test board',
        columns: [
          { name: 'Backlog', position: 0, wipLimit: null },
          { name: 'Done', position: 1, wipLimit: null },
        ],
        cards: [],
        labels: [],
      },
    },
  )
  await assertOk(response, 'Import board for paper drag test')
  const result = await response.json()
  return result.boardId as string
}

async function addCardViaApi(
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
  columnId: string,
  title: string,
) {
  const response = await request.post(
    `${API_BASE_URL}/boards/${boardId}/cards`,
    {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { title, description: '', columnId, position: 0 },
    },
  )
  await assertOk(response, `Create card '${title}'`)
  const card = await response.json()
  return card.id as string
}

async function getColumns(
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
) {
  const response = await request.get(
    `${API_BASE_URL}/boards/${boardId}/columns`,
    { headers: { Authorization: `Bearer ${auth.token}` } },
  )
  await assertOk(response, 'Get columns')
  return await response.json() as Array<{ id: string; name: string; position: number }>
}

async function getAuditLog(
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
) {
  const response = await request.get(
    `${API_BASE_URL}/audit/boards/${boardId}?limit=10`,
    { headers: { Authorization: `Bearer ${auth.token}` } },
  )
  await assertOk(response, 'Get audit log')
  return await response.json() as Array<{ action: number; entityType: string; [key: string]: unknown }>
}

test.describe('Paper board card drag', () => {
  test('dragging a card across columns in Paper mode persists and creates audit entry', async ({ page, request }) => {
    await enablePaperMode(page)
    const auth = await registerAndAttachSession(page, request, 'paper-drag')
    const seed = `${Date.now()}`
    const boardId = await createBoardWith2Columns(request, auth, seed)

    const columns = await getColumns(request, auth, boardId)
    const backlogCol = columns.find((c) => c.name === 'Backlog')!

    await addCardViaApi(request, auth, boardId, backlogCol.id, `Drag Target ${seed}`)

    await page.goto(`/workspace/boards/${boardId}`)
    await expect(page.locator('[data-testid="paper-board-lanes"]')).toBeVisible()

    const cardTitle = `Drag Target ${seed}`
    const card = page.locator('[data-card-id]').filter({ hasText: cardTitle }).first()
    await expect(card).toBeVisible()

    const cardHandle = card.locator('[data-action="drag-card-handle"]')
    const targetLane = page
      .locator('[data-column-dnd-id]')
      .filter({ has: page.getByRole('heading', { name: 'Done', exact: true }) })
      .first()

    await cardHandle.dragTo(targetLane)

    await expect(
      targetLane.locator('[data-card-id]').filter({ hasText: cardTitle }).first(),
    ).toBeVisible()

    const sourceLane = page
      .locator('[data-column-dnd-id]')
      .filter({ has: page.getByRole('heading', { name: 'Backlog', exact: true }) })
      .first()
    await expect(
      sourceLane.locator('[data-card-id]').filter({ hasText: cardTitle }),
    ).toHaveCount(0)

    const audit = await getAuditLog(request, auth, boardId)
    const moveEntry = audit.find(
      (e) => e.action === 5 && e.entityType === 'card',
    )
    expect(moveEntry).toBeDefined()
  })

  test('paper card focus ring is visible on keyboard focus', async ({ page, request }) => {
    await enablePaperMode(page)
    const auth = await registerAndAttachSession(page, request, 'paper-focus')
    const seed = `${Date.now()}`
    const boardId = await createBoardWith2Columns(request, auth, seed)

    const columns = await getColumns(request, auth, boardId)
    await addCardViaApi(request, auth, boardId, columns[0].id, `Focus Card ${seed}`)

    await page.goto(`/workspace/boards/${boardId}`)
    await expect(page.locator('[data-testid="paper-board-lanes"]')).toBeVisible()

    const card = page.locator('[data-card-id]').first()
    await page.keyboard.press('Tab')
    await expect(card).toBeFocused()

    const outline = await card.evaluate((el) => window.getComputedStyle(el).outline)
    expect(outline).toContain('2px')
    expect(outline).toContain('solid')
  })
})
