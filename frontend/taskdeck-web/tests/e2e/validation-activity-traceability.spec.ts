/**
 * TST-11 Slice E: Activity timeline and audit traceability validation.
 *
 * Covers board-scoped audit, entity-scoped audit (card level),
 * user-scoped audit, audit entries for board mutations (create, archive, restore),
 * activity view mode switching and selector discoverability,
 * and unauthorized access checks.
 */

import { expect, test } from '@playwright/test'
import {
  API_BASE_URL,
  registerAndAttachSession,
  registerUserSession,
  type AuthResult,
} from './support/authSession'

interface BoardDto {
  id: string
  name: string
}

interface ColumnDto {
  id: string
  name: string
  position: number
}

interface CardDto {
  id: string
  title: string
  columnId: string
}

interface AuditEntryDto {
  id: string
  action: number
  entityType: string
  entityId: string
  timestamp: string
  userName?: string | null
  changes?: string | null
  userId?: string | null
}

/**
 * AuditAction enum values from backend Domain/Enums/AuditAction.cs.
 * The API serializes these as integers (System.Text.Json default).
 */
const AuditAction = {
  Created: 0,
  Updated: 1,
  Deleted: 2,
  Archived: 3,
  Unarchived: 4,
  Moved: 5,
} as const

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'activity-val')
})

async function createBoard(
  request: import('@playwright/test').APIRequestContext,
  name: string,
): Promise<string> {
  const response = await request.post(`${API_BASE_URL}/boards`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: { name, description: 'Activity validation board' },
  })

  expect(response.ok()).toBeTruthy()
  const board = (await response.json()) as BoardDto
  return board.id
}

async function addColumn(
  request: import('@playwright/test').APIRequestContext,
  boardId: string,
  name: string,
  position: number,
): Promise<string> {
  const response = await request.post(
    `${API_BASE_URL}/boards/${boardId}/columns`,
    {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { boardId, name, position, wipLimit: null },
    },
  )

  expect(response.status()).toBe(201)
  const column = (await response.json()) as ColumnDto
  return column.id
}

async function addCard(
  request: import('@playwright/test').APIRequestContext,
  boardId: string,
  columnId: string,
  title: string,
): Promise<string> {
  const response = await request.post(
    `${API_BASE_URL}/boards/${boardId}/cards`,
    {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { boardId, columnId, title, description: 'Audit test card', position: 0 },
    },
  )

  expect(response.ok()).toBeTruthy()
  const card = (await response.json()) as CardDto
  return card.id
}

async function getBoardHistory(
  request: import('@playwright/test').APIRequestContext,
  boardId: string,
  token?: string,
): Promise<AuditEntryDto[]> {
  const response = await request.get(
    `${API_BASE_URL}/audit/boards/${boardId}`,
    { headers: { Authorization: `Bearer ${token ?? auth.token}` } },
  )

  expect(response.ok()).toBeTruthy()
  return (await response.json()) as AuditEntryDto[]
}

async function getEntityHistory(
  request: import('@playwright/test').APIRequestContext,
  entityType: string,
  entityId: string,
): Promise<AuditEntryDto[]> {
  const response = await request.get(
    `${API_BASE_URL}/audit/entities/${entityType}/${entityId}`,
    { headers: { Authorization: `Bearer ${auth.token}` } },
  )

  expect(response.ok()).toBeTruthy()
  return (await response.json()) as AuditEntryDto[]
}

async function getUserHistory(
  request: import('@playwright/test').APIRequestContext,
): Promise<AuditEntryDto[]> {
  const response = await request.get(
    `${API_BASE_URL}/audit/users/me`,
    { headers: { Authorization: `Bearer ${auth.token}` } },
  )

  expect(response.ok()).toBeTruthy()
  return (await response.json()) as AuditEntryDto[]
}

