import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { describe, expect, it } from 'vitest'

/**
 * Paper ink-ladder and status-foreground contrast (#1817).
 *
 * Sibling of `paperEmberContrast.spec.ts`, and the same shape: read the real
 * token values out of `paper-tokens.css` and measure, rather than trusting a
 * comment. It pins the per-token tuning decisions this issue's residual list
 * asked for:
 *
 *   - `--mute` and `--faint` are two rungs of one ink ladder. They were the
 *     SAME colour under light Paper (#6c6557) while `.paper-night` kept them a
 *     real step apart, so the light-mode ladder had lost a rung. The rung came
 *     back by darkening `--mute` (which only raises contrast) rather than
 *     lightening `--faint` (which had ~0.1 of AA headroom on `--paper-2`).
 *   - `--ink-2` is the new `--td-color-info` foreground under Paper: `info`
 *     used to collapse onto `--ember` alongside `error`.
 *   - `--ember-ink` on `--ember-tint` is the new `.td-alert--error` pairing;
 *     the previous `--ember` on `--ember-tint` measures 4.45:1, under AA.
 *
 * Text grounds are the three Paper surfaces body copy actually sits on
 * (`--paper`, `--paper-2`, `--paper-card`). `--paper-edge` is included only
 * for `--ink-2`, because `--td-color-info-light` maps to it.
 *
 * It lives in the Node-flavoured frontend-root `tests/` lane rather than
 * beside `src/tests/theme/paperEmberContrast.spec.ts` because it reads the
 * token sheet off disk: `tsconfig.vitest.json` type-checks `src/tests/**`
 * without node types, and its quarantine list may only shrink — so a new
 * `node:fs` spec cannot go there. That lane is not type-checked (#1607).
 */

const webRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
const tokensPath = resolve(webRoot, 'src/paper-tokens.css')
const css = readFileSync(tokensPath, 'utf8')

type Theme = '.paper' | '.paper-night'

function extractBlock(selector: Theme): string {
  const pattern = selector === '.paper'
    ? /\.paper\s*\{([\s\S]*?)\n\}/
    : /\.paper-night\s*\{([\s\S]*?)\n\}/
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

const THEMES: Theme[] = ['.paper', '.paper-night']
const TEXT_GROUNDS = ['--paper', '--paper-2', '--paper-card'] as const

describe.each(THEMES)('Paper ink ladder — %s', (theme) => {
  const block = extractBlock(theme)
  const mute = readToken(block, '--mute')
  const faint = readToken(block, '--faint')
  const ground = readToken(block, '--paper')

  it('keeps --mute and --faint as two distinct rungs', () => {
    expect(mute).not.toBe(faint)
  })

  it('puts --mute above --faint on the ladder', () => {
    // Higher contrast against the page = the stronger rung, in both themes.
    expect(contrast(mute, ground)).toBeGreaterThan(contrast(faint, ground))
  })

  it.each(TEXT_GROUNDS)('--mute clears 4.5:1 on %s', (surface) => {
    expect(contrast(mute, readToken(block, surface))).toBeGreaterThanOrEqual(4.5)
  })

  it.each(TEXT_GROUNDS)('--faint clears 4.5:1 on %s', (surface) => {
    expect(contrast(faint, readToken(block, surface))).toBeGreaterThanOrEqual(4.5)
  })
})

describe.each(THEMES)('Paper status foregrounds — %s', (theme) => {
  const block = extractBlock(theme)

  it.each([...TEXT_GROUNDS, '--paper-edge'] as const)(
    '--ink-2 (the info foreground) clears 4.5:1 on %s',
    (surface) => {
      expect(contrast(readToken(block, '--ink-2'), readToken(block, surface)))
        .toBeGreaterThanOrEqual(4.5)
    },
  )

  it('--ember-ink clears 4.5:1 on --ember-tint (the .td-alert--error pairing)', () => {
    expect(contrast(readToken(block, '--ember-ink'), readToken(block, '--ember-tint')))
      .toBeGreaterThanOrEqual(4.5)
  })
})
