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
 *     the previous `--ember` on `--ember-tint` measures 4.46:1, under AA.
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

/**
 * Notification type badges (#1842).
 *
 * `typeBadgeClass` used to emit raw Tailwind palette utilities; the colour now
 * lives in `--td-notify-*-bg` / `--td-notify-*-fg`, re-tinted under Paper by
 * `paper-legacy-bridge.css`. The badge is a filled chip, so the type is carried
 * by the background — five distinct Paper tints — and the foreground is `--ink`
 * for all five rather than each tint's own hue, because `--applied` on
 * `--applied-tint` is sub-AA in light Paper.
 */
const BADGE_TINTS = ['--overdue-tint', '--ember-tint', '--applied-tint', '--paper-edge', '--paper-2'] as const

describe.each(THEMES)('Paper notification badges — %s', (theme) => {
  const block = extractBlock(theme)

  it('gives the five badges five distinct backgrounds', () => {
    const values = BADGE_TINTS.map((name) => readToken(block, name))
    expect(new Set(values).size).toBe(BADGE_TINTS.length)
  })

  it.each(BADGE_TINTS)('--ink (the badge foreground) clears 4.5:1 on %s', (surface) => {
    expect(contrast(readToken(block, '--ink'), readToken(block, surface)))
      .toBeGreaterThanOrEqual(4.5)
  })
})

/**
 * Exact contrast figures cited in permanent comments (#1842, item 4).
 *
 * The `>= 4.5` assertions above are a FLOOR: a comment could state any number
 * above it and the suite would stay green, which is how a stale or invented
 * figure survives in a file that looks tested. Per the verify-the-measurement
 * norm, every figure a permanent comment states is re-measured here with the
 * same WCAG formula and pinned to two decimal places, so moving a token forces
 * the comment to be corrected alongside it. Each row names the comment it
 * pins. (Figures re-measured on this branch; `--ember` on `--ember-tint` came
 * back 4.46:1, not the 4.45:1 three comments had recorded — corrected there.)
 */
const PINNED_FIGURES: ReadonlyArray<{
  theme: Theme
  fg: string
  bg: string
  expected: number
  citedBy: string
}> = [
  // src/paper-tokens.css — the `--mute` ink-ladder comment.
  { theme: '.paper', fg: '--mute', bg: '--paper', expected: 5.73, citedBy: 'paper-tokens.css --mute comment' },
  { theme: '.paper', fg: '--mute', bg: '--paper-2', expected: 5.28, citedBy: 'paper-tokens.css --mute comment' },
  { theme: '.paper', fg: '--mute', bg: '--paper-edge', expected: 4.77, citedBy: 'paper-tokens.css --mute comment' },
  { theme: '.paper', fg: '--mute', bg: '--paper-card', expected: 6.19, citedBy: 'paper-tokens.css --mute comment' },
  { theme: '.paper', fg: '--faint', bg: '--paper-2', expected: 4.60, citedBy: 'paper-tokens.css --faint headroom claim' },
  // src/paper-legacy-bridge.css — the `.td-alert--error` disposition comment.
  { theme: '.paper', fg: '--ember-ink', bg: '--ember-tint', expected: 7.83, citedBy: 'paper-legacy-bridge.css .td-alert--error comment' },
  { theme: '.paper-night', fg: '--ember-ink', bg: '--ember-tint', expected: 9.53, citedBy: 'paper-legacy-bridge.css .td-alert--error comment' },
  { theme: '.paper', fg: '--ember', bg: '--ember-tint', expected: 4.46, citedBy: 'paper-legacy-bridge.css — the sub-AA pairing it rejects' },
  // src/paper-legacy-bridge.css — the Tailwind `obsidian` mapping comment.
  { theme: '.paper', fg: '--paper', bg: '--ember', expected: 5.25, citedBy: 'paper-legacy-bridge.css --td-tw-obsidian comment' },
  // src/paper-legacy-bridge.css — the badge foreground comment.
  { theme: '.paper', fg: '--applied', bg: '--applied-tint', expected: 4.46, citedBy: 'paper-legacy-bridge.css badge comment — why the fg is --ink' },
]

/** Contrast rounded the way the comments state it: two decimal places. */
function pinnedContrast(theme: Theme, fg: string, bg: string): number {
  const block = extractBlock(theme)
  return Math.round(contrast(readToken(block, fg), readToken(block, bg)) * 100) / 100
}

describe('contrast figures stated in permanent comments are exactly what is asserted', () => {
  it.each(PINNED_FIGURES)(
    '$theme: $fg on $bg is $expected:1 (cited by $citedBy)',
    ({ theme, fg, bg, expected }) => {
      expect(pinnedContrast(theme, fg, bg)).toBe(expected)
    },
  )

  it('puts --mute and --faint exactly 1.15:1 apart in light Paper', () => {
    // paper-tokens.css: "#635c4e puts the two rungs 1.15:1 apart".
    expect(pinnedContrast('.paper', '--mute', '--faint')).toBe(1.15)
  })

  it('keeps every badge foreground/background pair at 12.6:1 or better', () => {
    // paper-legacy-bridge.css badge comment: "--ink clears 12.6:1 or better on
    // every one of the five tints in BOTH themes".
    for (const theme of THEMES) {
      for (const tint of BADGE_TINTS) {
        expect(pinnedContrast(theme, '--ink', tint), `${theme} --ink on ${tint}`)
          .toBeGreaterThanOrEqual(12.6)
      }
    }
  })
})
