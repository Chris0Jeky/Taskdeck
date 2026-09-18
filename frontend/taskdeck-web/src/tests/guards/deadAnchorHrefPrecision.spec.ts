import { describe, expect, it } from 'vitest'
import { baseParse, NodeTypes, parserOptions } from '@vue/compiler-dom'
import deadAnchorGuardSource from './deadAnchors.spec.ts?raw'

type PlaceholderHrefDetector = (tag: string) => boolean

/**
 * Exercise the private detector from the repo-wide guard without exporting
 * test-only internals into production code. The extracted slice contains the
 * detector's constants, Vue-decoding dependency, and helper only; importing the
 * spec itself would register its full suite a second time.
 */
function loadPlaceholderHrefDetector(): PlaceholderHrefDetector {
  const start = deadAnchorGuardSource.indexOf('const HREF_ATTR =')
  const end = deadAnchorGuardSource.indexOf('/** Keep only actual Vue event attributes')

  expect(start).toBeGreaterThanOrEqual(0)
  expect(end).toBeGreaterThan(start)

  const executable = deadAnchorGuardSource
    .slice(start, end)
    .replace('function markupOnly(source: string): string', 'function markupOnly(source)')
    .replace(
      'function hasPlaceholderHref(tag: string): boolean',
      'function hasPlaceholderHref(tag)',
    )

  return new Function(
    'baseParse',
    'NodeTypes',
    'parserOptions',
    `${executable}\nreturn hasPlaceholderHref`,
  )(baseParse, NodeTypes, parserOptions) as PlaceholderHrefDetector
}

describe('dead-anchor href parser precision (#1949)', () => {
  const hasPlaceholderHref = loadPlaceholderHrefDetector()

  it('decodes bound href entities exactly once before inspecting the expression', () => {
    expect(hasPlaceholderHref(`<a :href="'java&#115;cript:void(0)'">`)).toBe(true)
    expect(hasPlaceholderHref(`<a :href="'java&#x73;cript:void(0)'">`)).toBe(true)
    expect(hasPlaceholderHref(`<a v-bind:href="'javascript&colon;void(0)'">`)).toBe(true)

    // Vue decodes the attribute once. The resulting JS string still contains
    // the literal entity text, and dynamic property assignment does not decode
    // it a second time.
    expect(hasPlaceholderHref(`<a :href="'java&amp;#115;cript:void(0)'">`)).toBe(false)
  })

  it('does not treat a quoted hash inside a real static URL as a placeholder', () => {
    expect(hasPlaceholderHref('<a href="/search?q=&quot;#&quot;">')).toBe(false)
    expect(hasPlaceholderHref('<a href="&#35;">')).toBe(true)
    expect(hasPlaceholderHref('<a href="#details">')).toBe(false)
  })
})
