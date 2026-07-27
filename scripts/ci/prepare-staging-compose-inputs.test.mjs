import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { appendFile, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

import {
  assertExpectedRequiredComposeVariables,
  findRequiredComposeVariables,
  prepareStagingComposeInputs,
} from './prepare-staging-compose-inputs.mjs'

const expectedComposeInputs = `
services:
  api:
    environment:
      Jwt__SecretKey: \${TASKDECK_JWT_SECRET:?JWT secret is required}
      Connectors__EncryptionKey: \${TASKDECK_CONNECTORS_ENCRYPTION_KEY?connector key is required}
      Optional__Value: \${TASKDECK_OPTIONAL_VALUE:-safe-default}
`

const canonicalComposeInputs = readFileSync(
  new URL('../../deploy/docker-compose.yml', import.meta.url),
  'utf8',
)

async function createTemporaryFiles(t, composeText = expectedComposeInputs) {
  const directory = await mkdtemp(join(tmpdir(), 'taskdeck-staging-inputs-'))
  t.after(() => rm(directory, { recursive: true, force: true }))

  const composePath = join(directory, 'compose.yml')
  const githubEnvPath = join(directory, 'github-env')
  await writeFile(composePath, composeText, 'utf8')
  await writeFile(githubEnvPath, '', 'utf8')
  return { composePath, githubEnvPath }
}

test('finds exactly the required Compose interpolation forms', () => {
  assert.deepEqual(findRequiredComposeVariables(expectedComposeInputs), [
    'TASKDECK_CONNECTORS_ENCRYPTION_KEY',
    'TASKDECK_JWT_SECRET',
  ])
  assert.deepEqual(assertExpectedRequiredComposeVariables(expectedComposeInputs), [
    'TASKDECK_CONNECTORS_ENCRYPTION_KEY',
    'TASKDECK_JWT_SECRET',
  ])
})

test('keeps the canonical Compose required-variable inventory exact', () => {
  assert.deepEqual(assertExpectedRequiredComposeVariables(canonicalComposeInputs), [
    'TASKDECK_CONNECTORS_ENCRYPTION_KEY',
    'TASKDECK_JWT_SECRET',
  ])
})

test('masks cryptographic job inputs before appending them to GITHUB_ENV', async (t) => {
  const { composePath, githubEnvPath } = await createTemporaryFiles(t)
  const requestedByteCounts = []
  const maskCommands = []
  const sideEffects = []

  const result = await prepareStagingComposeInputs({
    composePath,
    githubEnvPath,
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
  const composeWithUnexpectedRequirement = `${expectedComposeInputs}
      New__Value: \${TASKDECK_NEW_REQUIRED:?new value is required}
`
  const { composePath, githubEnvPath } = await createTemporaryFiles(
    t,
    composeWithUnexpectedRequirement,
  )
  let randomCalls = 0
  const maskCommands = []

  await assert.rejects(
    prepareStagingComposeInputs({
      composePath,
      githubEnvPath,
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

test('fails closed when an expected Compose requirement disappears', async () => {
  assert.throws(
    () =>
      assertExpectedRequiredComposeVariables(
        expectedComposeInputs.replace(
          '      Jwt__SecretKey: \${TASKDECK_JWT_SECRET:?JWT secret is required}\n',
          '',
        ),
      ),
    /missing \[TASKDECK_JWT_SECRET\]/,
  )
})

test('requires the GitHub Actions environment file before generating secrets', async () => {
  let randomCalls = 0

  await assert.rejects(
    prepareStagingComposeInputs({
      githubEnvPath: '',
      randomBytesSource() {
        randomCalls += 1
        return Buffer.alloc(64)
      },
    }),
    /GITHUB_ENV/,
  )
  assert.equal(randomCalls, 0)
})
