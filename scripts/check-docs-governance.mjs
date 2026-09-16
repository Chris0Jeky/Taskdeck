#!/usr/bin/env node

import { access, readFile } from 'node:fs/promises'
import { constants as fsConstants } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

export const CI_POLICY_PATH = 'ci/policy.v1.json'
export const CI_CONTROL_RULE_PATH = '.claude/rules/ci-control.md'

const requiredDocs = [
  'docs/STATUS.md',
  'docs/IMPLEMENTATION_MASTERPLAN.md',
  'docs/TESTING_GUIDE.md',
  'docs/MANUAL_TEST_CHECKLIST.md',
  'docs/GOLDEN_PRINCIPLES.md',
]

const errors = []

async function fileExists(path) {
  try {
    await access(resolve(path), fsConstants.F_OK)
    return true
  } catch {
    return false
  }
}

function expectContains(source, token, label) {
  if (!source.includes(token)) {
    errors.push(`${label} is missing required token: ${token}`)
  }
}

/**
 * Read `controlPaths` out of the Smart CI policy document.
 *
 * Fails closed: an unreadable policy, a missing `controlPaths` array, or a non-string entry is an
 * error rather than an empty list, because an empty list would make the mirror check vacuously pass.
 */
export function parsePolicyControlPaths(policyText, policyPath = CI_POLICY_PATH) {
  let policy
  try {
    policy = JSON.parse(policyText)
  } catch (error) {
    return { controlPaths: [], errors: [`${policyPath} is not parseable JSON: ${error.message}`] }
  }

  const controlPaths = policy?.controlPaths
  if (!Array.isArray(controlPaths)) {
    return { controlPaths: [], errors: [`${policyPath} does not declare a controlPaths array`] }
  }

  if (controlPaths.length === 0) {
    return { controlPaths: [], errors: [`${policyPath} declares an empty controlPaths array`] }
  }

  const invalid = controlPaths.filter((entry) => typeof entry !== 'string' || entry.trim() === '')
  if (invalid.length > 0) {
    return {
      controlPaths: [],
      errors: [`${policyPath} controlPaths must contain only non-empty strings`],
    }
  }

  if (controlPaths.some((entry) => entry !== entry.trim())) {
    return {
      controlPaths: [],
      errors: [`${policyPath} controlPaths must not contain leading or trailing whitespace`],
    }
  }

  return { controlPaths, errors: [] }
}

/**
 * Read the supported single-line scalar subset, not arbitrary YAML.
 *
 * Quoted strings support JSON double-quote escapes or YAML doubled single quotes. Plain scalars
 * keep internal quotes/brackets literally; only leading indicators select YAML structure. Tags,
 * aliases, anchors, block/flow collections and multiline scalars are deliberately unsupported.
 */
