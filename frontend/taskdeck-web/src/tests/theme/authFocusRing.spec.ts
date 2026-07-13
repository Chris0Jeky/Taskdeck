import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

/**
 * Legacy focus-ring regression (finding M5).
 *
 * The auth inputs previously used `box-shadow: 0 0 0 2px var(--ember-bloom, var(--td-focus-ring))`.
 * In Legacy `--ember-bloom` is undefined and `--td-focus-ring` is a full multi-shadow
 * value, so substituting it into the color slot invalidated the whole declaration and
 * (combined with `outline: none`) dropped the keyboard focus indicator entirely.
 *
 * The fix falls back at the whole-property level (`box-shadow: var(--td-focus-ring)`)
 * and scopes the ember-bloom ring to Paper only. This test guards the CSS source of
 * both auth views so the regression cannot silently return.
 */

const views = ['LoginView.vue', 'RegisterView.vue'] as const

describe.each(views)('auth focus ring — %s', (view) => {
  // Resolve from the project root (cwd) — vitest's import.meta.url is root-relative here.
  const source = readFileSync(resolve(process.cwd(), 'src/views', view), 'utf8')

  it('does not nest --td-focus-ring inside a color slot', () => {
    expect(source).not.toContain('var(--ember-bloom, var(--td-focus-ring))')
  })

  it('uses --td-focus-ring as a whole-property fallback for Legacy', () => {
    expect(source).toContain('box-shadow: var(--td-focus-ring);')
  })

  it('scopes the ember-bloom ring to Paper so Legacy keeps its full ring', () => {
    expect(source).toMatch(/\.paper\s+\.td-input:focus/)
    expect(source).toContain('box-shadow: 0 0 0 2px var(--ember-bloom);')
  })
})
