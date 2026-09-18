import { NodeTypes, parse as parseTemplate } from '@vue/compiler-dom'
import { parse as parseSfc } from '@vue/compiler-sfc'
import { describe, expect, it } from 'vitest'

import { PAPER_VIEW_ROOTS } from './paperRootInventory'

/**
 * Legacy ("off") mode substrate guard for the #1769 Paper view restyle wave.
 *
 * Coverage was extended (#1815) from the four #1780 roots to every Paper-idiom
 * view root in the wave: the #1775 Saved Views root (#1813), the six Settings
 * roots from PR #1808, and the secondary roots from PR #1810.
 *
 * `src/paper-tokens.css` scopes every Paper variable under `.paper` /
 * `.paper-night` — they DO NOT apply at `:root`. With Appearance set to
 * "Off (Legacy / Obsidian)" the body carries neither class
 * (`paperThemeStore.resolveBodyClass('off')` returns `null`), so a restyled
 * view's `color: var(--ink, #1a1814)` resolves to the near-black literal
 * fallback while `AppShell`'s `.td-content` still paints the Obsidian
 * `--td-surface-base` (#131313) — the `.td-shell--paper .td-content` cream
 * repaint is gated on `paperTheme.isOn`. These routes have no Paper/Legacy
 * component switch, so one component renders in both modes.
 *
 * The fix, and the invariant this test pins: a view root that sets the Paper
 * ink MUST also paint the Paper substrate. It is a no-op under
 * `.paper`/`.paper-night` (`.td-content` already paints `var(--paper)` there),
 * and in Legacy mode the two literal fallbacks land together at AA contrast.
 *
 * Sources are pulled in with Vite's `?raw` rather than `node:fs`: this spec is
 * type-checked by `tsconfig.vitest.json`, whose `types` deliberately omits
 * "node", and its quarantine list may only shrink. The glob is deliberately
 * non-recursive: core-loop views under `src/views/paper/` have their own theme
 * boundary and are outside the #1769 restyle wave guarded here.
 */

const TOP_LEVEL_VIEW_SOURCES = import.meta.glob('../../views/*.vue', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

type PaperViewName = (typeof PAPER_VIEW_ROOTS)[number]['view']
type DiscoveredRoot = { view: string; selector: string }

function sourceForView(view: PaperViewName): string {
  const path = `../../views/${view}`
  const source = TOP_LEVEL_VIEW_SOURCES[path]
  if (source === undefined) throw new Error(`Could not load ${path}`)
  return source
}

const VIEW_ROOTS = PAPER_VIEW_ROOTS.map(({ view, selector }) => ({
  view,
  selector,
  source: sourceForView(view),
}))

/**
 * A root satisfies the invariant by painting ANY Paper substrate token, not
 * only `--paper`. Most roots are full-bleed pages and paint `--paper`; a few
 * (e.g. `.paper-not-found`) are self-contained card panels whose substrate is
 * `--paper-card`. Both leave the root's ink on a Paper-family ground in Legacy
 * mode, and the contrast assertion below is measured against whichever literal
 * fallback the root actually paints — so widening the token set does not weaken
 * the legibility guarantee. What is still forbidden is painting nothing.
 */
const SUBSTRATE = /background(?:-color)?:\s*var\(--paper(?:-card|-2)?,\s*(#[0-9a-fA-F]{3,8})\s*\)/
const PAPER_INK = /color:\s*var\(--ink,\s*#[0-9a-fA-F]{3,8}\s*\)/

/** Read the first top-level rule body for `selector` (these blocks contain no nested braces). */
function readRootRule(source: string, selector: string): string {
  const pattern = new RegExp(`^\\${selector}\\s*\\{([\\s\\S]*?)\\}`, 'm')
  const match = source.match(pattern)
  if (!match) throw new Error(`Could not locate the ${selector} rule`)
  return match[1]
}

function parserErrorMessage(error: unknown): string {
  if (error instanceof Error) return error.message
  if (typeof error === 'object' && error !== null && 'message' in error) {
    return String(error.message)
  }
  return String(error)
}

function readTemplate(source: string, filename = '<inline SFC>'): string {
  const parsed = parseSfc(source, { filename })
  if (parsed.errors.length > 0) {
    throw new Error(`Could not parse ${filename}: ${parserErrorMessage(parsed.errors[0])}`)
  }
  if (parsed.descriptor.template === null) {
    throw new Error(`Could not locate the component template in ${filename}`)
  }
  return parsed.descriptor.template.content
}

/** Static `paper-*` classes on top-level template elements only. */
function rootPaperSelectors(source: string, filename = '<inline SFC>'): string[] {
  const selectors = new Set<string>()
  const template = parseTemplate(readTemplate(source, filename), {
    onError: (error) => {
      throw new Error(`Could not parse ${filename}: ${parserErrorMessage(error)}`)
    },
  })

  for (const child of template.children) {
    if (child.type !== NodeTypes.ELEMENT) continue
    for (const property of child.props) {
      if (property.type !== NodeTypes.ATTRIBUTE || property.name !== 'class' || property.value === undefined) {
        continue
      }
      for (const className of property.value.content.split(/\s+/)) {
        if (/^paper-[a-z0-9-]+$/.test(className)) selectors.add(`.${className}`)
      }
    }
  }

  return [...selectors]
}

function viewName(path: string): string {
  return path.slice(path.lastIndexOf('/') + 1)
}

function compareRoots(left: DiscoveredRoot, right: DiscoveredRoot): number {
  if (left.view !== right.view) return left.view < right.view ? -1 : 1
  if (left.selector === right.selector) return 0
  return left.selector < right.selector ? -1 : 1
}

/**
 * Discover the scope independently from the hand-maintained inventory. A
 * `paper-*` name alone is not enough: newer dark-shell views also use that
 * namespace. This guard owns top-level roots whose own rule opts into Paper's
 * `--ink` token, which is precisely the substrate invariant under test.
 */
function discoverPaperInkRoots(): DiscoveredRoot[] {
  return Object.entries(TOP_LEVEL_VIEW_SOURCES)
    .flatMap(([path, source]) =>
      rootPaperSelectors(source, path).flatMap((selector) => {
        try {
          return PAPER_INK.test(readRootRule(source, selector))
            ? [{ view: viewName(path), selector }]
            : []
        } catch {
          return []
        }
      }),
    )
    .sort(compareRoots)
}

function hexToRgb(hex: string): [number, number, number] {
  let h = hex.replace('#', '')
  if (h.length === 3) h = h.split('').map((c) => c + c).join('')
  return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)]
}

function relativeLuminance([r, g, b]: [number, number, number]): number {
  const lin = (c: number) => {
    const s = c / 255
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4)
  }
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)
}

