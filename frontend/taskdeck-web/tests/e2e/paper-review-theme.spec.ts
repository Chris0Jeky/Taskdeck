import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { createCaptureItem, triageCaptureItem, waitForProposalCreated } from './support/captureFlow'

for (const mode of ['paper', 'paper-night'] as const) {
  test(`populated Review inherits the selected ${mode} theme`, async ({ page, request }) => {
    await page.setViewportSize({ width: 1440, height: 1000 })
    await page.addInitScript((selectedMode) => {
      window.localStorage.setItem('td.paper.mode.v2', selectedMode)
    }, mode)
    const auth = await registerAndAttachSession(page, request, `review-theme-${mode}`)
    const boardId = await createBoardWithColumn(request, auth, `${Date.now()}`, {
      boardNamePrefix: 'Review theme',
      description: 'Synthetic theme inheritance fixture.',
      columnNamePrefix: 'Review lane',
    })
    const capture = await createCaptureItem(request, auth, boardId, 'Create card "Review theme proof"')
    await triageCaptureItem(request, auth, capture.id)
    const triaged = await waitForProposalCreated(request, auth, capture.id)

    await page.goto(`/workspace/review#proposal=${triaged.provenance!.proposalId}`)
    await expect(page.locator('body')).toHaveClass(new RegExp(`(^|\\s)${mode}(\\s|$)`))
    const review = page.getByTestId('paper-review-view')
    await expect(review).toBeVisible()
    await expect(review.getByRole('heading', { level: 1, name: /Review theme proof/ })).toBeVisible()

    const colors = await review.evaluate((element) => {
      const shell = getComputedStyle(document.body)
      const content = getComputedStyle(element)
      const tokens = ['--paper', '--paper-card', '--ink', '--ember']
      const expectedBackground = document.createElement('span')
      expectedBackground.style.backgroundColor = shell.getPropertyValue('--paper').trim()
      return {
        shell: tokens.map((token) => shell.getPropertyValue(token).trim()),
        content: tokens.map((token) => content.getPropertyValue(token).trim()),
        background: content.backgroundColor,
        expectedBackground: expectedBackground.style.backgroundColor,
      }
    })
    expect(colors.shell.every(Boolean)).toBe(true)
    expect(colors.content).toEqual(colors.shell)
    expect(colors.background).toBe(colors.expectedBackground)
  })
}
