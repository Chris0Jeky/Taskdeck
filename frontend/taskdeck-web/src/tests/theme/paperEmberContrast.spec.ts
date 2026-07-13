import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

/**
 * Contrast regression (finding M4).
 *
 * Primary CTAs render `--td-on-ember` text on the `--ember` background. Default
 * Paper only applies the `.paper` body class (root `--td-text-inverse: #131313`),
 * which put the old inverse token at 3.06:1 on Paper ember. This test reads the
 * real token values from paper-tokens.css and asserts the on-ember text clears
 * WCAG AA (4.5:1) at rest AND under the `filter: brightness(1.1)` hover, for both
 * Paper light (`.paper`) and Paper night (`.paper-night`).
 */

// vitest runs from the frontend/taskdeck-web project root; resolve the token source
// from cwd rather than import.meta.url (which is project-root-relative under vitest).
const tokensPath = resolve(process.cwd(), 'src/paper-tokens.css')
const css = readFileSync(tokensPath, 'utf8')

function extractBlock(selector: '.paper' | '.paper-night'): string {
  // Non-greedy up to the first closing brace; these token blocks contain no nested braces.
  const pattern = selector === '.paper'
    ? /\.paper\s*\{([\s\S]*?)\}/
    : /\.paper-night\s*\{([\s\S]*?)\}/
  const match = css.match(pattern)
  if (!match) throw new Error(`Could not locate ${selector} block in paper-tokens.css`)
  return match[1]
}

function readToken(block: string, name: string): string {
  const match = block.match(new RegExp(`${name}:\\s*(#[0-9a-fA-F]{3,8})`))
  if (!match) throw new Error(`Could not find token ${name}`)
  return match[1]
}

function hexToRgb(hex: string): [number, number, number] {
  let h = hex.replace('#', '')
  if (h.length === 3) h = h.split('').map((c) => c + c).join('')
  if (h.length === 8) h = h.slice(0, 6)
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

function brighten(hex: string, factor: number): string {
  const rgb = hexToRgb(hex).map((c) => Math.min(255, Math.round(c * factor)))
  return '#' + rgb.map((c) => c.toString(16).padStart(2, '0')).join('')
}

describe.each([
  ['.paper', '--ember', '--td-on-ember'] as const,
  ['.paper-night', '--ember', '--td-on-ember'] as const,
])('Paper on-ember CTA contrast — %s', (selector, emberToken, onEmberToken) => {
  const block = extractBlock(selector)
  const ember = readToken(block, emberToken)
  const onEmber = readToken(block, onEmberToken)

  it('clears 4.5:1 on the ember background at rest', () => {
    expect(contrast(onEmber, ember)).toBeGreaterThanOrEqual(4.5)
  })

  it('clears 4.5:1 under the brightness(1.1) hover', () => {
    expect(contrast(onEmber, brighten(ember, 1.1))).toBeGreaterThanOrEqual(4.5)
  })
})