function contrast(a: string, b: string): number {
  const la = relativeLuminance(hexToRgb(a))
  const lb = relativeLuminance(hexToRgb(b))
  const [hi, lo] = la >= lb ? [la, lb] : [lb, la]
  return (hi + 0.05) / (lo + 0.05)
}

describe('Paper view root inventory', () => {
  it('tracks every top-level view root that opts into Paper ink', () => {
    const declared = PAPER_VIEW_ROOTS
      .map(({ view, selector }) => ({ view, selector }))
      .sort(compareRoots)

    expect(discoverPaperInkRoots()).toEqual(declared)
  })

  it('parses native void elements in a discovered SFC template', () => {
    expect(rootPaperSelectors(sourceForView('ApiKeySettingsView.vue'))).toContain('.paper-api-keys')
  })

  it('fails closed with a source name when template parsing fails', () => {
    expect(() => rootPaperSelectors('<template><section></template>')).toThrow('<inline SFC>')
  })
})

describe('Paper view roots stay legible in Legacy mode', () => {
  it.each(VIEW_ROOTS)('$view $selector paints --paper wherever it sets --ink', ({ selector, source }) => {
    const rule = readRootRule(source, selector)

    // Guard the guard: if the ink declaration is ever dropped or renamed, the
    // substrate assertion below would otherwise pass vacuously.
    expect(rule).toMatch(PAPER_INK)
    expect(rule).toMatch(SUBSTRATE)
  })

  it.each(VIEW_ROOTS)(
    '$view $selector fallback ink clears WCAG AA on its fallback paper',
    ({ view, selector, source }) => {
      const rule = readRootRule(source, selector)
      const ink = rule.match(/color:\s*var\(--ink,\s*(#[0-9a-fA-F]{3,8})\s*\)/)?.[1]
      const paper = rule.match(SUBSTRATE)?.[1]
      expect(ink, `${view} ${selector} ink fallback`).toBeTruthy()
      expect(paper, `${view} ${selector} paper fallback`).toBeTruthy()

      expect(contrast(ink as string, paper as string)).toBeGreaterThanOrEqual(4.5)
    },
  )
})
