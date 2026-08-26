import { expect, test, type Locator, type Page } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'

type ThemeCase = {
  name: string
  mode: 'paper' | 'paper-night' | 'auto' | 'off'
  colorScheme: 'light' | 'dark'
  bodyClass?: 'paper' | 'paper-night'
}

const themes: ThemeCase[] = [
  { name: 'Paper Light', mode: 'paper', colorScheme: 'light', bodyClass: 'paper' },
  { name: 'Paper Night', mode: 'paper-night', colorScheme: 'dark', bodyClass: 'paper-night' },
  { name: 'Auto light', mode: 'auto', colorScheme: 'light', bodyClass: 'paper' },
  { name: 'Auto dark', mode: 'auto', colorScheme: 'dark', bodyClass: 'paper-night' },
  { name: 'Legacy fallback', mode: 'off', colorScheme: 'light' },
]

function parseRgb(value: string): [number, number, number] {
  const channels = value.match(/[\d.]+/g)?.slice(0, 3).map(Number)
  if (!channels || channels.length !== 3) throw new Error(`Could not parse colour: ${value}`)
  return channels as [number, number, number]
}

function luminance(rgb: [number, number, number]): number {
  const linear = rgb.map((channel) => {
    const value = channel / 255
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2]
}

function contrast(foreground: string, background: string): number {
  const foregroundLuminance = luminance(parseRgb(foreground))
  const backgroundLuminance = luminance(parseRgb(background))
  const [lighter, darker] = foregroundLuminance >= backgroundLuminance
    ? [foregroundLuminance, backgroundLuminance]
    : [backgroundLuminance, foregroundLuminance]
  return (lighter + 0.05) / (darker + 0.05)
}

async function computedState(locator: Locator) {
  return await locator.evaluate((element) => {
    const style = getComputedStyle(element)
    return {
      background: style.backgroundColor,
      color: style.color,
      boxShadow: style.boxShadow,
      cursor: style.cursor,
      opacity: Number(style.opacity),
      transform: style.transform,
    }
  })
}

async function expectContrast(locator: Locator, context: string) {
  const style = await computedState(locator)
  expect(contrast(style.color, style.background), context).toBeGreaterThanOrEqual(4.5)
  return style
}

async function exerciseStates(page: Page, locator: Locator, context: string, selected: boolean) {
  await page.locator('.paper-appearance__title').hover()
  const rest = await expectContrast(locator, `${context}: rest contrast`)

  await locator.hover()
  const hover = await expectContrast(locator, `${context}: hover contrast`)
  if (selected) {
    expect(hover.background, `${context}: selected hover background`).toBe(rest.background)
    expect(hover.color, `${context}: selected hover foreground`).toBe(rest.color)
  }

  await page.keyboard.press('Tab')
  await locator.focus()
  expect(await locator.evaluate((element) => element.matches(':focus-visible')), `${context}: focus-visible`).toBe(true)
  const focused = await expectContrast(locator, `${context}: focus contrast`)
  expect(focused.boxShadow, `${context}: focus ring`).not.toBe('none')

  if (selected) {
    await locator.hover()
    const box = await locator.boundingBox()
    if (!box) throw new Error(`${context}: selected segment has no bounding box`)
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
    await page.mouse.down()
    const pressed = await expectContrast(locator, `${context}: pressed contrast`)
    expect(pressed.background, `${context}: selected pressed background`).toBe(rest.background)
    expect(pressed.color, `${context}: selected pressed foreground`).toBe(rest.color)
    expect(pressed.transform, `${context}: pressed feedback`).not.toBe('none')
    await page.mouse.up()
  }

  await locator.evaluate((element: HTMLButtonElement) => { element.disabled = true })
  const disabledRest = await expectContrast(locator, `${context}: disabled contrast`)
  expect(disabledRest.cursor, `${context}: disabled cursor`).toBe('default')
  expect(disabledRest.opacity, `${context}: disabled opacity`).toBeLessThan(1)
  await locator.hover({ force: true })
  const disabledHover = await computedState(locator)
  expect(disabledHover.background, `${context}: disabled hover background`).toBe(disabledRest.background)
  expect(disabledHover.color, `${context}: disabled hover foreground`).toBe(disabledRest.color)
  await locator.evaluate((element: HTMLButtonElement) => { element.disabled = false })
}

test('Appearance segments keep legible composed states across themes', async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'appearance-contrast')

  for (const theme of themes) {
    await page.emulateMedia({ colorScheme: theme.colorScheme })
    await page.goto('/workspace/settings/appearance')
    await page.evaluate((mode) => {
      localStorage.setItem('td.paper.mode.v2', mode)
      localStorage.setItem('td.locale.v1', 'en')
    }, theme.mode)
    await page.reload()
    await expect(page.locator('.paper-appearance')).toBeVisible()
    await page.addStyleTag({ content: '.paper-appearance__segment { transition: none !important; }' })

    if (theme.bodyClass) {
      await expect(page.locator('body')).toHaveClass(new RegExp(`(^|\\s)${theme.bodyClass}(\\s|$)`))
    } else {
      await expect(page.locator('body')).not.toHaveClass(/(^|\s)paper(?:-night)?(\s|$)/)
    }

    const selectedTheme = page.locator(`[data-mode="${theme.mode}"]`)
    const unselectedThemeMode = theme.mode === 'paper-night' ? 'paper' : 'paper-night'
    const unselectedTheme = page.locator(`[data-mode="${unselectedThemeMode}"]`)
    const selectedLanguage = page.locator('[data-locale="en"]')
    const unselectedLanguage = page.locator('[data-locale="it"]')

    await expect(selectedTheme).toHaveAttribute('aria-pressed', 'true')
    await expect(selectedLanguage).toHaveAttribute('aria-pressed', 'true')
    await exerciseStates(page, selectedTheme, `${theme.name} Theme selected`, true)
    await exerciseStates(page, unselectedTheme, `${theme.name} Theme unselected`, false)
    await exerciseStates(page, selectedLanguage, `${theme.name} Language selected`, true)
    await exerciseStates(page, unselectedLanguage, `${theme.name} Language unselected`, false)

    if (theme.mode === 'paper') {
      await unselectedTheme.focus()
      await page.keyboard.press('Space')
      await expect(unselectedTheme).toHaveAttribute('aria-pressed', 'true')

      await unselectedLanguage.focus()
      await page.keyboard.press('Space')
      await expect(unselectedLanguage).toHaveAttribute('aria-pressed', 'true')
    }
  }
})
