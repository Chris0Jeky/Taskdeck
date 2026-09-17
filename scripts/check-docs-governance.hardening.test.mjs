import test from 'node:test'
import assert from 'node:assert/strict'

import {
  collectControlPathMirrorErrors,
  parsePolicyControlPaths,
  parseRuleFrontMatterPaths,
} from './check-docs-governance.mjs'

function ruleWithPath(path, { quoted = false } = {}) {
  const scalar = quoted ? JSON.stringify(path) : path
  return `---\npaths:\n  - ${scalar}\n---\n`
}

const controlCodePoints = [
  ...Array.from({ length: 0x20 }, (_, index) => index),
  ...Array.from({ length: 0x21 }, (_, index) => 0x7f + index),
]

test('rejects every C0, DEL, and C1 control character in policy paths', () => {
  for (const codePoint of controlCodePoints) {
    const path = `ci/${String.fromCodePoint(codePoint)}**`
    const result = parsePolicyControlPaths(JSON.stringify({ controlPaths: [path] }))
    assert.ok(result.errors.length > 0, `accepted U+${codePoint.toString(16).padStart(4, '0')}`)
  }
})

test('rejects every decoded C0, DEL, and C1 control character in quoted YAML paths', () => {
  for (const codePoint of controlCodePoints) {
    const path = `ci/${String.fromCodePoint(codePoint)}**`
    const result = parseRuleFrontMatterPaths(ruleWithPath(path, { quoted: true }))
    assert.ok(result.errors.length > 0, `accepted U+${codePoint.toString(16).padStart(4, '0')}`)
  }
})

test('preserves ordinary visible Unicode in policy and YAML paths', () => {
  const path = 'docs/日本語/naïve-Δ/**'
  assert.deepEqual(parsePolicyControlPaths(JSON.stringify({ controlPaths: [path] })), {
    controlPaths: [path],
    errors: [],
  })
  assert.deepEqual(parseRuleFrontMatterPaths(ruleWithPath(path, { quoted: true })), {
    paths: [path],
    errors: [],
  })
})

const implicitNonStrings = [
  '~',
  'null',
  'NULL',
  'yes',
  'ON',
  'true',
  'False',
  '0',
  '-42',
  '+42',
  '01',
  '017',
  '1:20',
  '1_000',
  '0b1010',
  '0xF__F',
  '0xFF',
  '1.5',
  '.5',
  '5.',
  '1.0e+3',
  '1:20.5',
  '.inf',
  '+.Inf',
  '-.INF',
  '.nan',
  '.NaN',
  '2026-09-17',
  '2026-09-17T12:34:56Z',
  '2026-09-17T12:34:56.',
  '2026-09-17 12:34:56 +01:00',
]

const implicitStrings = [
  '0XFF',
  '+.nAn',
  '1e1_0',
  '0o17',
  '1e3',
  '-2E-4',
  '+.5',
  '.nAn',
  '09',
  '2026-09-17T12:34:56z',
]

test('rejects unquoted YAML implicit non-string path scalars', () => {
  for (const scalar of implicitNonStrings) {
    const result = parseRuleFrontMatterPaths(ruleWithPath(scalar))
    assert.ok(result.errors.length > 0, `accepted unquoted ${scalar}`)
  }
})

test('accepts quoted forms of YAML implicit scalars as path strings', () => {
  for (const scalar of implicitNonStrings) {
    const errors = collectControlPathMirrorErrors(
      JSON.stringify({ controlPaths: [scalar] }),
      ruleWithPath(scalar, { quoted: true }),
    )
    assert.deepEqual(errors, [], `rejected quoted ${scalar}: ${errors.join(' | ')}`)
  }
})

test('preserves resolver-string spellings and ordinary path-like strings', () => {
  for (const path of [
    ...implicitStrings,
    '2026-09-17-notes.md',
    '123/notes.md',
    'true/guide.md',
    '0xFF.md',
    '1e3/results.md',
    '.nan.md',
    'nullish',
  ]) {
    const errors = collectControlPathMirrorErrors(
      JSON.stringify({ controlPaths: [path] }),
      ruleWithPath(path),
    )
    assert.deepEqual(errors, [], `rejected path-like string ${path}: ${errors.join(' | ')}`)
  }
})
