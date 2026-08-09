import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const workflowPath = fileURLToPath(new URL('../../.github/workflows/ci-extended.yml', import.meta.url))
const malformedFixturePath = fileURLToPath(new URL('./fixtures/actionlint-malformed.yml', import.meta.url))
const externalLintersFixturePath = fileURLToPath(new URL('./fixtures/actionlint-external-linters.yml', import.meta.url))
const repoRoot = fileURLToPath(new URL('../../', import.meta.url))

const expectedVersion = '1.7.12'
const expectedArchive = 'actionlint_1.7.12_linux_amd64.tar.gz'
const expectedChecksum = '8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8'
const expectedUrl = `https://github.com/rhysd/actionlint/releases/download/v${expectedVersion}/${expectedArchive}`
const expectedPyflakesVersion = '3.4.0'
const expectedPyflakesWheel = 'pyflakes-3.4.0-py2.py3-none-any.whl'
const expectedPyflakesChecksum = 'f742a7dbd0d9cb9ea41e9a24a918996e8170c799fa528688d40dd582c8265f4f'
const expectedPyflakesUrl = `https://files.pythonhosted.org/packages/c2/2f/81d580a0fb83baeb066698975cb14a618bdbed7720678566f1b046a95fe8/${expectedPyflakesWheel}`

async function loadWorkflow() {
  return readFile(workflowPath, 'utf8')
}

function requiredToolPath(variableName) {
  const toolPath = process.env[variableName]
  assert.ok(toolPath, `${variableName} must point to the installed tool`)
  return toolPath
}

function runActionlint(fixturePath) {
  return spawnSync(
    requiredToolPath('ACTIONLINT_BIN'),
    [
      '-shellcheck', requiredToolPath('ACTIONLINT_SHELLCHECK_BIN'),
      '-pyflakes', requiredToolPath('ACTIONLINT_PYFLAKES_BIN'),
      fixturePath,
    ],
    { encoding: 'utf8' },
  )
}

function runChecksumVerifier(expectedChecksum) {
  const bash = process.platform === 'win32'
    ? requiredToolPath('BASH_BIN')
    : process.env.BASH_BIN || 'bash'

  return spawnSync(
    bash,
    [
      'scripts/ci/verify-sha256.sh',
      expectedChecksum,
      'scripts/ci/fixtures/actionlint-malformed.yml',
    ],
    { cwd: repoRoot, encoding: 'utf8' },
  )
}

test('pins the Actionlint Linux release contract without the Docker action', async () => {
  const workflow = await loadWorkflow()
  const versionMatch = workflow.match(/^\s+ACTIONLINT_VERSION:\s+"([^"]+)"$/m)
  const archiveMatch = workflow.match(/^\s+archive_name="([^"]+)"$/m)
  const urlMatch = workflow.match(/^\s+download_url="([^"]+)"$/m)

  assert.ok(versionMatch, 'Missing pinned Actionlint version')
  assert.ok(archiveMatch, 'Missing pinned Actionlint archive template')
  assert.ok(urlMatch, 'Missing pinned Actionlint download URL template')
  assert.ok(workflow.includes(`ACTIONLINT_ARCHIVE_SHA256: ${expectedChecksum}`))

  const version = versionMatch[1]
  const archive = archiveMatch[1].replace('${ACTIONLINT_VERSION}', version)
  const downloadUrl = urlMatch[1]
    .replace('${ACTIONLINT_VERSION}', version)
    .replace('${archive_name}', archive)

  assert.equal(version, expectedVersion)
  assert.equal(archive, expectedArchive)
  assert.equal(downloadUrl, expectedUrl)
  assert.doesNotMatch(workflow, /^\s*uses:\s*rhysd\/actionlint@/m)
})

test('pins the Pyflakes wheel and installs it without an index lookup', async () => {
  const workflow = await loadWorkflow()
  const versionMatch = workflow.match(/^\s+PYFLAKES_VERSION:\s+"([^"]+)"$/m)
  const wheelMatch = workflow.match(/^\s+pyflakes_wheel_name="([^"]+)"$/m)
  const urlMatch = workflow.match(/^\s+pyflakes_download_url="([^"]+)"$/m)

  assert.ok(versionMatch, 'Missing pinned Pyflakes version')
  assert.ok(wheelMatch, 'Missing pinned Pyflakes wheel template')
  assert.ok(urlMatch, 'Missing pinned Pyflakes URL template')
  assert.ok(workflow.includes(`PYFLAKES_WHEEL_SHA256: ${expectedPyflakesChecksum}`))

  const version = versionMatch[1]
  const wheel = wheelMatch[1].replace('${PYFLAKES_VERSION}', version)
  const downloadUrl = urlMatch[1].replace('${pyflakes_wheel_name}', wheel)

  assert.equal(version, expectedPyflakesVersion)
  assert.equal(wheel, expectedPyflakesWheel)
  assert.equal(downloadUrl, expectedPyflakesUrl)
  assert.match(workflow, /-m pip install[\s\S]*?--no-deps \\\r?\n\s+--no-index/)
  assert.ok(workflow.includes('bash scripts/ci/verify-sha256.sh "${PYFLAKES_WHEEL_SHA256}" "${pyflakes_wheel_path}"'))
})

