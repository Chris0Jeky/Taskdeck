import { readdirSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { describe, expect, it } from 'vitest'

/**
 * Guards issue #1814.
 *
 * `--td-surface-{lowest,sunken,elevated,low,high}` were consumed by components
 * but declared nowhere. `--td-surface-elevated` carried no fallback either, so
 * `background: var(--td-surface-elevated)` was invalid-at-computed-value-time
 * and painted TRANSPARENT in Legacy mode — visibly, on the login and MFA
 * secondary buttons' hover state.
 *
 * This spec pins three things:
 *   1. all five are declared in the `:root` block of `design-tokens.css`;
 *   2. every consumer's hex fallback agrees with what `:root` resolves to
 *      (they used to disagree: #0e0e0e vs #f9f9f9);
 *   3. `paper-legacy-bridge.css` still restates all five, so the `:root`
 *      additions cannot leak Obsidian into Paper.
 *
 * It lives in the Node-flavoured frontend-root `tests/` lane rather than
 * `src/tests/` because it reads source files off disk: `tsconfig.vitest.json`
 * type-checks `src/tests/**` without node types (deliberately — see its
 * comment), and `?raw` CSS imports resolve to `''` under `test.css: false`.
 * That lane is not type-checked; tracked in #1607.
 */

// `fileURLToPath` takes the URL string, not a `URL` object: under the happy-dom
// environment the global `URL` is not the one node's helper accepts.
const webRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
const srcDir = resolve(webRoot, 'src')

const designTokens = readFileSync(resolve(srcDir, 'design-tokens.css'), 'utf8')
const paperBridge = readFileSync(resolve(srcDir, 'paper-legacy-bridge.css'), 'utf8')

/** The `:root { ... }` block only — `[data-theme]` / `[data-density]` excluded. */
const rootBlock = /:root\s*\{([\s\S]*?)\n\}/.exec(designTokens)?.[1] ?? ''

/** Intended tier for each depth alias, and the Obsidian hex it must resolve to. */
const DEPTH_ALIASES = {
  lowest: { tier: 'container-lowest', hex: '#0e0e0e' },
  sunken: { tier: 'container-lowest', hex: '#0e0e0e' },
  low: { tier: 'container-low', hex: '#1c1b1b' },
  elevated: { tier: 'container-high', hex: '#2a2a2a' },
  high: { tier: 'container-high', hex: '#2a2a2a' },
}

const NAMES = Object.keys(DEPTH_ALIASES)

function declaredValue(block, property) {
  const match = new RegExp(`--td-surface-${property}\\s*:\\s*([^;]+);`).exec(block)
  return match?.[1].trim()
}

function collectSourceFiles() {
  return readdirSync(srcDir, { recursive: true, encoding: 'utf8' })
    .map((entry) => entry.replace(/\\/g, '/'))
    .filter((entry) => /\.(vue|css|ts)$/.test(entry))
    .filter((entry) => !entry.startsWith('tests/'))
}

describe('legacy surface depth tokens (#1814)', () => {
  it('declares all five depth aliases at :root, on the Obsidian ladder', () => {
    expect(rootBlock).not.toBe('')

    for (const name of NAMES) {
      const { tier, hex } = DEPTH_ALIASES[name]

      // Declared, and as an alias rather than a literal, so the sibling
      // [data-theme] / [data-density] overrides of the ladder keep working.
      expect(declaredValue(rootBlock, name)).toBe(`var(--td-surface-${tier})`)

      // ...and the tier it points at is the Obsidian value the call sites assume.
      expect(declaredValue(rootBlock, tier)).toBe(hex)
    }
  })

  it('has no consumer whose hex fallback disagrees with the :root value', () => {
    // Matches `var(--td-surface-<name>)` and `var(--td-surface-<name>, <fallback>)`.
    // The `(?![\w-])` boundary keeps `--td-surface-container-low(est)` out.
    const consumer = new RegExp(
      `var\\(\\s*--td-surface-(${NAMES.join('|')})(?![\\w-])\\s*(?:,\\s*([^)]*?)\\s*)?\\)`,
      'g',
    )

    const disagreements = []
    let consumerCount = 0

    for (const file of collectSourceFiles()) {
      const source = readFileSync(resolve(srcDir, file), 'utf8')
      for (const [, rawName, fallback] of source.matchAll(consumer)) {
        consumerCount += 1
        const expected = DEPTH_ALIASES[rawName].hex
        if (fallback !== undefined && fallback.toLowerCase() !== expected) {
          disagreements.push(
            `src/${file}: --td-surface-${rawName} falls back to ${fallback}, want ${expected}`,
          )
        }
      }
    }

    expect(disagreements).toEqual([])
    // Sanity: the scan actually found the consumers it is meant to police.
    expect(consumerCount).toBeGreaterThanOrEqual(9)
  })

  it('keeps the Paper bridge restating all five, so :root cannot leak into Paper', () => {
    // The :root aliases substitute to Obsidian hexes at :root and are only
    // INHERITED by <body>; Paper's own declaration on <body> is what wins.
    const paperScope = /\.paper,\s*\n\.paper-night\s*\{([\s\S]*?)\n\}/.exec(paperBridge)?.[1] ?? ''
    expect(paperScope).not.toBe('')

    for (const name of NAMES) {
      expect(declaredValue(paperScope, name)).toMatch(
        /^var\(--(paper|paper-2|paper-card|paper-edge|line|whisper)\)$/,
      )
    }
  })
})
