import type { APIRequestContext } from '@playwright/test'
import { expect } from '@playwright/test'
import { API_BASE_URL, type AuthResult } from './authSession'
import { assertOk } from './httpAsserts'

interface ImportResultDto {
  success: boolean
  boardId: string | null
}

export interface CreateBoardWithColumnOptions {
  boardNamePrefix: string
  description: string
  columnNamePrefix: string
}

export async function createBoardWithColumn(
  request: APIRequestContext,
  auth: AuthResult,
  seed: string,
  options: CreateBoardWithColumnOptions,
): Promise<string> {
  const importResponse = await request.post(`${API_BASE_URL}/import/boards?userId=${encodeURIComponent(auth.user.id)}`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: {
      name: `${options.boardNamePrefix} ${seed}`,
      description: options.description,
      columns: [
        {
          name: `${options.columnNamePrefix} ${seed}`,
          position: 0,
          wipLimit: null,
        },
      ],
      cards: [],
      labels: [],
    },
  })

  await assertOk(importResponse, `Import board '${options.boardNamePrefix} ${seed}'`)
  const importResult = await importResponse.json() as ImportResultDto
  expect(importResult.success).toBeTruthy()
  expect(importResult.boardId).toBeTruthy()
  return importResult.boardId!
}
