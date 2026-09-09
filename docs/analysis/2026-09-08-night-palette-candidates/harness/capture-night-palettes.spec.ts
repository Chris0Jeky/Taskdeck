import { expect, test, type Page } from '@playwright/test'
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from '../../../../frontend/taskdeck-web/tests/e2e/support/authSession'
import { createBoardWithColumn } from '../../../../frontend/taskdeck-web/tests/e2e/support/boardHelpers'
import {
  createCaptureItem,
  triageCaptureItem,
  waitForProposalCreated,
} from '../../../../frontend/taskdeck-web/tests/e2e/support/captureFlow'

type Palette = {
  id: string
  label: string
  intent: string
  overrides: Record<string, string>
}

type PaletteConfig = {
  source: string
  viewport: { width: number; height: number }
  variants: Palette[]
}

type Surface = { id: string; label: string; path: string; ready: string }

const reportDir = resolve(process.cwd(), '..', '..', 'docs', 'analysis', '2026-09-08-night-palette-candidates')
const harnessDir = resolve(reportDir, 'harness')
const captureDir = resolve(reportDir, 'captures', '1440x1000')
const palettes = JSON.parse(readFileSync(resolve(reportDir, 'palettes.json'), 'utf8')) as PaletteConfig
const tokenCss = readFileSync(resolve(reportDir, '..', '..', '..', 'frontend', 'taskdeck-web', 'src', 'paper-tokens.css'), 'utf8')
const surfaces: Surface[] = [
  { id: 'home', label: 'Home', path: '/workspace/home', ready: '[data-testid="paper-home"]' },
  { id: 'today', label: 'Today', path: '/workspace/today', ready: '[data-paper-today]' },
  { id: 'review', label: 'Review', path: '/workspace/review', ready: '[data-testid="paper-review-view"]' },
]
const lightBaseline: Palette = {
  id: 'light-baseline',
  label: 'Light baseline parity',
  intent: 'The shipped light Paper mode, captured with no token overrides.',
  overrides: {},
}

const inspectedTokens = [
  '--paper', '--paper-2', '--paper-card', '--paper-edge', '--line', '--line-soft', '--whisper', '--faint',
  '--ink', '--ink-deep', '--ink-2', '--mute', '--ember', '--ember-deep', '--ember-bloom', '--ember-tint',
  '--ember-ink', '--td-on-ember', '--applied', '--applied-tint', '--overdue', '--overdue-tint',
]