function parseFrontMatterScalar(rawValue) {
  const text = rawValue.trim()
  if (text === '') {
    return { value: null, error: 'empty unquoted scalar' }
  }

  if (text.startsWith('"')) {
    const quoted = text.match(/^("(?:[^"\\]|\\.)*")(?:[ \t]+#.*)?$/)
    if (!quoted) {
      return { value: null, error: 'unbalanced quote or trailing content' }
    }
    try {
      return { value: JSON.parse(quoted[1]), error: null }
    } catch {
      return { value: null, error: 'unsupported double-quoted escape or control character' }
    }
  }

  if (text.startsWith("'")) {
    const quoted = text.match(/^'((?:[^']|'')*)'(?:[ \t]+#.*)?$/)
    return quoted
      ? { value: quoted[1].replaceAll("''", "'"), error: null }
      : { value: null, error: 'unbalanced quote or trailing content' }
  }

  const value = text.replace(/[ \t]+#.*$/, '')
  if (/^[\[{]/.test(value)) {
    const closer = value[0] === '[' ? ']' : '}'
    return {
      value: null,
      error: value.endsWith(closer)
        ? 'unsupported flow sequence or mapping'
        : 'unterminated flow sequence or mapping',
    }
  }
  if (/^[!&*|>@`%}\],#]/.test(value) || /^[-?:](?:[ \t]|$)/.test(value)) {
    return { value: null, error: 'unsupported leading scalar indicator' }
  }
  if (/:(?:[ \t]|$)/.test(value)) {
    return { value: null, error: 'unsupported nested mapping' }
  }

  return { value, error: null }
}

/**
 * Validate the WHOLE front matter block, not just `paths:`. An invalid or unsupported line anywhere
 * must fail closed rather than letting the mirror check certify a rule its loader might reject.
 *
 * Accepted: top-level keys with a separated single-line scalar, or one flat indented scalar list;
 * comments and blank lines. Each list chooses its own indentation, but every sibling must match it.
 * This dependency-free check intentionally does not implement the complete YAML grammar.
 */
function validateFrontMatterStructure(lines, rulePath) {
  const structureErrors = []
  const seenKeys = new Set()
  let blockKey = null
  let blockIndent = null
  let reportedOrphanEntry = false

  for (const line of lines) {
    if (line.trim() === '') {
      continue
    }
    if (/^[ \t]*\t/.test(line)) {
      structureErrors.push(`${rulePath} front matter has tab indentation, which this check cannot parse: ${line.trim()}`)
      continue
    }
    if (/^ *#/.test(line)) {
      continue
    }

    if (/^ /.test(line)) {
      const entry = line.match(/^( +)-(?:[ \t]+(.*))?$/)
      if (!entry) {
        structureErrors.push(`${rulePath} front matter has a line this check cannot parse: ${line.trim()}`)
        continue
      }
      if (blockKey === null) {
        if (!reportedOrphanEntry) {
          reportedOrphanEntry = true
          structureErrors.push(
            `${rulePath} front matter has a list entry with no preceding key, which this check cannot parse: ${line.trim()} (further orphaned entries not listed)`,
          )
        }
        continue
      }
      blockIndent ??= entry[1].length
      if (entry[1].length !== blockIndent) {
        structureErrors.push(`${rulePath} front matter has nested or inconsistent list indentation, which this check cannot parse: ${line.trim()}`)
        continue
      }
      // Empty quoted metadata strings are valid; only the paths consumer requires nonempty values.
      const { error } = parseFrontMatterScalar(entry[2] ?? '')
      if (error !== null) {
        structureErrors.push(`${rulePath} front matter has a list entry this check cannot parse (${error}): ${line.trim()}`)
      }
      continue
    }

    if (/^-(\s|$)/.test(line)) {
      structureErrors.push(
        `${rulePath} front matter has a list entry at column 0 that this check cannot parse (entries must be indented): ${line.trim()}`,
      )
      continue
    }

    // A colon without separation starts plain scalar text, not a YAML mapping value.
    const keyMatch = line.match(/^([A-Za-z0-9_][A-Za-z0-9_.-]*) *:(?:[ \t]+(.*))?$/)
    if (!keyMatch) {
      structureErrors.push(`${rulePath} front matter has a line this check cannot parse: ${line.trim()}`)
      blockKey = null
      blockIndent = null
      continue
    }

    const [, key, rawValue = ''] = keyMatch
    if (seenKeys.has(key)) {
      structureErrors.push(
        `${rulePath} front matter declares the key "${key}" twice; duplicate mapping keys are not supported`,
      )
    }
    seenKeys.add(key)
    blockIndent = null
    reportedOrphanEntry = false

    const value = rawValue.trim()
    if (value === '' || value.startsWith('#')) {
      blockKey = key
      continue
    }

    const { error } = parseFrontMatterScalar(value)
    if (key === 'paths' && error === 'unsupported flow sequence or mapping') {
      structureErrors.push(`${rulePath} front matter paths: must be a block sequence of "- glob" entries`)
    } else if (error !== null) {
      structureErrors.push(
        `${rulePath} front matter has an ${error} on key "${key}", which this check cannot parse: ${line.trim()}`,
      )
    }
    blockKey = null
  }

  return structureErrors
}

/**
 * Parse the `paths:` block sequence out of an agent-rule file's YAML front matter.
 *
 * Fails closed on every shape it does not fully understand, and validates the complete front matter
 * block first. Claude Code drops a rule file whose front matter does not parse, silently and with no
 * error anywhere, so "cannot parse" has to mean "red check", never "no paths found".
 */
export function parseRuleFrontMatterPaths(ruleText, rulePath = CI_CONTROL_RULE_PATH) {
  const frontMatterMatch = ruleText.match(/^---\r?\n([\s\S]*?)\r?\n---(\r?\n|$)/)
  if (!frontMatterMatch) {
    return {
      paths: [],
      errors: [`${rulePath} has no parseable YAML front matter (an unparseable rule file loads for nothing)`],
    }
  }

  const lines = frontMatterMatch[1].split(/\r?\n/)

  const structureErrors = validateFrontMatterStructure(lines, rulePath)
  if (structureErrors.length > 0) {
    return { paths: [], errors: structureErrors }
  }

  const keyIndex = lines.findIndex((line) => /^paths\s*:/.test(line))
  if (keyIndex === -1) {
    return { paths: [], errors: [`${rulePath} front matter has no paths: key`] }
  }

  if (!/^paths\s*:\s*(#.*)?$/.test(lines[keyIndex])) {
    return {
      paths: [],
      errors: [`${rulePath} front matter paths: must be a block sequence of "- glob" entries`],
    }
  }

  const paths = []
  const errors = []
  for (let index = keyIndex + 1; index < lines.length; index += 1) {
    const line = lines[index]
    if (line.trim() === '' || /^\s*#/.test(line)) {
      continue
    }

    if (!/^\s/.test(line)) {
      break
    }

    const itemMatch = line.match(/^\s+-\s+(.*?)\s*$/)
    if (!itemMatch) {
      errors.push(`${rulePath} front matter paths: has an entry this check cannot parse: ${line.trim()}`)
      continue
    }

    const { value } = parseFrontMatterScalar(itemMatch[1])
    if (value === null || value === '' || value !== value.trim() || /[\x00-\x1f\x7f]/.test(value)) {
      errors.push(`${rulePath} front matter paths: has an entry this check cannot parse: ${line.trim()}`)
      continue
    }

    paths.push(value)
  }

  if (errors.length === 0 && paths.length === 0) {
    errors.push(`${rulePath} front matter paths: declares no indented entries`)
  }

  return { paths, errors }
}

/**
 * `ci/policy.v1.json` controlPaths is the authority; the rule file's `paths:` front matter is a
 * mirror of it, and must be a superset (extras such as `.github/**` are deliberate, see the rule).
 */
export function collectControlPathMirrorErrors(
  policyText,
  ruleText,
  { policyPath = CI_POLICY_PATH, rulePath = CI_CONTROL_RULE_PATH } = {},
) {
  const policyResult = parsePolicyControlPaths(policyText, policyPath)
  const ruleResult = parseRuleFrontMatterPaths(ruleText, rulePath)
  const mirrorErrors = [...policyResult.errors, ...ruleResult.errors]

  if (mirrorErrors.length > 0) {
    return mirrorErrors
  }

  const declared = new Set(ruleResult.paths)
  const missing = policyResult.controlPaths.filter((controlPath) => !declared.has(controlPath))
  if (missing.length > 0) {
    mirrorErrors.push(
      `${rulePath} front matter paths: is missing ${missing.length} control path(s) declared in ` +
        `${policyPath} controlPaths: ${missing.join(', ')} ` +
        `(add them to the rule in the same PR, or the rule stops loading for those paths)`,
    )
  }

  return mirrorErrors
}

async function validateControlPathMirror() {
  for (const path of [CI_POLICY_PATH, CI_CONTROL_RULE_PATH]) {
    if (!(await fileExists(path))) {
      errors.push(`Missing required control-path mirror input: ${path}`)
      return
    }
  }

  const [policyText, ruleText] = await Promise.all([
    readFile(resolve(CI_POLICY_PATH), 'utf8'),
    readFile(resolve(CI_CONTROL_RULE_PATH), 'utf8'),
  ])

  errors.push(...collectControlPathMirrorErrors(policyText, ruleText))
}

async function main() {
  for (const path of requiredDocs) {
    if (!(await fileExists(path))) {
      errors.push(`Missing required active document: ${path}`)
    }
  }

  if (!(await fileExists('docs/INDEX.md'))) {
    errors.push('Missing required docs index: docs/INDEX.md')
  } else {
    const indexText = await readFile(resolve('docs/INDEX.md'), 'utf8')
    for (const path of requiredDocs) {
      expectContains(indexText, path.replace('docs/', ''), 'docs/INDEX.md')
    }

    const hasArchiveLink = indexText.includes('archive/') || indexText.includes('docs/archive/')
    if (!hasArchiveLink) {
      errors.push('docs/INDEX.md must reference the archive directory')
    }
  }

  const docsRequiringLastUpdated = [
    'docs/STATUS.md',
    'docs/GOLDEN_PRINCIPLES.md',
  ]

  for (const path of docsRequiringLastUpdated) {
    if (!(await fileExists(path))) {
      continue
    }

    const text = await readFile(resolve(path), 'utf8')
    const hasLastUpdatedLine = /^Last Updated:\s*\d{4}-\d{2}-\d{2}\s*$/m.test(text)
    if (!hasLastUpdatedLine) {
      errors.push(`${path} must contain a "Last Updated: YYYY-MM-DD" line`)
    }
  }

  await validateControlPathMirror()

  if (errors.length > 0) {
    console.error('Docs governance check failed:')
    for (const error of errors) {
      console.error(`- ${error}`)
    }
    process.exit(1)
  }

  console.log('Docs governance check passed.')
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch((error) => {
    console.error('Docs governance check crashed:', error)
    process.exit(1)
  })
}
