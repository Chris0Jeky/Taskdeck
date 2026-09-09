import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

test('approved high-risk proposals with more than five operations require explicit batch Apply', async ({ page, request }) => {
  const auth = await registerAndAttachSession(page, request, 'batch-eligibility')
  const headers = { Authorization: `Bearer ${auth.token}` }
  const seed = `${Date.now()}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Batch eligibility', description: 'Synthetic approved batch', columnNamePrefix: 'Todo',
  })
  const boardResponse = await request.get(`${API_BASE_URL}/boards/${boardId}`, { headers })
  await assertOk(boardResponse, 'read synthetic batch board')
  const board = await boardResponse.json() as { columns: { id: string }[] }
  const columnId = board.columns[0]!.id
  const created = await request.post(`${API_BASE_URL}/automation/proposals`, {
    headers,
    data: {
      sourceType: 1, requestedByUserId: auth.user.id, boardId,
      summary: 'Six explicitly approved cards', riskLevel: 2, correlationId: `batch-${seed}`,
      operations: Array.from({ length: 6 }, (_, sequence) => ({
        sequence, actionType: 'create', targetType: 'card', idempotencyKey: `batch-${seed}-${sequence}`,
        parameters: JSON.stringify({ title: `Batch card ${sequence}`, boardId, columnId }),
      })),
    },
  })
  await assertOk(created, 'create high-risk proposal')
  const proposal = await created.json() as { id: string }
  await assertOk(await request.post(`${API_BASE_URL}/automation/proposals/${proposal.id}/approve`, { headers }), 'approve proposal separately')

  let executeRequests = 0
  page.on('request', req => {
    if (req.method() === 'POST' && new URL(req.url()).pathname.endsWith('/automation/proposals/execute')) executeRequests++
  })
  await page.goto(`/workspace/review?boardId=${boardId}`)
  const apply = page.getByTestId('queue-batch-execute')
  await expect(apply).toBeVisible()
  await apply.click()
  const confirm = page.getByTestId('batch-execute-confirm')
  await expect(confirm).toBeVisible()
  expect(executeRequests).toBe(0)
  const before = await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })
  await assertOk(before, 'read before explicit Apply')
  expect(await before.json()).toHaveLength(0)

  const applied = page.waitForResponse(response => response.request().method() === 'POST'
    && new URL(response.url()).pathname.endsWith('/automation/proposals/execute'))
  await confirm.click()
  await assertOk(await applied, 'apply approved high-risk proposal')
  await expect(page.getByTestId('batch-execute-receipt-summary')).toContainText('Applied 1')
  expect(executeRequests).toBe(1)
  const after = await request.get(`${API_BASE_URL}/boards/${boardId}/cards`, { headers })
  await assertOk(after, 'read after explicit Apply')
  expect(await after.json()).toHaveLength(6)
})
