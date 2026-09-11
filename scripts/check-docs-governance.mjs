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

  return { controlPaths: controlPaths.map((entry) => entry.trim()), errors: [] }
}

function parseFrontMatterScalar(rawValue) {
  const doubleQuoted = rawValue.match(/^"([^"]*)"$/)
  if (doubleQuoted) {
    return { value: doubleQuoted[1] }
  }

  const singleQuoted = rawValue.match(/^'([^']*)'$/)
  if (singleQuoted) {
    return { value: singleQuoted[1] }
  }

  if (/["'#]/.test(rawValue)) {
    return { value: null }
  }

  return { value: rawValue }
}

/**
 * Parse the `paths:` block sequence out of an agent-rule file's YAML front matter.
 *
 * Fails closed on every shape it does not fully understand. Claude Code drops a rule file whose
 * front matter does not parse, silently and with no error anywhere, so "cannot parse" has to mean
 * "red check", never "no paths found".
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
    if (line.trim() === '') {
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
    if (value === null || value === '') {
      errors.push(`${rulePath} front matter paths: has an entry this check cannot parse: ${line.trim()}`)
      continue
    }

    paths.push(value)
  }

  if (errors.length === 0 && paths.length === 0) {
    errors.push(`${rulePath} front matter paths: declares no entries`)
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
