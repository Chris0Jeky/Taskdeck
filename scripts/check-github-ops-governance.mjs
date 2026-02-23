#!/usr/bin/env node

import { access, readFile } from 'node:fs/promises'
import { constants as fsConstants } from 'node:fs'
import { resolve } from 'node:path'

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

async function main() {
  await validateIssueTemplates()
  await validateIssueTemplateConfig()
  await validateProjectAutomationDocs()

  if (errors.length > 0) {
    console.error('GitHub operations governance check failed:')
    for (const error of errors) {
      console.error(`- ${error}`)
    }
    process.exit(1)
  }

  console.log('GitHub operations governance check passed.')
}

main().catch((error) => {
  console.error('GitHub operations governance check crashed:', error)
  process.exit(1)
})
