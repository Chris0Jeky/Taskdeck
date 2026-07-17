/**
 * TST-11 Slice E: Archive and recovery flow validation.
 *
 * Covers board archival, archive workspace viewing, board restoration,
 * state preservation through archive/restore cycle, archive API endpoint
 * behavior, cross-user isolation, and unauthorized access checks.
 */

import { expect, test } from '@playwright/test'
import {
  API_BASE_URL,
  registerAndAttachSession,
  registerUserSession,
  type AuthResult,
} from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'

interface BoardDto {
  id: string
  name: string
  isArchived: boolean
  updatedAt: string
}

interface ArchiveItemDto {
  id: string
  entityType: string
  entityId: string
  name: string
  boardId: string
  restoreStatus: number | string
  archivedAt: string
}

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'archive-val', { theme: 'legacy' })
})

async function createBoard(
  request: import('@playwright/test').APIRequestContext,
  name: string,
): Promise<string> {
  const response = await request.post(`${API_BASE_URL}/boards`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: { name, description: 'Archive validation board' },
  })

  expect(response.ok()).toBeTruthy()
  const board = (await response.json()) as BoardDto
  return board.id
}

async function archiveBoard(
  request: import('@playwright/test').APIRequestContext,
  boardId: string,
): Promise<void> {
  const response = await request.put(`${API_BASE_URL}/boards/${boardId}`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: { isArchived: true },
  })

  expect(response.ok()).toBeTruthy()
  const board = (await response.json()) as BoardDto
  expect(board.isArchived).toBeTruthy()
}

async function unarchiveBoard(
  request: import('@playwright/test').APIRequestContext,
  boardId: string,
): Promise<void> {
  const response = await request.put(`${API_BASE_URL}/boards/${boardId}`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: { isArchived: false },
  })

  expect(response.ok()).toBeTruthy()
}

async function listBoards(
  request: import('@playwright/test').APIRequestContext,
  includeArchived = false,
): Promise<BoardDto[]> {
  const url = includeArchived
    ? `${API_BASE_URL}/boards?includeArchived=true`
    : `${API_BASE_URL}/boards`

  const response = await request.get(url, {
    headers: { Authorization: `Bearer ${auth.token}` },
  })

  expect(response.ok()).toBeTruthy()
  const body = await response.json()
  // API returns PaginatedResult<BoardDto> with { items, totalCount, hasMore, offset, limit }
  return (body.items ?? body) as BoardDto[]
}

async function getArchiveItems(
  request: import('@playwright/test').APIRequestContext,
  token: string,
  entityType?: string,
): Promise<ArchiveItemDto[]> {
  const params = entityType ? `?entityType=${entityType}` : ''
  const response = await request.get(`${API_BASE_URL}/archive/items${params}`, {
    headers: { Authorization: `Bearer ${token}` },
  })

  expect(response.ok()).toBeTruthy()
  return (await response.json()) as ArchiveItemDto[]
}

test.describe('TST11-SC-010: Archive a board', () => {
  test('archived board disappears from default board list', async ({ request }) => {
    const boardName = `Archive Test ${Date.now()}`
    const boardId = await createBoard(request, boardName)

    // Verify board is in default list
    const boardsBefore = await listBoards(request)
    expect(boardsBefore.some((b) => b.id === boardId)).toBeTruthy()

    // Archive the board
    await archiveBoard(request, boardId)

    // Board should be absent from default list
    const boardsAfter = await listBoards(request)
    expect(boardsAfter.some((b) => b.id === boardId)).toBeFalsy()

    // Board should be present when includeArchived=true
    const boardsWithArchived = await listBoards(request, true)
    const archivedBoard = boardsWithArchived.find((b) => b.id === boardId)
    expect(archivedBoard).toBeTruthy()
    expect(archivedBoard!.isArchived).toBeTruthy()
  })
})

