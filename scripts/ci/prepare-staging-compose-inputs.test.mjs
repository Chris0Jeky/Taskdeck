import test from 'node:test'
import assert from 'node:assert/strict'
import { appendFile, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'

import {
  assertExpectedRequiredComposeVariables,
  inspectComposeVariables,
  parseComposeVariableInventory,
  prepareStagingComposeInputs,
} from './prepare-staging-compose-inputs.mjs'

const expectedVariableInventory = {
  TASKDECK_CONNECTORS_ENCRYPTION_KEY: {
    Name: 'TASKDECK_CONNECTORS_ENCRYPTION_KEY',
    Required: true,
  },
  TASKDECK_JWT_SECRET: { Name: 'TASKDECK_JWT_SECRET', Required: true },
  TASKDECK_OPTIONAL_VALUE: { Name: 'TASKDECK_OPTIONAL_VALUE', Required: false },
}

const effectiveParserCompose = `
services:
  probe:
    image: busybox:latest
    environment:
      Jwt__SecretKey: \${TASKDECK_JWT_SECRET:?JWT secret is required}
      Connectors__EncryptionKey: \${TASKDECK_CONNECTORS_ENCRYPTION_KEY?connector key is required}
      Optional__Value: \${TASKDECK_OPTIONAL_VALUE:-safe-default}
      Escaped__Value: $\${TASKDECK_ESCAPED_REQUIRED:?escaped literals are inert}
# \${TASKDECK_COMMENTED_REQUIRED:?commented requirements are inert}
`

async function createTemporaryFiles(t, composeText = effectiveParserCompose) {
  const directory = await mkdtemp(join(tmpdir(), 'taskdeck-staging-inputs-'))
  t.after(() => rm(directory, { recursive: true, force: true }))

  const composePath = join(directory, 'compose.yml')
  const githubEnvPath = join(directory, 'github-env')
  await writeFile(composePath, composeText, 'utf8')
  await writeFile(githubEnvPath, '', 'utf8')
  return { composePath, githubEnvPath }
}

test('parses the effective Compose inventory and selects exactly the required variables', () => {
  const inventory = parseComposeVariableInventory(JSON.stringify(expectedVariableInventory))

  assert.deepEqual(inventory, [
    { name: 'TASKDECK_CONNECTORS_ENCRYPTION_KEY', required: true },
    { name: 'TASKDECK_JWT_SECRET', required: true },
    { name: 'TASKDECK_OPTIONAL_VALUE', required: false },
  ])
  assert.deepEqual(assertExpectedRequiredComposeVariables(inventory), [
    'TASKDECK_CONNECTORS_ENCRYPTION_KEY',
    'TASKDECK_JWT_SECRET',
  ])
})

test('rejects non-object Compose variable output', () => {
  for (const invalidJson of ['null', '[]', '"not-an-inventory"']) {
    assert.throws(
      () => parseComposeVariableInventory(invalidJson),
      /must return a JSON object/,
    )
  }
})

test('rejects non-object Compose variable metadata', () => {
  assert.throws(
    () =>
      parseComposeVariableInventory(
        JSON.stringify({ TASKDECK_JWT_SECRET: null }),
      ),
    /metadata for TASKDECK_JWT_SECRET must be an object/,
  )
})

test('rejects a Compose variable key and Name mismatch', () => {
  assert.throws(
    () =>
      parseComposeVariableInventory(
        JSON.stringify({
          TASKDECK_JWT_SECRET: { Name: 'TASKDECK_OTHER_SECRET', Required: true },
        }),
      ),
    /key\/Name mismatch/,
  )
})

test('rejects a non-boolean Compose Required value', () => {
  assert.throws(
    () =>
      parseComposeVariableInventory(
        JSON.stringify({
          TASKDECK_JWT_SECRET: { Name: 'TASKDECK_JWT_SECRET', Required: 'true' },
        }),
      ),
    /must declare boolean Required/,
  )
})

test('uses Docker Compose semantics for comments and escaped literals', async (t) => {
  const { composePath } = await createTemporaryFiles(t)
  const inventory = await inspectComposeVariables({ composePath })

  assert.equal(inventory.some(({ name }) => name === 'TASKDECK_COMMENTED_REQUIRED'), false)
  assert.equal(inventory.some(({ name }) => name === 'TASKDECK_ESCAPED_REQUIRED'), false)
  assert.deepEqual(assertExpectedRequiredComposeVariables(inventory), [
    'TASKDECK_CONNECTORS_ENCRYPTION_KEY',
    'TASKDECK_JWT_SECRET',
  ])
})

test('masks cryptographic job inputs before appending them to GITHUB_ENV', async (t) => {
  const { composePath, githubEnvPath } = await createTemporaryFiles(t)
  const requestedByteCounts = []
  const maskCommands = []
  const sideEffects = []
  const executeCalls = []

  const result = await prepareStagingComposeInputs({
    composePath,
    githubEnvPath,
    async executeCommand(...args) {
      executeCalls.push(args)
      return { stdout: JSON.stringify(expectedVariableInventory), stderr: '' }
    },
    randomBytesSource(byteCount) {
      requestedByteCounts.push(byteCount)
      return Buffer.alloc(byteCount, byteCount)
    },
    emitMaskCommand(command) {
      maskCommands.push(command)
      sideEffects.push('mask')
    },
    async appendEnvironmentFile(...args) {
      sideEffects.push('append-environment')
      await appendFile(...args)
    },
  })

  const expectedJwtSecret = Buffer.alloc(48, 48).toString('base64')
  const expectedConnectorKey = Buffer.alloc(32, 32).toString('base64')
  assert.deepEqual(executeCalls, [
    [
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
    ],
  ])
  assert.deepEqual(requestedByteCounts, [48, 32])
  assert.deepEqual(maskCommands, [
    `::add-mask::${expectedJwtSecret}\n`,
    `::add-mask::${expectedConnectorKey}\n`,
  ])
  assert.deepEqual(sideEffects, ['mask', 'mask', 'append-environment'])
  assert.equal(
    await readFile(githubEnvPath, 'utf8'),
    `TASKDECK_JWT_SECRET=${expectedJwtSecret}\n` +
      `TASKDECK_CONNECTORS_ENCRYPTION_KEY=${expectedConnectorKey}\n`,
  )
  assert.deepEqual(result.requiredVariables, [
    'TASKDECK_CONNECTORS_ENCRYPTION_KEY',
    'TASKDECK_JWT_SECRET',
  ])
})

test('fails closed before generating or writing when Compose adds a required input', async (t) => {
  const { composePath, githubEnvPath } = await createTemporaryFiles(t)
  const inventoryWithUnexpectedRequirement = {
    ...expectedVariableInventory,
    TASKDECK_NEW_REQUIRED: { Name: 'TASKDECK_NEW_REQUIRED', Required: true },
  }
  let randomCalls = 0
  const maskCommands = []

  await assert.rejects(
    prepareStagingComposeInputs({
      composePath,
      githubEnvPath,
      async executeCommand() {
        return {
          stdout: JSON.stringify(inventoryWithUnexpectedRequirement),
          stderr: '',
        }
      },
      randomBytesSource() {
        randomCalls += 1
        return Buffer.alloc(64)
      },
      emitMaskCommand(command) {
        maskCommands.push(command)
      },
    }),
    /unexpected \[TASKDECK_NEW_REQUIRED\]/,
  )

  assert.equal(randomCalls, 0)
  assert.deepEqual(maskCommands, [])
  assert.equal(await readFile(githubEnvPath, 'utf8'), '')
})

test('fails closed when an expected Compose requirement disappears', () => {
  const inventoryWithoutJwt = parseComposeVariableInventory(
    JSON.stringify({
      TASKDECK_CONNECTORS_ENCRYPTION_KEY:
        expectedVariableInventory.TASKDECK_CONNECTORS_ENCRYPTION_KEY,
    }),
  )

  assert.throws(
    () => assertExpectedRequiredComposeVariables(inventoryWithoutJwt),
    /missing \[TASKDECK_JWT_SECRET\]/,
  )
})

test('fails closed before side effects when Docker Compose inspection fails', async (t) => {
  const { composePath, githubEnvPath } = await createTemporaryFiles(t)
  let randomCalls = 0
  let appendCalls = 0
  const maskCommands = []

  await assert.rejects(
    prepareStagingComposeInputs({
      composePath,
      githubEnvPath,
      async executeCommand() {
        throw new Error('compose executable unavailable')
      },
      randomBytesSource() {
        randomCalls += 1
        return Buffer.alloc(64)
      },
      emitMaskCommand(command) {
        maskCommands.push(command)
      },
      async appendEnvironmentFile() {
        appendCalls += 1
      },
    }),
    /Docker Compose variable inspection failed: compose executable unavailable/,
  )

  assert.equal(randomCalls, 0)
  assert.equal(appendCalls, 0)
  assert.deepEqual(maskCommands, [])
  assert.equal(await readFile(githubEnvPath, 'utf8'), '')
})

test('fails closed before side effects when Docker Compose output is invalid JSON', async (t) => {
  const { composePath, githubEnvPath } = await createTemporaryFiles(t)
  let randomCalls = 0

  await assert.rejects(
    prepareStagingComposeInputs({
      composePath,
      githubEnvPath,
      async executeCommand() {
        return { stdout: 'not-json', stderr: '' }
      },
      randomBytesSource() {
        randomCalls += 1
        return Buffer.alloc(64)
      },
    }),
    /returned invalid JSON/,
  )

  assert.equal(randomCalls, 0)
  assert.equal(await readFile(githubEnvPath, 'utf8'), '')
})

test('requires the GitHub Actions environment file before generating secrets', async () => {
  let randomCalls = 0
  let executeCalls = 0

  await assert.rejects(
    prepareStagingComposeInputs({
      githubEnvPath: '',
      async executeCommand() {
        executeCalls += 1
        return { stdout: JSON.stringify(expectedVariableInventory), stderr: '' }
      },
      randomBytesSource() {
        randomCalls += 1
        return Buffer.alloc(64)
      },
    }),
    /GITHUB_ENV/,
  )
  assert.equal(randomCalls, 0)
  assert.equal(executeCalls, 0)
})
