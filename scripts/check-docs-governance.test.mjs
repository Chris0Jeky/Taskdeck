import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

import {
  CI_CONTROL_RULE_PATH,
  CI_POLICY_PATH,
  collectControlPathMirrorErrors,
  parsePolicyControlPaths,
  parseRuleFrontMatterPaths,
} from './check-docs-governance.mjs'

const policyFixture = JSON.stringify({
  policyId: 'fixture',
  controlPaths: ['ci/**', 'scripts/ci/**', '.github/workflows/**', 'global.json'],
})

function ruleFixture(paths, body = '\n# CI-control region\n\nDoctrine.\n') {
  const entries = paths.map((path) => `  - "${path}"`).join('\n')
  return `---\npaths:\n${entries}\n---\n${body}`
}

const mirroredPaths = ['ci/**', 'scripts/ci/**', '.github/workflows/**', 'global.json']

test('passes when the rule front matter mirrors every control path', () => {
  assert.deepEqual(collectControlPathMirrorErrors(policyFixture, ruleFixture(mirroredPaths)), [])
})

test('passes when the front matter carries deliberate extras beyond policy', () => {
  const withExtras = ['.github/**', ...mirroredPaths, 'docs/ci/**']
  assert.deepEqual(collectControlPathMirrorErrors(policyFixture, ruleFixture(withExtras)), [])
})

test('is order-insensitive', () => {
  const shuffled = [...mirroredPaths].reverse()
  assert.deepEqual(collectControlPathMirrorErrors(policyFixture, ruleFixture(shuffled)), [])
})

test('fails and names the glob when one control path is missing from the front matter', () => {
  const drifted = mirroredPaths.filter((path) => path !== 'scripts/ci/**')
  const errors = collectControlPathMirrorErrors(policyFixture, ruleFixture(drifted))

  assert.equal(errors.length, 1)
  assert.match(errors[0], /is missing 1 control path\(s\)/)
  assert.match(errors[0], /scripts\/ci\/\*\*/)
  assert.match(errors[0], new RegExp(CI_CONTROL_RULE_PATH.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))
  assert.match(errors[0], new RegExp(CI_POLICY_PATH.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))
})

test('names every missing glob, not just the first', () => {
  const errors = collectControlPathMirrorErrors(policyFixture, ruleFixture(['ci/**']))

  assert.equal(errors.length, 1)
  assert.match(errors[0], /is missing 3 control path\(s\)/)
  for (const missing of ['scripts/ci/**', '.github/workflows/**', 'global.json']) {
    assert.ok(errors[0].includes(missing), `expected the message to name ${missing}`)
  }
})

test('a near-miss typo in the front matter still reports the real control path as missing', () => {
  const typo = ['ci/**', 'scripts/ci/*', '.github/workflows/**', 'global.json']
  const errors = collectControlPathMirrorErrors(policyFixture, ruleFixture(typo))

  assert.equal(errors.length, 1)
  assert.match(errors[0], /scripts\/ci\/\*\*/)
})

test('fails closed when the rule file has no front matter at all', () => {
  const errors = collectControlPathMirrorErrors(policyFixture, '# CI-control region\n\nNo front matter.\n')

  assert.equal(errors.length, 1)
  assert.match(errors[0], /no parseable YAML front matter/)
})

test('fails closed when the front matter has no paths: key', () => {
  const errors = collectControlPathMirrorErrors(policyFixture, '---\ndescription: "rule"\n---\n\nBody.\n')

  assert.equal(errors.length, 1)
  assert.match(errors[0], /no paths: key/)
})

test('fails closed on a flow-sequence paths: value it cannot parse', () => {
  const errors = collectControlPathMirrorErrors(policyFixture, '---\npaths: ["ci/**"]\n---\n\nBody.\n')

  assert.equal(errors.length, 1)
  assert.match(errors[0], /must be a block sequence/)
})

test('fails closed on an unparseable list entry rather than dropping it', () => {
  const ruleText = '---\npaths:\n  - "ci/**"\n  nested: oops\n---\n\nBody.\n'
  const errors = collectControlPathMirrorErrors(policyFixture, ruleText)

  assert.ok(errors.some((error) => /cannot parse/.test(error)), errors.join(' | '))
})

test('fails closed on a mangled quote rather than mirroring a mangled glob', () => {
  const ruleText = '---\npaths:\n  - "ci/**\n---\n\nBody.\n'
  const errors = collectControlPathMirrorErrors(policyFixture, ruleText)

  assert.ok(errors.some((error) => /cannot parse/.test(error)), errors.join(' | '))
})

test('fails closed when the paths: block is empty', () => {
  const errors = collectControlPathMirrorErrors(policyFixture, '---\npaths:\n---\n\nBody.\n')

  assert.equal(errors.length, 1)
  assert.match(errors[0], /declares no indented entries/)
})

