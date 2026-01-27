import { expect, test } from '@playwright/test'

test('board to card workflow smoke test', async ({ page }) => {
  const boardName = `Smoke Board ${Date.now()}`
  const columnName = `To Do ${Date.now()}`
  const cardTitle = `Smoke Card ${Date.now()}`

  await page.goto('/boards')

  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()

  await expect(page).toHaveURL(/\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()

  await page.getByRole('button', { name: '+ Add Column' }).click()
  await page.getByPlaceholder('Column name').fill(columnName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()

  await expect(page.getByText(columnName)).toBeVisible()

  await page.getByRole('button', { name: 'Add Card' }).first().click()
  await page.getByPlaceholder('Enter card title...').fill(cardTitle)
  await page.getByRole('button', { name: 'Add' }).click()

  await expect(page.getByText(cardTitle)).toBeVisible()
})

test('filter panel shortcut should toggle panel', async ({ page }) => {
  const boardName = `Filter Board ${Date.now()}`

  await page.goto('/boards')

  await page.getByRole('button', { name: '+ New Board' }).click()
  await page.getByPlaceholder('Board name').fill(boardName)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/boards\/[a-f0-9-]+$/)

  await page.keyboard.press('f')
  await expect(page.getByRole('heading', { name: 'Filter Cards' })).toBeVisible()

  await page.keyboard.press('f')
  await expect(page.getByRole('heading', { name: 'Filter Cards' })).not.toBeVisible()
})
