import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { describe, expect, it } from 'vitest'

/**
 * Guards the two invariants ADR-0053's safety argument rests on (#1817,
 * "Known limitations" 3). Both were true when the bridge landed, and both were
 * established by hand, once:
 *
 *   1. **Legacy is byte-identical.** `tailwind.config.js` emits every semantic
 *      colour as `var(--td-tw-<name>, <obsidian-hex>)` and nothing defines
 *      `--td-tw-*` at `:root`, so Legacy ("Paper off") resolves the fallback.
 *      Dropping or editing a fallback changes Legacy silently. The same holds
 *      for the `--td-*` palette declared at `:root` in `design-tokens.css`.
 *   2. **No Obsidian leaks into Paper.** Every `--td-tw-*` the config
 *      references must be defined in `paper-legacy-bridge.css`; a colour added
 *      to the config with no bridge definition falls back to its Obsidian hex
 *      *inside the Paper shell*.
 *
 * The pinned tables below are the ORIGINAL values, read out of git at
 * `0f6f9a5d^` — the commit before the bridge — not copied from the file under
 * test, so they are an independent source of truth rather than a restatement.
 *
 * This spec lives in the Node-flavoured frontend-root `tests/` lane, next to
 * `legacy-surface-depth-tokens.spec.ts`, because it reads `.css` and `.js`
 * sources off disk: `?raw` CSS imports resolve to `''` under `test.css: false`.
 * That lane is not type-checked; tracked in #1607.
 */

const webRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
const srcDir = resolve(webRoot, 'src')

const tailwindConfig = readFileSync(resolve(webRoot, 'tailwind.config.js'), 'utf8')
const designTokens = readFileSync(resolve(srcDir, 'design-tokens.css'), 'utf8')
const paperBridge = readFileSync(resolve(srcDir, 'paper-legacy-bridge.css'), 'utf8')

/**
 * The 51 Tailwind semantic colours as they stood at `0f6f9a5d^`. Legacy mode
 * must still compute exactly these.
 */
const ORIGINAL_TAILWIND: Record<string, string> = {
  'primary': '#ffb3ae',
  'primary-container': '#ff5352',
  'on-primary': '#68000b',
  'on-primary-container': '#5c0008',
  'on-primary-fixed': '#410004',
  'on-primary-fixed-variant': '#930014',
  'primary-fixed': '#ffdad7',
  'primary-fixed-dim': '#ffb3ae',
  'inverse-primary': '#ba1724',
  'secondary': '#c6c6cf',
  'secondary-container': '#45464e',
  'on-secondary': '#2f3037',
  'on-secondary-container': '#b4b4bd',
  'on-secondary-fixed': '#1a1b22',
  'on-secondary-fixed-variant': '#45464e',
  'secondary-fixed': '#e2e1eb',
  'secondary-fixed-dim': '#c6c6cf',
  'tertiary': '#c6c6c9',
  'tertiary-container': '#909193',
  'on-tertiary': '#2f3033',
  'on-tertiary-container': '#282a2c',
  'on-tertiary-fixed': '#1a1c1e',
  'on-tertiary-fixed-variant': '#454749',
  'tertiary-fixed': '#e2e2e5',
  'tertiary-fixed-dim': '#c6c6c9',
  'surface': '#131313',
  'surface-dim': '#131313',
  'surface-bright': '#3a3939',
  'surface-container-lowest': '#0e0e0e',
  'surface-container-low': '#1c1b1b',
  'surface-container': '#201f1f',
  'surface-container-high': '#2a2a2a',
  'surface-container-highest': '#353534',
  'surface-variant': '#353534',
  'surface-tint': '#ffb3ae',
  'on-surface': '#e5e2e1',
  'on-surface-variant': '#e4beba',
  'on-background': '#e5e2e1',
  'background': '#131313',
  'inverse-surface': '#e5e2e1',
  'inverse-on-surface': '#313030',
  'outline': '#ab8986',
  'outline-variant': '#5b403e',
  'error': '#ffb4ab',
  'error-container': '#93000a',
  'on-error': '#690005',
  'on-error-container': '#ffdad6',
  'ember': '#ff4d4d',
  'ember-glow': '#ff5352',
  'obsidian': '#131313',
  'argent': '#c7c6c4',
}

