import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'

/**
 * The API/worker round trip is covered by CaptureApiTests. This browser spec deliberately mocks
 * only the capture endpoints so it can isolate the Paper contract: Save must finish before Ask AI,
 * and the corrected text must be the one rendered when the proposal state returns.
 */
const itemId = '11111111-1111-4111-8111-111111111111'
const boardId = '22222222-2222-4222-8222-222222222222'
const proposalId = '33333333-3333-4333-8333-333333333333'

test('Paper wires correction Save before an explicit Ask AI request (mocked capture API)', async ({
  page,
  request,
}) => {
  await registerAndAttachSession(page, request, 'tx-correct-paper')

  const canonicalText = 'Canonical transcript text'
  const correctedText = 'Corrected transcript text'
  let status: 'Triaged' | 'ProposalCreated' = 'Triaged'
  let text = canonicalText
  let savePayload: Record<string, unknown> | null = null
  let triagePayload: Record<string, unknown> | null = null

  const summary = () => ({
    id: itemId,
    userId: 'e2e-user',
    boardId,
    status,
    source: 'TranscriptPaste',
    textExcerpt: text,
    createdAt: '2026-09-09T10:00:00Z',
    processedAt: '2026-09-09T10:01:00Z',
    errorMessage: null,
    disposition: null,
    canEditSuggestion: status === 'Triaged',
  })

  const detail = () => ({
    ...summary(),
    rawText: text,
    retryCount: 0,
    provenance: status === 'ProposalCreated'
      ? {
          captureItemId: itemId,
          triageRunId: '44444444-4444-4444-8444-444444444444',
          proposalId,
          promptVersion: 'e2e',
        }
      : null,
    metadata: { dueDate: null, labels: [] },
  })

  await page.route('**/api/capture/items**', async (route) => {
    const requestUrl = new URL(route.request().url())
    const path = requestUrl.pathname
    const method = route.request().method()
    const itemPath = `/api/capture/items/${itemId}`

    if (method === 'GET' && path === '/api/capture/items') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([summary()]) })
      return
    }

    if (method === 'GET' && path === `${itemPath}/status`) {
      const { id, status, processedAt, errorMessage, disposition, canEditSuggestion } = summary()
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ id, status, processedAt, errorMessage, disposition, canEditSuggestion }) })
      return
    }

    if (method === 'GET' && path === itemPath) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(detail()) })
      return
    }

    if (method === 'PUT' && path === `${itemPath}/suggestion`) {
      savePayload = route.request().postDataJSON() as Record<string, unknown>
      text = String(savePayload.text)
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(detail()) })
      return
    }

    if (method === 'POST' && path === `${itemPath}/triage`) {
      triagePayload = route.request().postDataJSON() as Record<string, unknown> | null
      status = 'ProposalCreated'
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ id: itemId, status: 'Triaging', alreadyTriaging: false }),
      })
      return
    }

    await route.continue()
  })

  await page.goto('/workspace/inbox')
  const row = page.locator(`.paper-triage__row[data-item-id="${itemId}"]`)
  await expect(row).toBeVisible()

  await row.locator('[data-action="edit"]').click()
  const editor = row.getByTestId('capture-edit')
  await expect(editor).toHaveAttribute('data-edit-state', 'ready')
  const textarea = editor.getByTestId('capture-edit-textarea')
  await expect(textarea).toHaveValue(canonicalText)
  await textarea.fill(correctedText)
  await editor.locator('[data-action="edit-save"]').click()

  await expect.poll(() => savePayload).toEqual({ text: correctedText })
  await expect(row).toContainText(correctedText)
  await expect(row.locator('[data-action="edit"]')).toBeVisible()

  await row.getByRole('button', { name: 'Ask AI', exact: true }).click()

  await expect.poll(() => triagePayload).toEqual({ boardId })
  await expect.poll(() => status).toBe('ProposalCreated')
  await expect(row).toContainText('Ready for review')
  await expect(row.locator('[data-action="edit"]')).toBeDisabled()
})
