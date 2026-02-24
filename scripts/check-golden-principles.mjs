#!/usr/bin/env node

import { access, readFile } from 'node:fs/promises'
import { constants as fsConstants } from 'node:fs'
import { resolve } from 'node:path'

const goldenPath = 'docs/GOLDEN_PRINCIPLES.md'
const requiredPrincipleIds = [
  'GP-01',
  'GP-02',
  'GP-03',
  'GP-04',
  'GP-05',
  'GP-06',
  'GP-07',
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
  if (!(await fileExists(goldenPath))) {
    errors.push(`Missing required golden principles document: ${goldenPath}`)
  } else {
    const goldenText = await readFile(resolve(goldenPath), 'utf8')
    const hasLastUpdatedLine = /^Last Updated:\s*\d{4}-\d{2}-\d{2}\s*$/m.test(goldenText)
    if (!hasLastUpdatedLine) {
      errors.push(`${goldenPath} must contain a "Last Updated: YYYY-MM-DD" line`)
    }

    for (const principleId of requiredPrincipleIds) {
      expectContains(goldenText, principleId, goldenPath)
    }
  }

  if (errors.length > 0) {
    console.error('Golden principles check failed:')
    for (const error of errors) {
      console.error(`- ${error}`)
    }
    process.exit(1)
  }

  console.log('Golden principles check passed.')
}

main().catch((error) => {
  console.error('Golden principles check crashed:', error)
  process.exit(1)
})
