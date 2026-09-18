import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

async function addCard(
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
  title: string,
) {
  const columnsResponse = await request.get(`${API_BASE_URL}/boards/${boardId}/columns`, {
    headers: { Authorization: `Bearer ${auth.token}` },
  })
  await assertOk(columnsResponse, 'List discard coordination board columns')
  const columns = await columnsResponse.json() as Array<{ id: string }>
  const response = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: { title, description: '', columnId: columns[0]!.id, position: 0 },
  })
  await assertOk(response, `Create discard coordination card '${title}'`)
}

async function openRouteDiscardPrompt(page: Page) {
  await page.goBack()
  await expect(page.getByTestId('card-switch-confirm')).toBeVisible()
  await expect(page.getByRole('dialog', { name: 'Discard card changes?' })).toHaveCount(1)
  await expect(page.getByTestId('card-discard-confirm')).toHaveCount(0)
}

test.describe('Paper board discard coordination', () => {
  test('keeps one confirmation owner across card close and browser Back', async ({ page, request }) => {
    const auth = await registerAndAttachSession(page, request, 'discard-coordination')
    const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
    const boardId = await createBoardWithColumn(request, auth, seed, {
      boardNamePrefix: 'Discard coordination',
      description: 'Browser discard coordination regression',
      columnNamePrefix: 'Backlog',
    })
    const boardName = `Discard coordination ${seed}`
    const cardTitle = `Unsaved discard ${seed}`
    await addCard(request, auth, boardId, cardTitle)

    await page.goto('/workspace/boards')
    await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
    const boardCard = page.locator('.paper-boards__card').filter({ hasText: boardName })
    await expect(boardCard).toBeVisible()
    await boardCard.click()
    await expect(page).toHaveURL(new RegExp(`/workspace/boards/${boardId}$`))
    await expect(page.locator('[data-testid="paper-board-lanes"]')).toBeVisible()

    const cardOpener = page.getByRole('button', { name: `Card ${cardTitle}`, exact: true })
    await cardOpener.click()
    const editor = page.getByRole('dialog', { name: 'Edit Card' })
    await expect(editor).toBeVisible()
    await page.locator('#card-title').fill('Unsaved title')

    await page.getByRole('button', { name: 'Close card editor' }).click()
    await expect(page.getByTestId('card-discard-confirm')).toBeVisible()
    await expect(page.getByRole('dialog', { name: 'Discard card changes?' })).toHaveCount(1)

    await openRouteDiscardPrompt(page)
    await expect(page.getByRole('dialog', { name: 'Discard card changes?' })).toBeFocused()
    await page.keyboard.press('Escape')
    await expect(page).toHaveURL(new RegExp(`/workspace/boards/${boardId}$`))
    await expect(page.locator('#card-title')).toHaveValue('Unsaved title')
    await expect(page.getByTestId('card-discard-confirm')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Close card editor' })).toBeFocused()

    await page.getByRole('button', { name: 'Close card editor' }).click()
    await expect(page.getByTestId('card-discard-confirm')).toBeVisible()
    await openRouteDiscardPrompt(page)
    await page.getByTestId('card-switch-confirm').click()

    await expect(page).toHaveURL('/workspace/boards')
    await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Close card editor' })).toHaveCount(0)
    await expect(page.getByTestId('card-discard-confirm')).toHaveCount(0)
    const focusAfterNavigation = await page.evaluate(() => {
      const active = document.activeElement
      return {
        isConnected: active?.isConnected ?? false,
        isOnBoardsDestination: active === document.body || (active instanceof Element && active.closest('.paper-boards') !== null),
        testId: active?.getAttribute('data-testid'),
      }
    })
    expect(focusAfterNavigation.isConnected).toBe(true)
    expect(focusAfterNavigation.isOnBoardsDestination).toBe(true)
    expect(focusAfterNavigation.testId).not.toBe('card-switch-confirm')
    expect(focusAfterNavigation.testId).not.toBe('card-discard-confirm')
  })
})
