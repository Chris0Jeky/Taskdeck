#!/usr/bin/env node

import { access, readFile } from 'node:fs/promises'
import { constants as fsConstants } from 'node:fs'
import { resolve } from 'node:path'

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

  if (errors.length > 0) {
    console.error('Docs governance check failed:')
    for (const error of errors) {
      console.error(`- ${error}`)
    }
    process.exit(1)
  }

  console.log('Docs governance check passed.')
}

main().catch((error) => {
  console.error('Docs governance check crashed:', error)
  process.exit(1)
})
