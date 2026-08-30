import { describe, expect, it } from 'vitest'
import { SUPPORTED_LOCALES, DEFAULT_LOCALE, type SupportedLocale } from '../../i18n'
import en from '../../locales/en'
import itCatalog from '../../locales/it'
import esCatalog from '../../locales/es'

// Imported directly from `src/locales/*`, NOT from the i18n runtime: since
// #1858 only `en` is registered eagerly there (it/es are code-split), and this
// guard is about the catalogs' source-of-truth parity, not the runtime.
// (`itCatalog` because a bare `it` would shadow vitest's `it`.)
const messages = { en, it: itCatalog, es: esCatalog }

/**
 * Catalog guard (ADR-0054 §6).
 *
 * Missing-key warnings are OFF at runtime on purpose — a partially translated
 * surface is the expected state during a surface-by-surface rollout, and `en`
 * fills the gap silently. That makes a GENUINELY missing key invisible in the
 * browser, so it has to be caught here instead: this spec is the only thing
 * standing between "someone added an English string and forgot the other two
 * catalogs" and a Spanish user finding it in production.
 *
 * It walks whatever is in `src/locales/*` — there is no per-surface
 * registration list to keep in sync. Extract a new surface and it is covered.
 *
 * What it does NOT check: whether the Italian and Spanish are any good. Tone
 * and correctness are a review responsibility (ADR-0054 §3); this guard only
 * proves the three catalogs are structurally parallel.
 */

type Flat = Map<string, string>

const TRANSLATED_LOCALES = SUPPORTED_LOCALES.filter(
  (locale): locale is SupportedLocale => locale !== DEFAULT_LOCALE,
)

/** Flatten a nested catalog into dotted key → string. Throws on a non-string leaf. */
function flatten(node: unknown, prefix = '', out: Flat = new Map()): Flat {
  if (typeof node === 'string') {
    out.set(prefix, node)
    return out
  }
  if (node !== null && typeof node === 'object' && !Array.isArray(node)) {
    for (const [key, value] of Object.entries(node as Record<string, unknown>)) {
      flatten(value, prefix ? `${prefix}.${key}` : key, out)
    }
    return out
  }
  throw new Error(
    `Catalog leaf at "${prefix || '<root>'}" is ${Array.isArray(node) ? 'an array' : typeof node}; ` +
      'message catalogs must contain only nested objects and strings.',
  )
}

/**
 * The set of `{placeholder}` names in a message. Order is intentionally NOT
 * compared: word order legitimately differs between languages, membership does
 * not — dropping `{count}` in Italian silently renders a sentence with a hole.
 */
function placeholders(message: string): string[] {
  return [...message.matchAll(/\{\s*([A-Za-z0-9_]+)\s*\}/g)].map((match) => match[1]).sort()
}

/** vue-i18n pipe-separated plural forms. One segment = not a plural message. */
function pluralSegments(message: string): number {
  return message.split('|').length
}

const flattened = new Map<SupportedLocale, Flat>(
  SUPPORTED_LOCALES.map((locale) => [locale, flatten(messages[locale])]),
)

const source = flattened.get(DEFAULT_LOCALE)!

