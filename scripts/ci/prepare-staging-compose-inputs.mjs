#!/usr/bin/env node

import { randomBytes } from 'node:crypto'
import { appendFile, readFile } from 'node:fs/promises'
import { writeSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const expectedRequiredVariables = [
  'TASKDECK_CONNECTORS_ENCRYPTION_KEY',
  'TASKDECK_JWT_SECRET',
]

export function findRequiredComposeVariables(composeText) {
  const variables = new Set()
  const requiredVariablePattern = /\$\{([A-Za-z_][A-Za-z0-9_]*)(?::)?\?[^}\r\n]*\}/g

  for (const match of composeText.matchAll(requiredVariablePattern)) {
    variables.add(match[1])
  }

  return [...variables].sort()
}

export function assertExpectedRequiredComposeVariables(composeText) {
  const actualVariables = findRequiredComposeVariables(composeText)
  const missingVariables = expectedRequiredVariables.filter(
    (variable) => !actualVariables.includes(variable),
  )
  const unexpectedVariables = actualVariables.filter(
    (variable) => !expectedRequiredVariables.includes(variable),
  )

  if (missingVariables.length > 0 || unexpectedVariables.length > 0) {
    throw new Error(
      `Compose required-variable inventory mismatch: expected [${expectedRequiredVariables.join(', ')}], ` +
        `actual [${actualVariables.join(', ')}], missing [${missingVariables.join(', ')}], ` +
        `unexpected [${unexpectedVariables.join(', ')}]`,
    )
  }

  return actualVariables
}

function defaultEmitMaskCommand(command) {
  writeSync(process.stdout.fd, command)
}

export async function prepareStagingComposeInputs({
  composePath = 'deploy/docker-compose.yml',
  githubEnvPath = process.env.GITHUB_ENV,
  randomBytesSource = randomBytes,
  emitMaskCommand = defaultEmitMaskCommand,
  appendEnvironmentFile = appendFile,
} = {}) {
  if (!githubEnvPath) {
    throw new Error('GITHUB_ENV must identify the GitHub Actions job environment file')
  }

  const composeText = await readFile(resolve(composePath), 'utf8')
  const requiredVariables = assertExpectedRequiredComposeVariables(composeText)
  const values = {
    TASKDECK_JWT_SECRET: randomBytesSource(48).toString('base64'),
    TASKDECK_CONNECTORS_ENCRYPTION_KEY: randomBytesSource(32).toString('base64'),
  }

  for (const value of Object.values(values)) {
    emitMaskCommand(`::add-mask::${value}\n`)
  }

  const environmentLines = Object.entries(values)
    .map(([name, value]) => `${name}=${value}`)
    .join('\n')
  await appendEnvironmentFile(resolve(githubEnvPath), `${environmentLines}\n`, 'utf8')

  return { requiredVariables }
}

async function main() {
  const { requiredVariables } = await prepareStagingComposeInputs()
  console.log(`Prepared ephemeral inputs for ${requiredVariables.length} required Compose variables.`)
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch((error) => {
    console.error('Failed to prepare staging Compose inputs:', error.message)
    process.exit(1)
  })
}
