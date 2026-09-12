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

test('fails closed on a duplicate paths: key rather than choosing one value', () => {
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

function withExtraFrontMatter(...lines) {
  return ruleFixture(mirroredPaths, '').replace(/---\n$/, `${lines.join('\n')}\n---\n`)
}

for (const scalar of [
  "the CI region's rules",
  'fix [#2928',
  'fix {the mirror',
  'a closing ] or } is text',
  'an internal " quote is text',
  'https://example.invalid/rules#section',
  'rule # an ignored comment containing [ and "',
  String.raw`"a \"quoted\" rule"`,
  "'the CI region''s rules'",
  '"CI rule" # comment containing [',
]) {
  test(`accepts the supported scalar without misreading its punctuation: ${scalar}`, () => {
    assert.deepEqual(collectControlPathMirrorErrors(policyFixture, withExtraFrontMatter(`description: ${scalar}`)), [])
  })
}

for (const lines of [
  ['description:no separator'],
  ['description: - nested sequence'],
  ['description: nested: mapping'],
  ['description: "closed" trailing garbage'],
  ["description: 'closed' trailing garbage"],
  ['description: "bad\\q escape"'],
  ['description: &anchor value'],
  ['description: *unknown'],
  ['description: !tag value'],
  ['description: |'],
  ['description: >'],
  ['description: [balanced, but, unsupported]'],
  ['description: {balanced: unsupported}'],
  ['extra:', '  - "unterminated'],
  ['extra:', '  - nested: mapping'],
  ['extra:', '  - - nested'],
  ['extra:', '  - first', '    - deeper'],
  ['extra:', '    - first', '  - shallower'],
  ['extra:', '\t- tab-indented'],
  ['extra:', '  \t- mixed-indentation'],
]) {
  test(`fails closed on unsupported frontmatter structure: ${JSON.stringify(lines)}`, () => {
    const errors = collectControlPathMirrorErrors(policyFixture, withExtraFrontMatter(...lines))
    assert.ok(errors.some((error) => /cannot parse/.test(error)), errors.join(' | '))
  })
}

test('rejects tabs and nested indentation in the paths block itself', () => {
  const original = ruleFixture(mirroredPaths)
  for (const changed of [
    original.replace('  - "ci/**"', '\t- "ci/**"'),
    original.replace('  - "scripts/ci/**"', '    - "scripts/ci/**"'),
  ]) {
    assert.ok(collectControlPathMirrorErrors(policyFixture, changed).length > 0)
  }
})

test('allows a different flat sequence indentation for each top-level key', () => {
  const rule = withExtraFrontMatter('extra:', '    - first', '    - second')
  assert.deepEqual(collectControlPathMirrorErrors(policyFixture, rule), [])
})

test('rejects an alias-like unquoted glob even when it exactly mirrors policy', () => {
  const policy = JSON.stringify({ controlPaths: ['**/.npmrc'] })
  assert.ok(collectControlPathMirrorErrors(policy, '---\npaths:\n  - **/.npmrc\n---\n').length > 0)
  assert.deepEqual(collectControlPathMirrorErrors(policy, ruleFixture(['**/.npmrc'])), [])
})

for (const path of [' ci/**', 'ci/** ', '\tci/**', 'ci/**\n']) {
  test(`rejects policy whitespace rather than normalizing away the mismatch: ${JSON.stringify(path)}`, () => {
    const result = parsePolicyControlPaths(JSON.stringify({ controlPaths: [path] }))
    assert.equal(result.controlPaths.length, 0)
    assert.ok(result.errors.some((error) => /whitespace/.test(error)), result.errors.join(' | '))
  })
}

test('decodes supported quoting in path values instead of comparing escape source text', () => {
  const paths = ["src/team's/**", 'src/team"s/**', 'src/back\\slash/**']
  const policy = JSON.stringify({ controlPaths: paths })
  const rule = `---\npaths:\n${paths.map((path) => `  - ${JSON.stringify(path)}`).join('\n')}\n---\n`
  assert.deepEqual(collectControlPathMirrorErrors(policy, rule), [])
  assert.deepEqual(parseRuleFrontMatterPaths("---\npaths:\n  - 'src/team''s/**'\n---\n").paths, [paths[0]])
})

test('treats internal punctuation in a plain path as text and separated hashes as comments', () => {
  const paths = ["src/team's/**", 'src/fix[#2928/**', 'src/name#fragment/**']
  const policy = JSON.stringify({ controlPaths: paths })
  const rule = `---\npaths:\n${paths.map((path) => `  - ${path} # explanation`).join('\n')}\n---\n`
  assert.deepEqual(collectControlPathMirrorErrors(policy, rule), [])
})