describe('message catalogs', () => {
  it('has a non-empty source catalog to compare against', () => {
    // Guards the guard: every check below iterates the `en` key set, so an
    // accidentally empty source would make this whole file vacuously green.
    expect(source.size).toBeGreaterThan(0)
  })

  it.each(SUPPORTED_LOCALES.map((locale) => [locale]))(
    '%s: contains only nested objects and non-empty strings',
    (locale) => {
      const catalog = flattened.get(locale as SupportedLocale)!
      const empty = [...catalog.entries()]
        .filter(([, value]) => value.trim() === '')
        .map(([key]) => key)
      expect(empty, `empty or whitespace-only messages in "${locale}"`).toEqual([])
    },
  )

  it.each(TRANSLATED_LOCALES.map((locale) => [locale]))(
    '%s: covers every key in the source catalog',
    (locale) => {
      const catalog = flattened.get(locale as SupportedLocale)!
      const missing = [...source.keys()].filter((key) => !catalog.has(key))
      expect(
        missing,
        `keys present in "${DEFAULT_LOCALE}" but missing from "${locale}"`,
      ).toEqual([])
    },
  )

  it.each(TRANSLATED_LOCALES.map((locale) => [locale]))(
    '%s: has no keys the source catalog lacks',
    (locale) => {
      const catalog = flattened.get(locale as SupportedLocale)!
      const extra = [...catalog.keys()].filter((key) => !source.has(key))
      expect(
        extra,
        `stale keys in "${locale}" — renamed or removed from "${DEFAULT_LOCALE}"`,
      ).toEqual([])
    },
  )

  it.each(TRANSLATED_LOCALES.map((locale) => [locale]))(
    '%s: interpolation placeholders match the source for every key',
    (locale) => {
      const catalog = flattened.get(locale as SupportedLocale)!
      const mismatched: string[] = []
      for (const [key, sourceMessage] of source) {
        const translated = catalog.get(key)
        if (translated === undefined) continue // reported by the coverage test
        const expected = placeholders(sourceMessage)
        const actual = placeholders(translated)
        if (expected.join(',') !== actual.join(',')) {
          mismatched.push(`${key}: expected {${expected.join('}, {')}} but got {${actual.join('}, {')}}`)
        }
      }
      expect(mismatched, `placeholder mismatches in "${locale}"`).toEqual([])
    },
  )

  it.each(TRANSLATED_LOCALES.map((locale) => [locale]))(
    '%s: plural messages have the same number of forms as the source',
    (locale) => {
      // en/it/es all use the CLDR one/other cardinal system, so segment parity
      // is the right rule for these three. A locale with a different system
      // (pl, ru, ar) needs a per-locale expectation here AND a `pluralRules`
      // entry in the i18n runtime — relax this then, not before (ADR-0054 §4).
      const catalog = flattened.get(locale as SupportedLocale)!
      const mismatched: string[] = []
      for (const [key, sourceMessage] of source) {
        const translated = catalog.get(key)
        if (translated === undefined) continue
        const expected = pluralSegments(sourceMessage)
        const actual = pluralSegments(translated)
        if (expected !== actual) {
          mismatched.push(`${key}: expected ${expected} plural form(s), got ${actual}`)
        }
      }
      expect(mismatched, `plural-form mismatches in "${locale}"`).toEqual([])
    },
  )

  it.each(SUPPORTED_LOCALES.map((locale) => [locale]))(
    '%s: Inbox edit guidance names the current disposition controls',
    (locale) => {
      const catalog = flattened.get(locale as SupportedLocale)!
      const guidance = [...catalog.entries()].filter(([key]) =>
        key.startsWith('inbox.triage.edit.'),
      )
      const stale = guidance
        .filter(([, message]) => /\b(?:Accept|Reject)\b/.test(message))
        .map(([key]) => key)

      expect(stale, 'Inbox edit guidance still names retired controls').toEqual([])

      const guidanceText = guidance.map(([, message]) => message).join(' ')
      for (const action of ['Ask AI', 'Keep', 'Archive']) {
        expect(guidanceText, `Inbox edit guidance is missing "${action}"`).toContain(action)
      }
    },
  )
})

describe('catalog guard itself', () => {
  it('rejects a non-string, non-object leaf', () => {
    expect(() => flatten({ home: { count: 3 } })).toThrow(/must contain only nested objects and strings/)
  })

  it('rejects an array leaf', () => {
    expect(() => flatten({ home: { items: ['a'] } })).toThrow(/is an array/)
  })

  it('extracts placeholder names ignoring order and whitespace', () => {
    expect(placeholders('{ total } then {completed}')).toEqual(['completed', 'total'])
    expect(placeholders('no placeholders here')).toEqual([])
  })

  it('counts pipe-separated plural forms', () => {
    expect(pluralSegments('one thing')).toBe(1)
    expect(pluralSegments('one thing | many things')).toBe(2)
  })
})