/**
 * The colour-valued `--td-*` declarations at `:root` in `design-tokens.css`, as
 * they stood at `0f6f9a5d^`. The bridge must never have moved any of them.
 * (Additions are allowed — #1814's depth aliases and #1817's notification
 * stripe tokens are additions — but a pinned value may not change.)
 */
const ORIGINAL_ROOT: Record<string, string> = {
  '--td-surface-base': '#131313',
  '--td-surface-container-lowest': '#0e0e0e',
  '--td-surface-container-low': '#1c1b1b',
  '--td-surface-container': '#201f1f',
  '--td-surface-container-high': '#2a2a2a',
  '--td-surface-container-highest': '#353534',
  '--td-surface-bright': '#3a3939',
  '--td-color-primary': '#ffb3ae',
  '--td-color-primary-hover': '#ff5352',
  '--td-color-primary-light': 'rgba(255, 83, 82, 0.15)',
  '--td-color-ember': '#ff4d4d',
  '--td-color-ember-glow': '#ff5352',
  '--td-color-ember-dim': 'rgba(255, 77, 77, 0.1)',
  '--td-color-success': '#4ade80',
  '--td-color-success-light': 'rgba(74, 222, 128, 0.15)',
  '--td-color-warning': '#fbbf24',
  '--td-color-warning-light': 'rgba(251, 191, 36, 0.15)',
  '--td-color-error': '#ff4d4d',
  '--td-color-error-light': 'rgba(255, 77, 77, 0.15)',
  '--td-color-info': '#ffb3ae',
  '--td-color-info-light': 'rgba(255, 179, 174, 0.15)',
  '--td-text-primary': '#e5e2e1',
  '--td-text-secondary': '#e4beba',
  '--td-text-tertiary': 'rgba(229, 226, 225, 0.4)',
  '--td-text-inverse': '#131313',
  '--td-text-ember': '#ff4d4d',
  '--td-text-muted': 'rgba(229, 226, 225, 0.6)',
  '--td-border-default': 'rgba(91, 64, 62, 0.15)',
  '--td-border-ghost': 'rgba(91, 64, 62, 0.1)',
  '--td-border-focus': '#ffb3ae',
  '--td-border-ember': '#ff5352',
  '--td-focus-ring': '0 0 0 2px #ff5352, 0 0 0 4px rgba(255, 83, 82, 0.2)',
  '--td-focus-ring-error': '0 0 0 2px #ff4d4d, 0 0 0 4px rgba(255, 77, 77, 0.2)',
  '--td-shadow-sm': '0 2px 8px rgba(0, 0, 0, 0.3)',
  '--td-shadow-md': '0 8px 24px rgba(0, 0, 0, 0.35)',
  '--td-shadow-lg': '0 20px 40px rgba(0, 0, 0, 0.4), 0 0 1px rgba(199, 198, 196, 0.1)',
  '--td-shadow-xl': '0 32px 64px rgba(0, 0, 0, 0.5)',
  '--td-glass-bg': 'rgba(32, 31, 31, 0.8)',
}

/** The `:root { ... }` block of design-tokens.css. */
const rootBlock = /:root\s*\{([\s\S]*?)\n\}/.exec(designTokens)?.[1] ?? ''

/** The `.paper, .paper-night { ... }` token block of the bridge. */
const paperScope = /\.paper,\s*\n\.paper-night\s*\{([\s\S]*?)\n\}/.exec(paperBridge)?.[1] ?? ''

/** Every `'<name>': '<value>'` colour entry in the Tailwind config. */
function tailwindColorEntries(): Array<[string, string]> {
  const colorsBlock = /colors:\s*\{([\s\S]*?)\n {6}\}/.exec(tailwindConfig)?.[1] ?? ''
  expect(colorsBlock, 'tailwind.config.js colors block').not.toBe('')
  return [...colorsBlock.matchAll(/'([a-z0-9-]+)':\s*'([^']+)'/g)].map(
    ([, name, value]) => [name, value] as [string, string],
  )
}

function declaredValue(block: string, property: string): string | undefined {
  return new RegExp(`${property}\\s*:\\s*([^;]+);`).exec(block)?.[1].trim()
}

