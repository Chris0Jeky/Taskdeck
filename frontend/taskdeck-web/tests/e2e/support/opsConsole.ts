import { expect, type Page } from '@playwright/test'

export async function selectOpsTemplate(page: Page, templateName: string): Promise<void> {
  const toolbar = page.locator('.td-cli-toolbar')
  const templateInput = toolbar.getByRole('combobox', { name: 'Command template' })

  await expect(page.locator('.td-template-meta')).toBeVisible()
  await templateInput.fill(templateName)

  if (await templateInput.getAttribute('aria-expanded') === 'true') {
    await templateInput.press('Enter')
  }

  await expect(templateInput).toHaveValue(templateName)
  await expect(templateInput).toHaveAttribute('aria-expanded', 'false')
  await expect(toolbar.locator('[role="listbox"]:visible')).toHaveCount(0)
}
