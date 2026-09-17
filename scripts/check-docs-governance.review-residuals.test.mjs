import test from 'node:test'
import assert from 'node:assert/strict'

import {
  collectControlPathMirrorErrors,
  parseRuleFrontMatterPaths,
} from './check-docs-governance.mjs'

function quotedRule(path, extraFrontMatterLines = []) {
  return [
    '---',
    ...extraFrontMatterLines,
    'paths:',
    `  - ${JSON.stringify(path)}`,
    '---',
    '',
  ].join('\n')
}

for (const [name, nonPrintable] of [
  ['U+FFFE', '\uFFFE'],
  ['U+FFFF', '\uFFFF'],
]) {
  test(`rejects ${name} when policy and quoted rule paths match`, () => {
    const path = `ci/${nonPrintable}/**`
    const errors = collectControlPathMirrorErrors(
      JSON.stringify({ controlPaths: [path] }),
      quotedRule(path),
    )

    assert.ok(errors.length > 0, `${name} must not be certified as a usable path glob`)
    assert.ok(
      errors.some((error) => /control|unicode|parse/i.test(error)),
      errors.join(' | '),
    )
  })

  test(`rejects decoded ${name} in a quoted rule path`, () => {
    const path = `ci/${nonPrintable}/**`
    const result = parseRuleFrontMatterPaths(quotedRule(path))

    assert.deepEqual(result.paths, [])
    assert.ok(result.errors.length > 0, `${name} must fail rule-frontmatter parsing`)
  })
}

for (const [name, nonPrintable] of [
  ['NUL', '\u0000'],
  ['C1 NEXT LINE', '\u0085'],
  ['U+FFFE', '\uFFFE'],
  ['U+FFFF', '\uFFFF'],
]) {
  test(`rejects ${name} in a comment-only frontmatter line`, () => {
    const errors = collectControlPathMirrorErrors(
      JSON.stringify({ controlPaths: ['ci/**'] }),
      quotedRule('ci/**', [`# note${nonPrintable}`]),
    )

    assert.ok(errors.length > 0, 'comment-only lines must be validated before they are skipped')
    assert.ok(
      errors.some((error) => /control|unicode|parse/i.test(error)),
      errors.join(' | '),
    )
  })
}