/** CSS with `/* ... *\/` comments removed. */
function stripComments(css: string): string {
  return css.replace(/\/\*[\s\S]*?\*\//g, '')
}

/**
 * The only rules in the bridge allowed to select the root element. `<body>`
 * cannot set `color-scheme` for the document, so these two follow the body
 * skin through `:has()`. They are exempt from the "scoped under .paper" rule
 * BY NAME, and may declare nothing but `color-scheme`.
 */
const ROOT_COLOR_SCHEME_RULES = [
  { selector: ':root:has(> body.paper)', value: 'light' },
  { selector: ':root:has(> body.paper-night)', value: 'dark' },
] as const

describe('ADR-0053 bridge invariant 1 — Legacy is byte-identical', () => {
  it('emits every Tailwind semantic colour as var(--td-tw-<name>, <original hex>)', () => {
    const entries = tailwindColorEntries()
    const seen = new Set<string>()
    const drift: string[] = []

    for (const [name, value] of entries) {
      seen.add(name)
      const expected = ORIGINAL_TAILWIND[name]
      if (expected === undefined) {
        drift.push(
          `${name}: colour is not in the pinned Obsidian table. A new semantic colour must be ` +
            'added to this table AND defined in paper-legacy-bridge.css, or it leaks Obsidian ' +
            'into the Paper shell.',
        )
        continue
      }
      if (value !== `var(--td-tw-${name}, ${expected})`) {
        drift.push(`${name}: emits "${value}", want "var(--td-tw-${name}, ${expected})"`)
      }
    }

    for (const name of Object.keys(ORIGINAL_TAILWIND)) {
      if (!seen.has(name)) drift.push(`${name}: dropped from tailwind.config.js`)
    }

    expect(drift).toEqual([])
    expect(entries.length).toBe(Object.keys(ORIGINAL_TAILWIND).length)
  })

  it('never defines --td-tw-* at :root, so the fallbacks are what Legacy resolves', () => {
    expect(rootBlock).not.toBe('')
    expect(rootBlock).not.toMatch(/--td-tw-/)
    // ...and nowhere else in the file either (a stray `html {}` block would do).
    expect(stripComments(designTokens)).not.toMatch(/--td-tw-/)
  })

  it('leaves every pinned :root Obsidian value exactly where it was', () => {
    const drift: string[] = []
    for (const [name, expected] of Object.entries(ORIGINAL_ROOT)) {
      const actual = declaredValue(rootBlock, name)
      if (actual !== expected) drift.push(`${name}: is "${actual}", want "${expected}"`)
    }
    expect(drift).toEqual([])
  })

  it('scopes every bridge rule under .paper / .paper-night, apart from two named :root color-scheme rules', () => {
    // The old assertion was named "never :root" but accepted anything matching
    // `body.paper` ANYWHERE in the selector, which the two `:root:has(> body.paper*)`
    // rules do inside their `:has()` — so it never tested the claim its name
    // made (#1842). The two rules are deliberate: `color-scheme` on the root
    // element cannot be reached from `<body>`. They are exempted BY NAME, and
    // held to carrying nothing but `color-scheme`, so a token declaration can
    // never ride in at `:root` behind the exemption.
    const rules = [...stripComments(paperBridge).matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(
      ([, selector, body]) => ({ selector: selector.trim().replace(/\s+/g, ' '), body: body.trim() }),
    )
    expect(rules.length).toBeGreaterThan(0)

    const seenExemptions: string[] = []
    for (const { selector, body } of rules) {
      const exemption = ROOT_COLOR_SCHEME_RULES.find((r) => r.selector === selector)
      if (exemption) {
        seenExemptions.push(selector)
        expect(body, `exempt rule "${selector}" must declare only color-scheme`)
          .toBe(`color-scheme: ${exemption.value};`)
        continue
      }
      expect(selector, `bridge selector "${selector}"`).not.toMatch(/(^|[\s,>+~(])(:root|html)\b/)
      expect(selector, `bridge selector "${selector}"`).toMatch(/(^|[\s,(])(\.paper\b|\.paper-night\b)/)
    }

    // Both exemptions must actually be present, so deleting one is a failure
    // rather than a silently smaller allow-list.
    expect(seenExemptions).toEqual(ROOT_COLOR_SCHEME_RULES.map((r) => r.selector))
  })
})

describe('ADR-0053 bridge invariant 2 — no Obsidian leaks into Paper', () => {
  it('defines every --td-tw-* the Tailwind config references', () => {
    const referenced = tailwindColorEntries()
      .map(([, value]) => /var\((--td-tw-[a-z0-9-]+)/.exec(value)?.[1])
      .filter((name): name is string => Boolean(name))

    expect(referenced.length).toBe(Object.keys(ORIGINAL_TAILWIND).length)

    const undefined_ = referenced.filter((name) => declaredValue(paperScope, name) === undefined)
    expect(undefined_).toEqual([])
  })

  it('keeps every bridge value a var() reference, so the palette stays single-sourced', () => {
    const body = stripComments(paperBridge)
    expect(body).not.toMatch(/#([0-9a-fA-F]{3,4}|[0-9a-fA-F]{6,8})\b/)
    expect(body).not.toMatch(/\brgba?\(/)
  })

  it('agrees with :root on the depth ladder — elevated and high share one tier', () => {
    // design-tokens.css aliases both onto container-high; the bridge used to
    // send `elevated` to the card tier, which also inverted the hover
    // direction on a light substrate (#1817).
    expect(declaredValue(rootBlock, '--td-surface-elevated')).toBe(
      declaredValue(rootBlock, '--td-surface-high'),
    )
    expect(declaredValue(paperScope, '--td-surface-elevated')).toBe(
      declaredValue(paperScope, '--td-surface-high'),
    )
  })

  it('gives info a foreground distinct from error', () => {
    // Obsidian distinguished them (#ffb3ae vs #ff4d4d); collapsing both onto
    // --ember made every info banner read as an error (#1817).
    expect(declaredValue(paperScope, '--td-color-info')).not.toBe(
      declaredValue(paperScope, '--td-color-error'),
    )
  })

  it('keeps the four *-light status tints consistently opaque', () => {
    // `--ember-bloom` and friends carry an alpha channel; the Paper tints do
    // not. Mixing the two is the inconsistency #1817 records.
    const translucent = /bloom|color-mix|rgba?\(|[0-9a-fA-F]{8}\b/
    for (const name of ['success', 'warning', 'error', 'info']) {
      const value = declaredValue(paperScope, `--td-color-${name}-light`)
      expect(value, `--td-color-${name}-light`).toBeTruthy()
      expect(value, `--td-color-${name}-light`).not.toMatch(translucent)
    }
  })

  it('gives lg and xl distinct elevations above md', () => {
    const md = declaredValue(paperScope, '--td-shadow-md')
    const lg = declaredValue(paperScope, '--td-shadow-lg')
    const xl = declaredValue(paperScope, '--td-shadow-xl')
    expect(new Set([md, lg, xl]).size).toBe(3)
  })

  it('re-tints all five notification stripes, with :root holding the Legacy hues', () => {
    const stripes = [
      '--td-notify-proposal',
      '--td-notify-mention',
      '--td-notify-board-change',
      '--td-notify-assignment',
      '--td-notify-system',
    ]
    const paperValues = stripes.map((name) => declaredValue(paperScope, name))
    for (const [i, value] of paperValues.entries()) {
      expect(value, `${stripes[i]} under Paper`).toMatch(/^var\(--[a-z0-9-]+\)$/)
      expect(declaredValue(rootBlock, stripes[i]), `${stripes[i]} at :root`).toBeTruthy()
    }
    // Five types, five visually separable stripes.
    expect(new Set(paperValues).size).toBe(stripes.length)
  })

  it('re-tints all five notification badges, with :root holding the Legacy hues', () => {
    // The stripes' companion (#1842): `typeBadgeClass` used to emit raw
    // Tailwind palette utilities, which do not follow the active skin.
    const types = ['proposal', 'mention', 'board-change', 'assignment', 'system']
    const backgrounds = types.map((t) => declaredValue(paperScope, `--td-notify-${t}-bg`))

    for (const t of types) {
      for (const part of ['bg', 'fg']) {
        const name = `--td-notify-${t}-${part}`
        expect(declaredValue(paperScope, name), `${name} under Paper`).toMatch(/^var\(--[a-z0-9-]+\)$/)
        expect(declaredValue(rootBlock, name), `${name} at :root`).toBeTruthy()
      }
      // The badge is a filled chip, so the type is carried by the background.
      expect(declaredValue(paperScope, `--td-notify-${t}-fg`), `${t} badge foreground`)
        .toBe('var(--ink)')
    }

    // Five types, five separable fills.
    expect(new Set(backgrounds).size).toBe(types.length)
  })
})

describe('the dead [data-theme] light theme is gone', () => {
  it('leaves no data-theme selector or reference in the token sheet', () => {
    expect(designTokens).not.toMatch(/data-theme/)
  })

  it('keeps the live [data-density] siblings', () => {
    expect(designTokens).toMatch(/\[data-density="compact"\]/)
  })
})
