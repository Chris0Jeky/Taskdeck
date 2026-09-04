import { describe, expect, it } from 'vitest'
import tdDialogSource from '../../components/ui/TdDialog.vue?raw'
import paperBoardDialogShellSource from '../../views/paper/board/PaperBoardDialogShell.vue?raw'

/**
 * Source-text invariants for the two dialog sheets that bind themselves to the
 * visual viewport (issue #2180).
 *
 * Why source text rather than a mounted component: the invariant is a CSS
 * cascade property. `@supports (height: 100dvh)` is a feature query, and the
 * DOM environment vitest runs (happy-dom) evaluates neither feature queries nor
 * viewport units, so a mounted spec cannot tell a declaration inside the guard
 * from one outside it. Playwright can, but only on engines that actually lack
 * `dvh` — and the browsers this guards are precisely the ones no runner in the
 * matrix provides. Reading the stylesheet is the only check that can fail on
 * the regression. Precedent: `PaperBranding.spec.ts`.
 *
 * The defect these pin: `top` consumed the live offset unconditionally while
 * `height` was consumed only inside `@supports (height: 100dvh)`. A browser
 * with the VisualViewport API and no `dvh` was therefore pushed down by
 * `offsetTop` while staying a full layout viewport tall, overflowing the bottom
 * of the screen by exactly `offsetTop` and putting the footer actions under the
 * software keyboard.
 *
 * The shape each rule must keep, in this order:
 *   1. a plain viewport-unit floor, for browsers with no custom properties;
 *   2. the same declaration reading the custom property with a `100vh`
 *      fallback, OUTSIDE the feature query, so every browser that has custom
 *      properties follows the visual viewport;
 *   3. the same declaration inside `@supports (height: 100dvh)`, identical but
 *      for a `100dvh` fallback, which upgrades only the fallback.
 *
 * Step 2 must never use a `100dvh` fallback. `var()` is parse-valid in every
 * browser with custom properties, so the substitution would happen on a
 * `dvh`-less browser too and yield a value that is invalid at computed-value
 * time; per the CSS Variables spec the property then computes to its initial
 * value (`auto` / `none`) and the discarded floor does not resurface.
 *
 * The sources arrive through Vite's `?raw` query rather than `node:fs`. Same
 * effect, but it keeps this spec inside `tsconfig.vitest.json`'s type-checked
 * set: `types` there is deliberately `["vite/client", "vite-plugin-pwa/client"]`
 * without `"node"`, and `PaperBranding.spec.ts` sits in that config's
 * quarantine list solely because its `node:` imports do not type-check.
 */

const SUPPORTS_MARKER = '@supports (height: 100dvh)'