test.describe('TST11-SC-018: Board-scoped activity timeline', () => {
  test('board history includes creation and child entity mutations', async ({ request }) => {
    const boardId = await createBoard(request, `Audit Board ${Date.now()}`)
    const colId = await addColumn(request, boardId, 'Todo', 0)
    await addCard(request, boardId, colId, 'Audit Card 1')

    const history = await getBoardHistory(request, boardId)
    // Board creation + column creation + card creation = at least 3 entries
    expect(history.length).toBeGreaterThanOrEqual(3)

    // Entries should have required fields
    for (const entry of history) {
      expect(typeof entry.action).toBe('number')
      expect(entry.action).toBeGreaterThanOrEqual(0)
      expect(entry.timestamp).toBeTruthy()
      expect(entry.entityType).toBeTruthy()
    }

    // Verify a Created action exists
    expect(history.some((e) => e.action === AuditAction.Created)).toBeTruthy()
  })

  test('board history fetch renders the first audit row in the virtual timeline', async ({ page, request }) => {
    const boardName = `Visible Activity ${Date.now()}`
    await createBoard(request, boardName)

    await page.goto('/workspace/activity')

    await page.locator('#activity-board-select').selectOption({ label: boardName })
    await page.getByRole('button', { name: 'Fetch', exact: true }).click()

    await expect(page).toHaveURL(/\/workspace\/activity\/board\/[a-f0-9-]+$/)
    const timeline = page.locator('.td-timeline--virtual')
    await expect(timeline).toBeVisible()
    await expect(timeline.locator('[data-index="0"]')).toBeVisible()
    await expect(timeline.locator('.td-timeline__action').first()).toHaveText('Created')
  })

  test('@mobile long board history stays scrollable and traceable on small screens', async ({ page, request }) => {
    const boardName = `Mobile Activity Long History ${Date.now()}`
    const boardId = await createBoard(request, boardName)
    const columnId = await addColumn(request, boardId, 'Synthetic Activity Column', 0)

    for (let index = 1; index <= 48; index += 1) {
      await addCard(
        request,
        boardId,
        columnId,
        `Synthetic Activity Card ${String(index).padStart(2, '0')}`,
      )
    }

    const history = await getBoardHistory(request, boardId)
    expect(history).toHaveLength(50)

    await page.goto('/workspace/activity')
    await page.locator('#activity-board-select').selectOption({ label: boardName })
    await page.getByRole('button', { name: 'Fetch', exact: true }).click()

    await expect(page).toHaveURL(/\/workspace\/activity\/board\/[a-f0-9-]+$/)
    const timeline = page.locator('.td-timeline--virtual')
    await expect(timeline).toBeVisible()
    await expect(timeline.locator('[data-index="0"]')).toBeVisible()

    const overflow = await page.evaluate(() => {
      const documentRoot = document.documentElement
      const timelineElement = document.querySelector<HTMLElement>('.td-timeline--virtual')
      return {
        documentWithinViewport:
          documentRoot.scrollWidth <= documentRoot.clientWidth + 2,
        timelineWithinViewport:
          timelineElement !== null &&
          timelineElement.scrollWidth <= timelineElement.clientWidth + 2,
      }
    })
    expect(overflow.documentWithinViewport).toBe(true)
    expect(overflow.timelineWithinViewport).toBe(true)

    const timelineSize = await timeline.evaluate((element) => ({
      scrollHeight: element.scrollHeight,
      clientHeight: element.clientHeight,
    }))
    expect(timelineSize.scrollHeight).toBeGreaterThan(timelineSize.clientHeight)

    await timeline.evaluate((element) => {
      element.scrollIntoView({ block: 'center', inline: 'nearest' })
      element.scrollTo({ top: element.scrollHeight, left: 0, behavior: 'auto' })
    })

    const lastRow = timeline.locator('[data-index="49"]')
    await expect.poll(
      async () => {
        await timeline.evaluate((element) => {
          element.scrollTo({ top: element.scrollHeight, left: 0, behavior: 'auto' })
        })
        return page.evaluate(() => {
          const timelineElement = document.querySelector<HTMLElement>('.td-timeline--virtual')
          const row = timelineElement?.querySelector<HTMLElement>('[data-index="49"]')
          if (!timelineElement || !row) {
            return {
              atBottom: false,
              inTimelineViewport: false,
              inBrowserViewport: false,
            }
          }

          const timelineRect = timelineElement.getBoundingClientRect()
          const rowRect = row.getBoundingClientRect()
          const atBottom =
            timelineElement.scrollTop + timelineElement.clientHeight >=
            timelineElement.scrollHeight - 1
          const inTimelineViewport =
            rowRect.height > 0 &&
            rowRect.top >= timelineRect.top - 1 &&
            rowRect.bottom <= timelineRect.bottom + 1
          const inBrowserViewport =
            rowRect.top >= -1 && rowRect.bottom <= window.innerHeight + 1

          return { atBottom, inTimelineViewport, inBrowserViewport }
        })
      },
      { timeout: 8_000, intervals: [50, 100, 200, 500] },
    ).toMatchObject({ atBottom: true, inTimelineViewport: true, inBrowserViewport: true })
    await expect(lastRow).toBeVisible()
  })
})

test.describe('TST11-SC-019: Entity-scoped activity (card level)', () => {
  test('card history shows creation event', async ({ request }) => {
    const boardId = await createBoard(request, `Card Audit ${Date.now()}`)
    const colId = await addColumn(request, boardId, 'Backlog', 0)
    const cardId = await addCard(request, boardId, colId, 'Traced Card')

    const history = await getEntityHistory(request, 'card', cardId)
    expect(history.length).toBeGreaterThan(0)

    // Should include a creation event (action === 0 is AuditAction.Created)
    const hasCreateAction = history.some((e) => e.action === AuditAction.Created)
    expect(hasCreateAction).toBeTruthy()
  })
})

test.describe('TST11-SC-020: User-scoped activity', () => {
  test('user history contains actions from the current session', async ({ request }) => {
    const boardId = await createBoard(request, `User Audit ${Date.now()}`)
    await addColumn(request, boardId, 'Backlog', 0)

    const history = await getUserHistory(request)
    expect(history.length).toBeGreaterThan(0)
  })
})

