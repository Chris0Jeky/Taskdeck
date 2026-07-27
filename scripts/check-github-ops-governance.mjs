#!/usr/bin/env node

import { access, readFile } from 'node:fs/promises'
import { constants as fsConstants } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const errors = []

const requiredIssueTemplateFiles = [
  '.github/ISSUE_TEMPLATE/bug_report.md',
  '.github/ISSUE_TEMPLATE/feature.md',
  '.github/ISSUE_TEMPLATE/security_hardening.md',
  '.github/ISSUE_TEMPLATE/tech_debt_refactor.md',
]

const allowedTemplateLabels = new Set([
  'bug',
  'security',
  'hardening',
  'backend',
  'frontend',
  'ux',
  'testing',
  'docs',
  'refactor',
  'tech-debt',
  'starter-packs',
  'llm',
  'feature',
  'automation',
  'worker',
  'performance',
])

const deprecatedTemplateLabels = new Set([])

async function fileExists(path) {
  try {
    await access(resolve(path), fsConstants.F_OK)
    return true
  } catch {
    return false
  }
}

function parseLabelsFromFrontMatter(markdown, path) {
  const frontMatterMatch = markdown.match(/^---\r?\n([\s\S]*?)\r?\n---/)
  if (!frontMatterMatch) {
    errors.push(`${path} is missing YAML front matter`)
    return []
  }

  const frontMatter = frontMatterMatch[1]
  const labelsLine = frontMatter.match(/^\s*labels:\s*\[(.*)\]\s*$/m)
  if (!labelsLine) {
    errors.push(`${path} is missing a labels array in front matter`)
    return []
  }

  return labelsLine[1]
    .split(',')
    .map((value) => value.trim().replace(/^['"]|['"]$/g, ''))
    .filter((value) => value.length > 0)
}

async function validateIssueTemplates() {
  for (const path of requiredIssueTemplateFiles) {
    if (!(await fileExists(path))) {
      errors.push(`Missing required issue template: ${path}`)
      continue
    }

    const template = await readFile(resolve(path), 'utf8')
    const labels = parseLabelsFromFrontMatter(template, path)

    if (labels.length === 0) {
      errors.push(`${path} must define at least one label`)
      continue
    }

    for (const label of labels) {
      if (deprecatedTemplateLabels.has(label)) {
        errors.push(`${path} uses deprecated label "${label}"`)
      }

      if (!allowedTemplateLabels.has(label)) {
        errors.push(`${path} uses unsupported label "${label}"`)
      }
    }
  }
}

async function validateIssueTemplateConfig() {
  const configPath = '.github/ISSUE_TEMPLATE/config.yml'
  if (!(await fileExists(configPath))) {
    errors.push(`Missing required issue template config: ${configPath}`)
    return
  }

  const configText = await readFile(resolve(configPath), 'utf8')
  if (!/^blank_issues_enabled:\s*false\s*$/m.test(configText)) {
    errors.push(`${configPath} must set "blank_issues_enabled: false"`)
  }
}

async function validateProjectAutomationDocs() {
  if (!(await fileExists('docs/GITHUB_PROJECT_AUTOMATION.md'))) {
    errors.push('Missing required project automation doc: docs/GITHUB_PROJECT_AUTOMATION.md')
  } else {
    const automationText = await readFile(resolve('docs/GITHUB_PROJECT_AUTOMATION.md'), 'utf8')
    const requiredTokens = [
      '`Pending`',
      '`Now`',
      '`Next`',
      '`Blocked`',
      '`Review`',
      '`Done`',
      '`Item added to project`',
      '`Item reopened`',
      '`Item closed`',
      '`Pull request linked to issue`',
      '`Pull request merged`',
    ]

    for (const token of requiredTokens) {
      if (!automationText.includes(token)) {
        errors.push(`docs/GITHUB_PROJECT_AUTOMATION.md is missing required token: ${token}`)
      }
    }
  }

  if (!(await fileExists('AGENTS.md'))) {
    errors.push('Missing AGENTS.md at repository root')
    return
  }

  const agentsText = await readFile(resolve('AGENTS.md'), 'utf8')
  if (!agentsText.includes('docs/GITHUB_PROJECT_AUTOMATION.md')) {
    errors.push('AGENTS.md must reference docs/GITHUB_PROJECT_AUTOMATION.md')
  }
}

export function inspectParkedStagingGateTriggers(workflowText) {
  const lines = workflowText.split(/\r?\n/)
  const onIndex = lines.findIndex((line) => line === 'on:')
  if (onIndex < 0) {
    return { onBlockFound: false, triggerNames: [], unsupportedEntries: [] }
  }

  const triggerNames = []
  const unsupportedEntries = []
  for (let index = onIndex + 1; index < lines.length; index += 1) {
    const line = lines[index]
    if (line.length > 0 && !/^\s/.test(line)) {
      break
    }

    // This governance contract deliberately requires two-space event keys. Any
    // unfamiliar entry at that level fails closed instead of being ignored.
    const eventEntry = line.match(/^ {2}(?!\s)(.*)$/)?.[1]
    if (!eventEntry || eventEntry.startsWith('#')) {
      continue
    }

    const eventMatch = eventEntry.match(
      /^(?:"([^"\r\n]+)"|'([^'\r\n]+)'|([A-Za-z][\w-]*))\s*:(?:\s*.*)?$/,
    )
    if (!eventMatch) {
      unsupportedEntries.push(eventEntry)
      continue
    }

    triggerNames.push(eventMatch[1] ?? eventMatch[2] ?? eventMatch[3])
  }

  return { onBlockFound: true, triggerNames, unsupportedEntries }
}

export function retainsReleaseEventHandling(workflowText) {
  return /github\.event(?:_name|\.release)\b/.test(workflowText) || /\bEVENT_NAME\b/.test(workflowText)
}

function parseMappingEntryAtIndent(line, indent) {
  const content = line.match(new RegExp(`^ {${indent}}(?!\\s)(.*)$`))?.[1]
  if (!content || content.startsWith('#')) {
    return null
  }

  const match = content.match(
    /^(?:"([^"\r\n]+)"|'([^'\r\n]+)'|([A-Za-z][\w-]*))\s*:\s*(.*)$/,
  )
  if (!match) {
    return null
  }

  return { key: match[1] ?? match[2] ?? match[3], value: match[4] }
}

function findDirectChild(lines, parentIndex, parentIndent, childIndent, key) {
  for (let index = parentIndex + 1; index < lines.length; index += 1) {
    const line = lines[index]
    const trimmed = line.trim()
    if (trimmed.length === 0 || trimmed.startsWith('#')) {
      continue
    }

    const indentation = line.match(/^ */)?.[0].length ?? 0
    if (indentation <= parentIndent) {
      break
    }

    const entry = parseMappingEntryAtIndent(line, childIndent)
    if (entry?.key === key) {
      return { index, entry }
    }
  }

  return null
}

function normalizeYamlScalar(value) {
  const withoutComment = value.replace(/\s+#.*$/, '').trim()
  const quoted = withoutComment.match(/^(?:"([^"]*)"|'([^']*)')$/)
  return quoted ? (quoted[1] ?? quoted[2]) : withoutComment
}

export function inspectWorkflowDispatchImageTagInput(workflowText) {
  const lines = workflowText.split(/\r?\n/)
  const onIndex = lines.findIndex((line) => line === 'on:')
  if (onIndex < 0) {
    return { imageTagFound: false, requiredValues: [], typeValues: [] }
  }

  const workflowDispatch = findDirectChild(lines, onIndex, 0, 2, 'workflow_dispatch')
  const inputs = workflowDispatch
    ? findDirectChild(lines, workflowDispatch.index, 2, 4, 'inputs')
    : null
  const imageTag = inputs ? findDirectChild(lines, inputs.index, 4, 6, 'image_tag') : null
  if (!imageTag) {
    return { imageTagFound: false, requiredValues: [], typeValues: [] }
  }

  const requiredValues = []
  const typeValues = []
  for (let index = imageTag.index + 1; index < lines.length; index += 1) {
    const line = lines[index]
    const trimmed = line.trim()
    if (trimmed.length === 0 || trimmed.startsWith('#')) {
      continue
    }

    const indentation = line.match(/^ */)?.[0].length ?? 0
    if (indentation <= 6) {
      break
    }

    const entry = parseMappingEntryAtIndent(line, 8)
    if (entry?.key === 'required') {
      requiredValues.push(normalizeYamlScalar(entry.value))
    } else if (entry?.key === 'type') {
      typeValues.push(normalizeYamlScalar(entry.value))
    }
  }

  return { imageTagFound: true, requiredValues, typeValues }
}

export function validateParkedStagingGateWorkflow(workflowText, workflowPath = '.github/workflows/cd-staging-gate.yml') {
  const workflowErrors = []
  const inspection = inspectParkedStagingGateTriggers(workflowText)
  if (!inspection.onBlockFound) {
    return [`${workflowPath} is missing its top-level on block`]
  }

  if (inspection.unsupportedEntries.length > 0) {
    workflowErrors.push(
      `${workflowPath} has unsupported top-level trigger entries: ${inspection.unsupportedEntries.join(', ')}`,
    )
  }

  if (inspection.triggerNames.length !== 1 || inspection.triggerNames[0] !== 'workflow_dispatch') {
    workflowErrors.push(
      `${workflowPath} is parked and must remain manual-only; expected only workflow_dispatch, found: ${inspection.triggerNames.join(', ') || '(none)'}`,
    )
  }

  const imageTagInput = inspectWorkflowDispatchImageTagInput(workflowText)
  if (!imageTagInput.imageTagFound) {
    workflowErrors.push(`${workflowPath} must define workflow_dispatch.inputs.image_tag`)
  } else {
    if (imageTagInput.requiredValues.length !== 1 || imageTagInput.requiredValues[0] !== 'true') {
      workflowErrors.push(`${workflowPath} workflow_dispatch.inputs.image_tag must set required: true exactly once`)
    }
    if (imageTagInput.typeValues.length !== 1 || imageTagInput.typeValues[0] !== 'string') {
      workflowErrors.push(`${workflowPath} workflow_dispatch.inputs.image_tag must set type: string exactly once`)
    }
  }

  if (retainsReleaseEventHandling(workflowText)) {
    workflowErrors.push(`${workflowPath} retains unreachable release-event handling after becoming manual-only`)
  }

  return workflowErrors
}

async function validateParkedStagingGateTriggers() {
  const workflowPath = '.github/workflows/cd-staging-gate.yml'
  if (!(await fileExists(workflowPath))) {
    errors.push(`Missing parked staging workflow: ${workflowPath}`)
    return
  }

  const workflowText = await readFile(resolve(workflowPath), 'utf8')
  errors.push(...validateParkedStagingGateWorkflow(workflowText, workflowPath))
}

async function main() {
  await validateIssueTemplates()
  await validateIssueTemplateConfig()
  await validateProjectAutomationDocs()
  await validateParkedStagingGateTriggers()

  if (errors.length > 0) {
    console.error('GitHub operations governance check failed:')
    for (const error of errors) {
      console.error(`- ${error}`)
    }
    process.exit(1)
  }

  console.log('GitHub operations governance check passed.')
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch((error) => {
    console.error('GitHub operations governance check crashed:', error)
    process.exit(1)
  })
}