/** The single `<style>` block of a single-file component, comments stripped. */
function readStyleBlock(source: string, label: string): string {
  const match = /<style[^>]*>([\s\S]*)<\/style>/.exec(source.replace(/\r\n/g, '\n'))
  if (!match) throw new Error(`${label} has no <style> block`)
  // Strip CSS comments first: these rules are heavily commented, and a comment
  // that quotes a forbidden form must not read as a declaration.
  return match[1].replace(/\/\*[\s\S]*?\*\//g, '')
}

/**
 * Split the stylesheet into the text inside the `dvh` feature query and the
 * text outside it, matching braces so nested rule blocks are handled.
 */
function splitOnSupportsGuard(css: string, label: string): { inside: string; outside: string } {
  const start = css.indexOf(SUPPORTS_MARKER)
  if (start === -1) throw new Error(`${label} no longer contains "${SUPPORTS_MARKER}"`)

  const open = css.indexOf('{', start)
  if (open === -1) throw new Error(`${label} has "${SUPPORTS_MARKER}" with no block`)

  let depth = 0
  let close = -1
  for (let index = open; index < css.length; index += 1) {
    if (css[index] === '{') depth += 1
    else if (css[index] === '}') {
      depth -= 1
      if (depth === 0) {
        close = index
        break
      }
    }
  }
  if (close === -1) throw new Error(`${label} has an unbalanced "${SUPPORTS_MARKER}" block`)

  return {
    inside: css.slice(open + 1, close),
    outside: css.slice(0, start) + css.slice(close + 1),
  }
}

interface DialogTarget {
  label: string
  source: string
  property: string
  /**
   * Every declaration that must follow the visual viewport. `floor` is the
   * plain viewport-unit form that must precede the `var()` form; `guarded` is
   * the `100dvh`-fallback form that belongs inside the feature query.
   */
  declarations: Array<{ name: string; floor: RegExp; unguarded: RegExp; guarded: RegExp }>
}

const targets: DialogTarget[] = [
  {
    label: 'PaperBoardDialogShell.vue',
    source: paperBoardDialogShellSource,
    property: '--paper-board-dialog-visual-viewport-height',
    declarations: [
      {
        name: 'height',
        floor: /(?<!-)height:\s*100vh\s*;/,
        unguarded: /(?<!-)height:\s*var\(--paper-board-dialog-visual-viewport-height,\s*100vh\)\s*;/,
        guarded: /(?<!-)height:\s*var\(--paper-board-dialog-visual-viewport-height,\s*100dvh\)\s*;/,
      },
    ],
  },
  {
    label: 'TdDialog.vue',
    source: tdDialogSource,
    property: '--td-dialog-visual-viewport-height',
    declarations: [
      {
        name: 'height',
        floor: /(?<!-)height:\s*100vh\s*;/,
        unguarded: /(?<!-)height:\s*var\(--td-dialog-visual-viewport-height,\s*100vh\)\s*;/,
        guarded: /(?<!-)height:\s*var\(--td-dialog-visual-viewport-height,\s*100dvh\)\s*;/,
      },
      {
        name: 'max-height',
        floor: /max-height:\s*calc\(100vh\s*-\s*2\s*\*\s*var\(--td-space-8\)\)\s*;/,
        unguarded:
          /max-height:\s*calc\(var\(--td-dialog-visual-viewport-height,\s*100vh\)\s*-\s*2\s*\*\s*var\(--td-space-8\)\)\s*;/,
        guarded:
          /max-height:\s*calc\(var\(--td-dialog-visual-viewport-height,\s*100dvh\)\s*-\s*2\s*\*\s*var\(--td-space-8\)\)\s*;/,
      },
    ],
  },
]

describe.each(targets)('$label visual-viewport CSS', (target) => {
  const css = readStyleBlock(target.source, target.label)
  const { inside, outside } = splitOnSupportsGuard(css, target.label)

  it.each(target.declarations)(
    '$name reads the visual viewport outside the dvh feature query',
    (declaration) => {
      // Without this the browser class with a VisualViewport API and no `dvh`
      // takes `top: var(...-offset-top)` and keeps a full layout-viewport size.
      expect(outside).toMatch(declaration.unguarded)
    },
  )

  it.each(target.declarations)('$name keeps a plain floor ahead of the var() form', (declaration) => {
    const floorAt = outside.search(declaration.floor)
    const varAt = outside.search(declaration.unguarded)

    expect(floorAt).toBeGreaterThanOrEqual(0)
    expect(varAt).toBeGreaterThanOrEqual(0)
    // Source order is the whole mechanism: a browser without custom properties
    // drops the var() form at parse time and must still have the floor.
    expect(floorAt).toBeLessThan(varAt)
  })

  it.each(target.declarations)('$name upgrades only its fallback inside the guard', (declaration) => {
    expect(inside).toMatch(declaration.guarded)
  })

  it('never falls back to 100dvh outside the dvh feature query', () => {
    // A `100dvh` fallback is only safe where the feature query has already
    // proved `dvh` parses. Unguarded it is invalid at computed-value time on a
    // `dvh`-less browser, which computes to `auto` / `none` instead of
    // resurfacing the discarded floor.
    const unguardedDvhFallback = new RegExp(
      `var\\(\\s*${target.property}\\s*,\\s*100dvh\\s*\\)`,
    )
    expect(outside).not.toMatch(unguardedDvhFallback)
  })

  it('consumes the offset and the height in the same cascade layer', () => {
    // The asymmetry IS the bug: `top` was unconditional and `height` was not.
    expect(outside).toMatch(
      new RegExp(`top:\\s*var\\(${target.property.replace('-height', '-offset-top')},\\s*0px\\)`),
    )
    expect(outside).toMatch(new RegExp(`var\\(${target.property},\\s*100vh\\)`))
  })
})