test.describe('TST11-SC-021: Audit entries for board mutations', () => {
  test('create, rename, archive, and restore all generate audit entries', async ({ request }) => {
    // Create
    const boardId = await createBoard(request, `Mutation Audit ${Date.now()}`)

    // Rename
    await request.put(`${API_BASE_URL}/boards/${boardId}`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { name: `Renamed Mutation Audit ${Date.now()}` },
    })

    // Archive
    await request.put(`${API_BASE_URL}/boards/${boardId}`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { isArchived: true },
    })

    // Restore (unarchive)
    await request.put(`${API_BASE_URL}/boards/${boardId}`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: { isArchived: false },
    })

    const history = await getBoardHistory(request, boardId)
    // Should have at least entries for create + rename + archive + restore
    expect(history.length).toBeGreaterThanOrEqual(4)

    const actionTypes = new Set(history.map((e) => e.action))
    // Created (0) should be present from board creation
    expect(actionTypes.has(AuditAction.Created)).toBeTruthy()
    // Updated (1) should be present from rename
    expect(actionTypes.has(AuditAction.Updated)).toBeTruthy()
    // Archived (3) should be present from archive
    expect(actionTypes.has(AuditAction.Archived)).toBeTruthy()
    // Unarchived (4) should be present from restore
    expect(actionTypes.has(AuditAction.Unarchived)).toBeTruthy()
  })
})

test.describe('TST11-SC-023: Activity view mode switching', () => {
  test('activity view supports board, entity, and user mode selection', async ({ page, request }) => {
    // Create a board so there is data to work with
    await createBoard(request, `Activity Mode ${Date.now()}`)

    await page.goto('/workspace/activity')
    await expect(page.getByRole('heading', { name: 'Activity', exact: true })).toBeVisible()

    // Verify the hero actions are present (use .first() since the button also
    // appears inside the WorkspaceHelpCallout actions slot)
    await expect(page.getByRole('button', { name: 'Open Review' }).first()).toBeVisible()
    await expect(page.getByRole('button', { name: 'Open Boards' }).first()).toBeVisible()

    // Locate the mode selector dropdown
    const modeSelector = page.locator('select[aria-label="Activity view mode"]')
    await expect(modeSelector).toBeVisible()

    // Verify all three options exist
    const options = modeSelector.locator('option')
    await expect(options).toHaveCount(3)
    await expect(options.nth(0)).toHaveText('Board History')
    await expect(options.nth(1)).toHaveText('Entity History')
    await expect(options.nth(2)).toHaveText('User History')

    // Default mode should be 'board'
    await expect(modeSelector).toHaveValue('board')

    // Switch to 'entity' mode and verify entity-specific controls appear
    await modeSelector.selectOption('entity')
    await expect(modeSelector).toHaveValue('entity')
    const entityTypeSelector = page.locator('select[aria-label="Select entity type"]')
    await expect(entityTypeSelector).toBeVisible()

    // Switch to 'user' mode and verify user-specific content appears
    await modeSelector.selectOption('user')
    await expect(modeSelector).toHaveValue('user')
    await expect(page.getByText('Current user:')).toBeVisible()

    // Switch back to 'board' mode and verify board selector reappears
    await modeSelector.selectOption('board')
    await expect(modeSelector).toHaveValue('board')
    const boardSelector = page.locator('select[aria-label="Select board"]')
    await expect(boardSelector).toBeVisible()
  })
})

test.describe('TST11-SC-024: Activity selector discoverability', () => {
  test('activity view shows help callout and navigation buttons', async ({ page, request }) => {
    await createBoard(request, `Activity Discover ${Date.now()}`)

    await page.goto('/workspace/activity')
    await expect(page.getByRole('heading', { name: 'Activity', exact: true })).toBeVisible()

    // Help callout should be visible
    await expect(page.getByText('Why do these selectors matter?')).toBeVisible()

    // Navigation links
    await expect(page.getByRole('button', { name: 'Open Review' }).first()).toBeVisible()
    await expect(page.getByRole('button', { name: 'Open Boards' }).first()).toBeVisible()
  })
})

test.describe('TST11-SC-025: Audit unauthorized access', () => {
  test('audit endpoints return 401 without authentication', async ({ request }) => {
    const boardResponse = await request.get(
      `${API_BASE_URL}/audit/boards/00000000-0000-0000-0000-000000000000`,
    )
    expect(boardResponse.status()).toBe(401)

    const entityResponse = await request.get(
      `${API_BASE_URL}/audit/entities/card/00000000-0000-0000-0000-000000000000`,
    )
    expect(entityResponse.status()).toBe(401)

    const userResponse = await request.get(`${API_BASE_URL}/audit/users/me`)
    expect(userResponse.status()).toBe(401)
  })

  test('cross-user board audit returns 403', async ({ request }) => {
    // UserA creates a board
    const boardId = await createBoard(request, `Cross Audit ${Date.now()}`)

    // Register UserB
    const userB = await registerUserSession(request, 'audit-cross-b')

    // UserB tries to access UserA's board audit
    const response = await request.get(
      `${API_BASE_URL}/audit/boards/${boardId}`,
      { headers: { Authorization: `Bearer ${userB.token}` } },
    )

    // Should be 403 (forbidden) or 404 (board not found for this user)
    expect([403, 404]).toContain(response.status())
  })
})