test('fails closed on malformed YAML AFTER the paths: block', () => {
  const ruleText = `${ruleFixture(mirroredPaths, '').replace(/---\n$/, '')}broken: [\n---\n\nBody.\n`
  const errors = collectControlPathMirrorErrors(policyFixture, ruleText)

  assert.ok(errors.length > 0, 'malformed YAML after the block must not pass')
  assert.ok(
    errors.some((error) => /unterminated flow sequence or mapping/.test(error)),
    errors.join(' | '),
  )
  assert.ok(errors.some((error) => /broken: \[/.test(error)), errors.join(' | '))
})

test('fails closed on malformed YAML BEFORE the paths: block', () => {
  const ruleText = `---\ndescription: "rule\npaths:\n  - "ci/**"\n---\n\nBody.\n`
  const errors = collectControlPathMirrorErrors(policyFixture, ruleText)

  assert.ok(errors.length > 0, 'malformed YAML before the block must not pass')
  assert.ok(errors.some((error) => /cannot parse/.test(error)), errors.join(' | '))
})

test('fails closed on a duplicate paths: key, which a loader would resolve to one of the two', () => {
  const ruleText = '---\npaths:\n  - "ci/**"\n  - "scripts/ci/**"\n  - ".github/workflows/**"\n  - "global.json"\npaths:\n  - "docs/**"\n---\n\nBody.\n'
  const errors = collectControlPathMirrorErrors(policyFixture, ruleText)

  assert.equal(errors.length, 1)
  assert.match(errors[0], /declares the key "paths" twice/)
})

test('fails closed on any duplicate top-level key, not only paths:', () => {
  const ruleText = `---\ndescription: "one"\n${ruleFixture(mirroredPaths, '').slice(4).replace(/---\n$/, '')}description: "two"\n---\n\nBody.\n`
  const errors = collectControlPathMirrorErrors(policyFixture, ruleText)

  assert.equal(errors.length, 1)
  assert.match(errors[0], /declares the key "description" twice/)
})

test('fails closed on a list entry at column 0 rather than reporting an empty block', () => {
  const ruleText = '---\npaths:\n- "ci/**"\n---\n\nBody.\n'
  const errors = collectControlPathMirrorErrors(policyFixture, ruleText)

  assert.ok(errors.some((error) => /entries must be indented/.test(error)), errors.join(' | '))
})

test('reports an orphaned-entry cascade once, keeping the line that broke the document readable', () => {
  const ruleText = `---\npaths: [broken\n${mirroredPaths.map((path) => `  - "${path}"`).join('\n')}\n---\n\nBody.\n`
  const errors = collectControlPathMirrorErrors(policyFixture, ruleText)

  assert.equal(errors.length, 2)
  assert.match(errors[0], /unterminated flow sequence or mapping on key "paths"/)
  assert.match(errors[1], /further orphaned entries not listed/)
})

test('accepts a legal second key after the paths: block', () => {
  const ruleText = `${ruleFixture(mirroredPaths, '').replace(/---\n$/, '')}description: "CI-control region"\n---\n\nBody.\n`

  assert.deepEqual(collectControlPathMirrorErrors(policyFixture, ruleText), [])
})

test('accepts comments on a key, inside the list, and at column 0', () => {
  const ruleText = [
    '---',
    '# the mirror of ci/policy.v1.json controlPaths',
    'paths: # authority: ci/policy.v1.json',
    '  - "ci/**"',
    '  # policy and planner live together',
    '  - "scripts/ci/**"',
    '  - ".github/workflows/**"',
    '  - "global.json"',
    '---',
    '',
    'Body.',
    '',
  ].join('\n')

  assert.deepEqual(collectControlPathMirrorErrors(policyFixture, ruleText), [])
})

test('accepts unquoted globs and CRLF line endings', () => {
  const ruleText = ['---', 'paths:', ...mirroredPaths.map((path) => `  - ${path}`), '---', '', 'Body.', ''].join('\r\n')

  assert.deepEqual(collectControlPathMirrorErrors(policyFixture, ruleText), [])
})

test('fails closed when the policy is not parseable JSON', () => {
  const errors = collectControlPathMirrorErrors('{ not json', ruleFixture(mirroredPaths))

  assert.equal(errors.length, 1)
  assert.match(errors[0], /not parseable JSON/)
})

test('fails closed when the policy declares no controlPaths array', () => {
  const errors = collectControlPathMirrorErrors('{"policyId":"fixture"}', ruleFixture(mirroredPaths))

  assert.equal(errors.length, 1)
  assert.match(errors[0], /does not declare a controlPaths array/)
})

test('fails closed when controlPaths is empty, so the check is never vacuous', () => {
  const errors = collectControlPathMirrorErrors('{"controlPaths":[]}', ruleFixture(mirroredPaths))

  assert.equal(errors.length, 1)
  assert.match(errors[0], /empty controlPaths array/)
})

test('fails closed when a controlPaths entry is not a non-empty string', () => {
  const errors = collectControlPathMirrorErrors('{"controlPaths":["ci/**", 7]}', ruleFixture(mirroredPaths))

  assert.equal(errors.length, 1)
  assert.match(errors[0], /only non-empty strings/)
})

test('the repository files themselves satisfy the mirror', () => {
  const policyText = readFileSync(new URL(`../${CI_POLICY_PATH}`, import.meta.url), 'utf8')
  const ruleText = readFileSync(new URL(`../${CI_CONTROL_RULE_PATH}`, import.meta.url), 'utf8')

  const { controlPaths } = parsePolicyControlPaths(policyText)
  const { paths, errors: parseErrors } = parseRuleFrontMatterPaths(ruleText)

  assert.deepEqual(parseErrors, [])
  assert.ok(controlPaths.length >= 36, `expected the policy to declare its control paths, got ${controlPaths.length}`)
  assert.ok(paths.length >= controlPaths.length, 'the rule front matter must be a superset of controlPaths')
  assert.deepEqual(collectControlPathMirrorErrors(policyText, ruleText), [])
})
