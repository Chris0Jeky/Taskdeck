#!/usr/bin/env node

import { randomBytes } from 'node:crypto'
import { execFile } from 'node:child_process'
import { appendFile } from 'node:fs/promises'
import { writeSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'

const execFileAsync = promisify(execFile)

const expectedRequiredVariables = [
  'TASKDECK_CONNECTORS_ENCRYPTION_KEY',
  'TASKDECK_JWT_SECRET',
]

export function parseComposeVariableInventory(inventoryJson) {
  let parsedInventory
  try {
    parsedInventory = JSON.parse(inventoryJson)
  } catch (error) {
    throw new Error(`Docker Compose variable inspection returned invalid JSON: ${error.message}`)
  }

  if (
    parsedInventory === null ||
    typeof parsedInventory !== 'object' ||
    Array.isArray(parsedInventory)
  ) {
    throw new Error('Docker Compose variable inspection must return a JSON object')
  }

  return Object.entries(parsedInventory)
    .map(([key, metadata]) => {
      if (metadata === null || typeof metadata !== 'object' || Array.isArray(metadata)) {
        throw new Error(`Docker Compose variable metadata for ${key} must be an object`)
      }
      if (metadata.Name !== key) {
        throw new Error(
          `Docker Compose variable key/Name mismatch: key ${key}, Name ${String(metadata.Name)}`,
        )
      }
      if (typeof metadata.Required !== 'boolean') {
        throw new Error(`Docker Compose variable ${key} must declare boolean Required`)
      }

      return { name: key, required: metadata.Required }
    })
    .sort((left, right) => left.name.localeCompare(right.name))
}

export function assertExpectedRequiredComposeVariables(variableInventory) {
  if (!Array.isArray(variableInventory)) {
    throw new Error('Docker Compose variable inventory must be an array')
  }

  const actualVariables = variableInventory
    .filter((variable) => variable.required)
    .map((variable) => variable.name)
    .sort()
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

export async function inspectComposeVariables({
  composePath = 'deploy/docker-compose.yml',
  executeCommand = execFileAsync,
} = {}) {
  let commandResult
  try {
    commandResult = await executeCommand(
      'docker',
      [
        'compose',
        '-f',
        resolve(composePath),
        '--profile',
        'baseline',
        'config',
        '--variables',
        '--format',
        'json',
      ],
      { encoding: 'utf8', maxBuffer: 1024 * 1024, windowsHide: true },
    )
  } catch (error) {
    const detail = error instanceof Error ? error.message : String(error)
    throw new Error(`Docker Compose variable inspection failed: ${detail}`, { cause: error })
  }

  if (commandResult === null || typeof commandResult?.stdout !== 'string') {
    throw new Error('Docker Compose variable inspection did not return string stdout')
  }

  return parseComposeVariableInventory(commandResult.stdout)
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
  executeCommand = execFileAsync,
} = {}) {
  if (!githubEnvPath) {
    throw new Error('GITHUB_ENV must identify the GitHub Actions job environment file')
  }

  const variableInventory = await inspectComposeVariables({ composePath, executeCommand })
  const requiredVariables = assertExpectedRequiredComposeVariables(variableInventory)
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
