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

function mix(foreground: [number, number, number], background: [number, number, number], opacity: number): [number, number, number] {
  return foreground.map((channel, index) => (
    channel * opacity + background[index] * (1 - opacity)
  )) as [number, number, number]
}

function contrast(foreground: [number, number, number], background: [number, number, number]): number {
  const foregroundLuminance = luminance(foreground)
  const backgroundLuminance = luminance(background)
  const [lighter, darker] = foregroundLuminance >= backgroundLuminance
    ? [foregroundLuminance, backgroundLuminance]
    : [backgroundLuminance, foregroundLuminance]
  return (lighter + 0.05) / (darker + 0.05)
}

async function computedState(locator: Locator) {
  return await locator.evaluate((element) => {
    const style = getComputedStyle(element)
    let substrate = element.parentElement
    while (substrate) {
      const background = getComputedStyle(substrate).backgroundColor
      if (background !== 'transparent' && !/rgba\([^)]*,\s*0\s*\)$/.test(background)) break
      substrate = substrate.parentElement
    }
    return {
      background: style.backgroundColor,
      color: style.color,
      boxShadow: style.boxShadow,
      cursor: style.cursor,
      opacity: Number(style.opacity),
      transform: style.transform,
      transitionProperty: style.transitionProperty,
      underlay: substrate ? getComputedStyle(substrate).backgroundColor : 'rgb(255, 255, 255)',
    }
  })
}

async function expectContrast(locator: Locator, context: string) {
  const style = await computedState(locator)
  const underlay = parseRgb(style.underlay)
  const renderedForeground = mix(parseRgb(style.color), underlay, style.opacity)
  const renderedBackground = mix(parseRgb(style.background), underlay, style.opacity)
  expect(contrast(renderedForeground, renderedBackground), context).toBeGreaterThanOrEqual(4.5)
  return style
}

async function exerciseStates(page: Page, locator: Locator, context: string, selected: boolean) {
  await page.locator('.paper-appearance__title').hover()
  const rest = await expectContrast(locator, `${context}: rest contrast`)
  const transitionedProperties = rest.transitionProperty.split(',').map((property) => property.trim())
  expect(transitionedProperties, `${context}: atomic colour transition`).not.toContain('color')
  expect(transitionedProperties, `${context}: atomic background transition`).not.toContain('background-color')

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

  await locator.hover()
  const box = await locator.boundingBox()
  if (!box) throw new Error(`${context}: segment has no bounding box`)
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
  await page.mouse.down()
  const pressed = await expectContrast(locator, `${context}: pressed contrast`)
  expect(pressed.transform, `${context}: pressed feedback`).not.toBe('none')
  if (selected) {
    expect(pressed.background, `${context}: selected pressed background`).toBe(rest.background)
    expect(pressed.color, `${context}: selected pressed foreground`).toBe(rest.color)
  }
  await page.locator('.paper-appearance__title').hover({ force: true })
  await page.mouse.up()
  await expect(locator).toHaveAttribute('aria-pressed', selected ? 'true' : 'false')

  await locator.evaluate((element: HTMLButtonElement) => { element.disabled = true })
  const disabledRest = await expectContrast(locator, `${context}: disabled contrast`)
  expect(disabledRest.cursor, `${context}: disabled cursor`).toBe('default')
  expect(disabledRest.opacity, `${context}: disabled opacity`).toBe(1)
  await locator.hover({ force: true })
  const disabledHover = await computedState(locator)
  expect(disabledHover.background, `${context}: disabled hover background`).toBe(disabledRest.background)
  expect(disabledHover.color, `${context}: disabled hover foreground`).toBe(disabledRest.color)
  await locator.evaluate((element: HTMLButtonElement) => { element.disabled = false })
}

async function sampleSelectionTransition(
  page: Page,
  locator: Locator,
  context: string,
  keyboard: boolean,
) {
  await expect(locator).toHaveAttribute('aria-pressed', 'false')
  if (keyboard) {
    await locator.focus()
    await page.keyboard.press('Space')
  } else {
    await locator.click()
  }
  await expect(locator).toHaveAttribute('aria-pressed', 'true')

  let previousSample = 0
  for (const elapsed of [0, 35, 70, 105, 140, 175]) {
    await page.waitForTimeout(elapsed - previousSample)
    await expectContrast(locator, `${context}: ${elapsed}ms transition contrast`)
    previousSample = elapsed
  }
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

    const useKeyboard = theme.mode === 'paper'
    await sampleSelectionTransition(
      page,
      unselectedLanguage,
      `${theme.name} Language selection`,
      useKeyboard,
    )
    await sampleSelectionTransition(
      page,
      unselectedTheme,
      `${theme.name} Theme selection`,
      useKeyboard,
    )
  }
})
