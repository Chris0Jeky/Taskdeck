import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'

test('Paper composer uploads a transcript file through the capture route', async ({
  page,
  request,
}, testInfo) => {
  const auth = await registerAndAttachSession(page, request, 'paper-transcript-file')
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Paper Transcript File',
    description: 'Paper transcript file capture route e2e board',
    columnNamePrefix: 'Inbox',
  })

  await page.goto(`/workspace/boards/${boardId}`)
  await expect(page.getByTestId('paper-board-lanes')).toBeVisible()
  await page.getByRole('button', { name: 'Capture here' }).click()
  await expect(page).toHaveURL(new RegExp(`/workspace/inbox\\?boardId=${boardId}$`))

  await expect(page.getByTestId('paper-inbox-capture')).toBeVisible()
  await page.getByTestId('paper-composer-source-transcript').check()

  const transcriptText = '- [ ] Review the uploaded transcript\n- [ ] Keep the capture route intact'
  const transcriptName = 'paper-capture-transcript.txt'
  const fileInput = page.getByTestId('paper-composer-transcript-file').locator('input[type="file"]')
  await fileInput.setInputFiles({
    name: transcriptName,
    mimeType: 'text/plain',
    buffer: Buffer.from(transcriptText, 'utf8'),
  })

  await expect(page.getByTestId('paper-composer-transcript-file-name')).toContainText(transcriptName)
  await expect(page.getByTestId('paper-composer-body')).toHaveValue(transcriptText)
  await page.screenshot({ path: testInfo.outputPath('paper-transcript-file-selected.png'), fullPage: true })

  const createRequestPromise = page.waitForRequest((request) =>
    request.method() === 'POST' && /\/api\/capture\/items$/i.test(request.url()))
  const createResponsePromise = page.waitForResponse((response) =>
    response.request().method() === 'POST' && /\/api\/capture\/items$/i.test(response.url()))
  await page.getByRole('button', { name: /^Capture/ }).click()

  const createRequest = await createRequestPromise
  expect(createRequest.postDataJSON()).toMatchObject({
    boardId,
    text: transcriptText,
    source: 'TranscriptFile',
  })
  await assertOk(await createResponsePromise, 'create Paper transcript-file capture')

  await expect(
    page.locator('.paper-triage__row').filter({ hasText: 'Review the uploaded transcript' }).first(),
  ).toBeVisible()
  await page.screenshot({ path: testInfo.outputPath('paper-transcript-file-capture.png'), fullPage: true })
})