function readTokenBlock(selector: string): Record<string, string> {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const block = tokenCss.match(new RegExp(`${escapedSelector}\\s*\\{([\\s\\S]*?)\\n\\}`))?.[1]
  if (!block) throw new Error(`Could not locate ${selector} in paper-tokens.css`)
  return Object.fromEntries(
    [...block.matchAll(/(--[a-z0-9-]+):\s*(#[0-9a-fA-F]{3,8})/g)]
      .map((match) => [match[1], match[2].toLowerCase()]),
  )
}

const baseTokens = {
  paper: readTokenBlock('.paper'),
  'paper-night': readTokenBlock('.paper-night'),
}

function asCssRule(overrides: Record<string, string>): string {
  return Object.entries(overrides).map(([name, value]) => `${name}: ${value} !important;`).join(' ')
}

async function selectPaperMode(page: Page, mode: 'paper' | 'paper-night') {
  await page.addInitScript((selectedMode) => {
    window.localStorage.setItem('td.paper.mode.v2', selectedMode)
  }, mode)
}

async function applyVariant(page: Page, palette: Palette, mode: 'paper' | 'paper-night') {
  const experimentalNightRootTokens = mode === 'paper-night' && palette.id !== 'baseline'
    ? { ...baseTokens['paper-night'], ...palette.overrides }
    : {}
  const cssText = asCssRule(experimentalNightRootTokens)
  await page.evaluate(({ selectedMode, cssText }) => {
    document.body.classList.toggle('paper-night', selectedMode === 'paper-night')
    document.body.classList.toggle('paper', selectedMode === 'paper')
    document.querySelector('[data-night-palette-override]')?.remove()
    if (!cssText) return
    const style = document.createElement('style')
    style.dataset.nightPaletteOverride = 'true'
    // Review's real root currently carries `.paper` even when the selected
    // user mode is `paper-night`. Candidate captures scope the experimental
    // night token set to that real root so the comparison shows the proposed
    // surfaces while preserving the shipped baseline parity finding.
    style.textContent = `body.${selectedMode}, body.${selectedMode} .paper { ${cssText} }`
    document.head.append(style)
  }, { selectedMode: mode, cssText })
  await expect.poll(() => page.evaluate((selectedMode) => {
    return document.body.classList.contains(selectedMode)
  }, mode)).toBe(true)
}

async function readComputedTokens(page: Page): Promise<Record<string, string>> {
  return await page.evaluate((names) => {
    const styles = window.getComputedStyle(document.body)
    return Object.fromEntries(names.map((name) => [name, styles.getPropertyValue(name).trim().toLowerCase()]))
  }, inspectedTokens)
}

async function readRootMeasurements(page: Page, surface: Surface) {
  return await page.evaluate(({ ready, names }) => {
    const root = document.querySelector<HTMLElement>(ready)
    if (!root) throw new Error(`Missing render root ${ready}`)
    const read = (element: Element) => {
      const styles = window.getComputedStyle(element)
      return {
        tokens: Object.fromEntries(names.map((name) => [name, styles.getPropertyValue(name).trim().toLowerCase()])),
        backgroundColor: styles.backgroundColor,
      }
    }
    const sampleCard = root.querySelector<HTMLElement>('.card, [class*="card"]')
    return {
      rootTag: root.tagName.toLowerCase(),
      rootClasses: root.className,
      root: read(root),
      sampleCard: sampleCard ? read(sampleCard) : null,
    }
  }, { ready: surface.ready, names: inspectedTokens })
}

function readPngDimensions(path: string): { width: number; height: number } {
  const bytes = readFileSync(path)
  if (bytes.length < 24 || bytes.readUInt32BE(0) !== 0x89504e47 || bytes.toString('ascii', 12, 16) !== 'IHDR') {
    throw new Error(`Not a PNG: ${path}`)
  }
  return { width: bytes.readUInt32BE(16), height: bytes.readUInt32BE(20) }
}

async function captureSurface(page: Page, surface: Surface, palette: Palette, mode: 'paper' | 'paper-night') {
  await selectPaperMode(page, mode)
  await page.goto(surface.path)
  await expect(page.locator(surface.ready)).toBeVisible()
  await expect(page.locator('body')).toHaveClass(new RegExp(`(^|\\s)${mode}(\\s|$)`))
  await applyVariant(page, palette, mode)

  const tokenValues = await readComputedTokens(page)
  const rootMeasurements = await readRootMeasurements(page, surface)
  const viewport = page.viewportSize()
  expect(viewport).toEqual(palettes.viewport)
  const screenshotDir = resolve(captureDir, surface.id)
  mkdirSync(screenshotDir, { recursive: true })
  const screenshotPath = resolve(screenshotDir, `${palette.id}.png`)
  await page.screenshot({ path: screenshotPath, fullPage: false })
  const pngDimensions = readPngDimensions(screenshotPath)
  expect(pngDimensions).toEqual(palettes.viewport)

  const visibleText = (await page.locator('body').innerText()).trim()
  expect(visibleText.length, `${surface.label}/${palette.label} should contain rendered app content`).toBeGreaterThan(120)
  return {
    screenshot: `captures/1440x1000/${surface.id}/${palette.id}.png`,
    tokenValues,
    rootMeasurements,
    viewport,
    pngDimensions,
    visibleTextLength: visibleText.length,
  }
}

test('capture populated Paper surfaces across night candidates and light parity', async ({ page, request }) => {
  test.setTimeout(180_000)
  const auth = await registerAndAttachSession(page, request, 'nightpalette')
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Night palette evidence',
    description: 'Synthetic Mock dataset for issue 2009 rendered palette comparison.',
    columnNamePrefix: 'Review queue',
  })
  const captureText = '- [ ] Night palette proof'
  const capture = await createCaptureItem(request, auth, boardId, captureText)
  await triageCaptureItem(request, auth, capture.id)
  const triaged = await waitForProposalCreated(request, auth, capture.id)
  expect(triaged.provenance?.proposalId).toBeTruthy()

  const nightVariants = palettes.variants.map((variant) => ({ palette: variant, mode: 'paper-night' as const }))
  const allVariants = [
    { palette: lightBaseline, mode: 'paper' as const },
    ...nightVariants,
  ]
  const records: Array<Record<string, unknown>> = []
  for (const variant of allVariants) {
    for (const surface of surfaces) {
      const result = await captureSurface(page, surface, variant.palette, variant.mode)
      const expectedTokens = {
        ...baseTokens[variant.mode],
        ...(variant.mode === 'paper-night' ? variant.palette.overrides : {}),
      }
      const rootExpectedTokens = variant.mode === 'paper-night' && variant.palette.id === 'baseline' && surface.id !== 'review'
        ? expectedTokens
        : variant.mode === 'paper-night' && variant.palette.id !== 'baseline'
          ? expectedTokens
          : baseTokens.paper
      const mismatches = inspectedTokens.filter((name) => {
        const expected = expectedTokens[name]
        const actual = result.tokenValues[name]
        return expected !== undefined && actual !== expected.toLowerCase()
      }).map((name) => ({ name, expected: expectedTokens[name], actual: result.tokenValues[name] }))
      const rootMismatches = inspectedTokens.filter((name) => {
        const expected = rootExpectedTokens[name]
        const actual = result.rootMeasurements.root.tokens[name]
        return expected !== undefined && actual !== expected.toLowerCase()
      }).map((name) => ({
        name,
        expected: rootExpectedTokens[name],
        actual: result.rootMeasurements.root.tokens[name],
      }))
      expect(mismatches, `${surface.label}/${variant.palette.label} browser tokens`).toEqual([])
      if (variant.palette.id !== 'baseline' || variant.mode === 'paper') {
        expect(rootMismatches, `${surface.label}/${variant.palette.label} render-root tokens`).toEqual([])
      }
      records.push({
        surface: surface.id,
        surfaceLabel: surface.label,
        palette: variant.palette.id,
        paletteLabel: variant.palette.label,
        selectedMode: variant.mode,
        screenshot: result.screenshot,
        viewport: result.viewport,
        pngDimensions: result.pngDimensions,
        visibleTextLength: result.visibleTextLength,
        expectedTokens,
        computedTokens: result.tokenValues,
        renderRoot: result.rootMeasurements,
        mismatches,
        rootMismatches,
        parityFinding: variant.palette.id === 'baseline' && variant.mode === 'paper-night' && surface.id === 'review'
          ? 'Shipped Review root remains .paper/light while body selection is paper-night; candidate rows apply an ephemeral night token set to the real .paper root.'
          : null,
      })
    }
  }

  writeFileSync(resolve(reportDir, 'rendered-token-measurements.json'), JSON.stringify({
    generatedAt: new Date().toISOString(),
    viewport: palettes.viewport,
    syntheticDataset: { boardId, captureId: capture.id, proposalId: triaged.provenance?.proposalId, seed },
    selectedModes: { night: 'paper-night', lightParity: 'paper' },
    records,
  }, null, 2) + '\n')
})