test.describe('TST11-SC-011: View archived boards in archive workspace', () => {
  test('archive view shows archived boards', async ({ page, request }) => {
    const boardName = `Archive View ${Date.now()}`
    const boardId = await createBoard(request, boardName)
    await archiveBoard(request, boardId)

    await page.goto('/workspace/archive')
    await expect(page.getByRole('heading', { name: 'Archive', exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Archived Boards' })).toBeVisible()

    // Archived board should appear in the list
    await expect(page.getByText(boardName)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Restore Board' }).first()).toBeVisible()

    // Entity type filter should be present
    await expect(page.getByRole('combobox', { name: 'Filter by entity type' })).toBeVisible()
  })
})

test.describe('TST11-SC-012: Restore archived board', () => {
  test('restored board reappears in board list with preserved state', async ({ page, request }) => {
    const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
    const boardId = await createBoardWithColumn(request, auth, seed, {
      boardNamePrefix: 'Restore Test',
      description: 'board for restore validation',
      columnNamePrefix: 'Todo',
    })

    // Archive the board
    await archiveBoard(request, boardId)

    // Navigate to archive and restore
    await page.goto('/workspace/archive')
    await expect(page.getByRole('heading', { name: 'Archive', exact: true })).toBeVisible()

    // Find and click restore for this board
    const boardRow = page.locator('.td-archive-row').filter({ hasText: `Restore Test ${seed}` })
    await expect(boardRow).toBeVisible()

    // Accept the confirmation dialog
    page.once('dialog', (dialog) => dialog.accept())
    await boardRow.getByRole('button', { name: 'Restore Board' }).click()

    // Verify success toast
    await expect(page.getByText('Restored board')).toBeVisible()

    // Board should reappear in boards list
    const boards = await listBoards(request)
    expect(boards.some((b) => b.id === boardId)).toBeTruthy()

    // Verify seeded column still exists after restore
    const columnsResponse = await request.get(
      `${API_BASE_URL}/boards/${boardId}/columns`,
      { headers: { Authorization: `Bearer ${auth.token}` } },
    )
    expect(columnsResponse.ok()).toBeTruthy()
    const columns = (await columnsResponse.json()) as Array<{ id: string; name: string }>
    expect(columns.length).toBeGreaterThanOrEqual(1)
    expect(columns.some((c) => c.name.startsWith('Todo'))).toBeTruthy()
  })
})

test.describe('TST11-SC-013: State preservation through archive/restore', () => {
  test('card positions and labels survive archive/restore cycle', async ({ request }) => {
    const boardId = await createBoard(request, `State Preserve ${Date.now()}`)

    // Add columns
    for (const col of [
      { name: 'Todo', position: 0 },
      { name: 'Doing', position: 1 },
      { name: 'Done', position: 2 },
    ]) {
      await request.post(`${API_BASE_URL}/boards/${boardId}/columns`, {
        headers: { Authorization: `Bearer ${auth.token}` },
        data: { boardId, ...col, wipLimit: null },
      })
    }

    // Add a label
    const labelResponse = await request.post(`${API_BASE_URL}/boards/${boardId}/labels`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { boardId, name: 'preserve-test', colorHex: '#10B981' },
    })
    expect(labelResponse.ok()).toBeTruthy()
    const createdLabel = (await labelResponse.json()) as { id: string; name: string }

    // Get columns to find IDs
    const columnsResponse = await request.get(`${API_BASE_URL}/boards/${boardId}/columns`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    const columns = (await columnsResponse.json()) as Array<{ id: string; name: string; position: number }>
    const todoCol = columns.find((c) => c.name === 'Todo')!

    // Add a card with label
    const cardResponse = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: {
        boardId,
        columnId: todoCol.id,
        title: 'Preserved Card',
        description: 'This card should survive archive/restore.',
        position: 0,
        labelIds: [createdLabel.id],
      },
    })
    expect(cardResponse.ok()).toBeTruthy()
    const createdCard = (await cardResponse.json()) as { id: string; title: string; columnId: string; position: number }

    // Capture baseline
    const baselineCards = await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    const cardsBefore = (await baselineCards.json()) as Array<{ id: string; title: string; columnId: string; position: number }>

    // Archive and restore
    await archiveBoard(request, boardId)
    await unarchiveBoard(request, boardId)

    // Verify cards state -- find by ID, not by array index
    const restoredCards = await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    const cardsAfter = (await restoredCards.json()) as Array<{ id: string; title: string; columnId: string; position: number }>

    expect(cardsAfter).toHaveLength(cardsBefore.length)
    const restoredCard = cardsAfter.find((c) => c.id === createdCard.id)
    expect(restoredCard).toBeTruthy()
    expect(restoredCard!.title).toBe('Preserved Card')
    expect(restoredCard!.columnId).toBe(todoCol.id)
    expect(restoredCard!.position).toBe(createdCard.position)

    // Verify columns state
    const restoredCols = await request.get(`${API_BASE_URL}/boards/${boardId}/columns`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    const columnsAfter = (await restoredCols.json()) as Array<{ name: string; position: number }>
    expect(columnsAfter).toHaveLength(3)
    expect(columnsAfter.map((c) => `${c.position}:${c.name}`).sort()).toEqual(
      columns.map((c) => `${c.position}:${c.name}`).sort(),
    )

    // Verify labels survived
    const restoredLabels = await request.get(`${API_BASE_URL}/boards/${boardId}/labels`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    const labelsAfter = (await restoredLabels.json()) as Array<{ id: string; name: string }>
    expect(labelsAfter.some((l) => l.name === 'preserve-test')).toBeTruthy()
  })
})

test.describe('TST11-SC-014: Archive recovery API', () => {
  test('archive items endpoint returns list and supports entity type filter', async ({ request }) => {
    // Get all archive items
    const allItems = await getArchiveItems(request, auth.token)
    expect(Array.isArray(allItems)).toBeTruthy()

    // Filter by entity type
    const boardItems = await getArchiveItems(request, auth.token, 'board')
    expect(Array.isArray(boardItems)).toBeTruthy()

    // All returned items should be board type
    for (const item of boardItems) {
      expect(item.entityType.toLowerCase()).toBe('board')
    }
  })
})

test.describe('TST11-SC-016: Cross-user archive isolation', () => {
  test('users cannot see each other archived boards', async ({ request }) => {
    // UserA creates and archives a board
    const boardName = `UserA Private ${Date.now()}`
    const boardId = await createBoard(request, boardName)
    await archiveBoard(request, boardId)

    // Register UserB
    const userB = await registerUserSession(request, 'archive-iso-b')

    // UserB checks archived boards
    const userBBoards = await request.get(`${API_BASE_URL}/boards?includeArchived=true`, {
      headers: { Authorization: `Bearer ${userB.token}` },
    })
    expect(userBBoards.ok()).toBeTruthy()
    const userBBody = await userBBoards.json()
    // API returns PaginatedResult<BoardDto> with { items, totalCount, hasMore, offset, limit }
    const userBBoardList = (userBBody.items ?? userBBody) as BoardDto[]

    // UserA's archived board should not be visible to UserB
    expect(userBBoardList.some((b) => b.id === boardId)).toBeFalsy()

    // UserB checks archive items
    const userBItems = await getArchiveItems(request, userB.token)
    expect(userBItems.some((i) => i.entityId === boardId)).toBeFalsy()
  })
})

test.describe('TST11-SC-017: Archive unauthorized access', () => {
  test('archive endpoints return 401 without authentication', async ({ request }) => {
    const itemsResponse = await request.get(`${API_BASE_URL}/archive/items`)
    expect(itemsResponse.status()).toBe(401)

    // Restore endpoint also requires auth
    const restoreResponse = await request.post(
      `${API_BASE_URL}/archive/board/00000000-0000-0000-0000-000000000000/restore`,
      { data: { targetBoardId: null, restoreMode: 0, conflictStrategy: 0 } },
    )
    expect(restoreResponse.status()).toBe(401)
  })
})