test('keeps checkout and every bootstrap boundary fail closed before linting', async () => {
  const workflow = await loadWorkflow()
  const bootstrapMatch = workflow.match(
    /- name: Install Actionlint toolchain[\s\S]*?(?=\r?\n\s+- name: Test Actionlint bootstrap contract)/,
  )
  assert.ok(bootstrapMatch, 'Missing Actionlint bootstrap step')
  const bootstrap = bootstrapMatch[0]
  const orderedMarkers = [
    '- name: Checkout',
    '- name: Install Actionlint toolchain',
    '--fail',
    'bash scripts/ci/verify-sha256.sh "${ACTIONLINT_ARCHIVE_SHA256}" "${archive_path}"',
    'tar --extract --gzip',
    'version_output=',
    'bash scripts/ci/verify-sha256.sh "${PYFLAKES_WHEEL_SHA256}" "${pyflakes_wheel_path}"',
    'python3 -m venv',
    'pyflakes_version_output=',
    '- name: Test Actionlint bootstrap contract',
    '- name: Run actionlint',
    'git rev-parse --verify HEAD',
    '"${ACTIONLINT_BIN}"',
  ]

  let previousIndex = -1
  for (const marker of orderedMarkers) {
    const markerIndex = workflow.indexOf(marker)
    assert.ok(markerIndex > previousIndex, `Expected workflow marker in order: ${marker}`)
    previousIndex = markerIndex
  }

  assert.match(workflow, /- name: Install Actionlint toolchain[\s\S]*?set -euo pipefail/)
  assert.match(workflow, /workflow-lint:[\s\S]*?timeout-minutes: 10/)
  assert.match(workflow, /- name: Checkout\r?\n\s+uses: actions\/checkout@v7\r?\n\s+with:\r?\n\s+persist-credentials: false/)
  assert.match(workflow, /--output "\$\{archive_path\}" \\\r?\n\s+"\$\{download_url\}"/)
  assert.match(workflow, /--output "\$\{pyflakes_wheel_path\}" \\\r?\n\s+"\$\{pyflakes_download_url\}"/)
  assert.ok(workflow.includes('bash scripts/ci/verify-sha256.sh "${ACTIONLINT_ARCHIVE_SHA256}" "${archive_path}"'))
  assert.ok(workflow.includes('bash scripts/ci/verify-sha256.sh "${PYFLAKES_WHEEL_SHA256}" "${pyflakes_wheel_path}"'))
  assert.ok(workflow.includes('tar --extract --gzip --file "${archive_path}" --directory "${install_dir}" actionlint'))
  assert.ok(workflow.includes('if [ ! -f "${actionlint_bin}" ] || [ -L "${actionlint_bin}" ]; then'))
  assert.ok(workflow.includes('version_output="$("${actionlint_bin}" -version)"'))
  assert.ok(workflow.includes('if [ "${actual_version}" != "${ACTIONLINT_VERSION}" ]; then'))
  assert.ok(workflow.includes('pyflakes_version_output="$("${pyflakes_bin}" --version)"'))

  for (const boundedCurlFlag of [
    '--connect-timeout 15',
    '--max-time 120',
    '--retry 3',
    '--retry-all-errors',
    '--retry-max-time 240',
  ]) {
    assert.equal(bootstrap.split(boundedCurlFlag).length - 1, 2, `Expected both downloads to use ${boundedCurlFlag}`)
  }

  assert.ok(workflow.includes("checkout_head=\"$(git rev-parse --verify HEAD)\""))
  assert.ok(workflow.includes('workflow_count="$(find .github/workflows'))
  assert.ok(workflow.includes("printf 'Checked out HEAD: %s\\nWorkflow files discovered: %s\\n'"))
  assert.match(
    workflow,
    /"\$\{ACTIONLINT_BIN\}" \\\r?\n\s+-color \\\r?\n\s+-verbose \\\r?\n\s+-shellcheck "\$\{ACTIONLINT_SHELLCHECK_BIN\}" \\\r?\n\s+-pyflakes "\$\{ACTIONLINT_PYFLAKES_BIN\}"/,
  )
})

test('checksum verifier accepts the expected digest', async () => {
  const fixture = await readFile(malformedFixturePath)
  const expectedChecksum = createHash('sha256').update(fixture).digest('hex')
  const result = runChecksumVerifier(expectedChecksum)

  assert.ifError(result.error)
  assert.equal(result.status, 0, result.stderr)
  assert.match(result.stdout, /actionlint-malformed\.yml: OK/)
})

test('checksum verifier rejects a corrupt digest', () => {
  const result = runChecksumVerifier('0'.repeat(64))

  assert.ifError(result.error)
  assert.equal(result.status, 1, 'Corrupt checksum unexpectedly passed verification')
  assert.match(`${result.stdout}\n${result.stderr}`, /actionlint-malformed\.yml: FAILED/)
})

test('rejects a malformed workflow fixture with the installed Actionlint binary', () => {
  const result = runActionlint(malformedFixturePath)

  assert.ifError(result.error)
  assert.equal(result.status, 1, 'Malformed workflow did not produce Actionlint exit 1')
  const output = `${result.stdout}\n${result.stderr}`
  assert.match(output, /actionlint-malformed\.yml/)
  assert.match(output, /"runs-on" section is missing in job "malformed"/)
})

test('runs ShellCheck and Pyflakes through explicit Actionlint paths', () => {
  const result = runActionlint(externalLintersFixturePath)

  assert.ifError(result.error)
  assert.equal(result.status, 1, 'External linter fixture did not produce Actionlint exit 1')
  const output = `${result.stdout}\n${result.stderr}`
  assert.match(output, /SC2086/)
  assert.match(output, /undefined name 'undefined_name'.*\[pyflakes\]/)
})
