import assert from 'node:assert/strict'
import { spawn, spawnSync } from 'node:child_process'
import { closeSync, existsSync, openSync, readFileSync } from 'node:fs'
import {
  chmod,
  copyFile,
  mkdir,
  mkdtemp,
  readFile,
  rm,
  writeFile,
} from 'node:fs/promises'
import net from 'node:net'
import { tmpdir } from 'node:os'
import { delimiter, join } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const repoRoot = fileURLToPath(new URL('../../', import.meta.url))
const powershellLauncher = join(repoRoot, 'scripts', 'dev-up.ps1')
const bashLauncher = join(repoRoot, 'scripts', 'dev-up.sh')
const frontendPackage = join(repoRoot, 'frontend', 'taskdeck-web', 'package.json')
const trackedNodeVersion = join(repoRoot, '.nvmrc')
const RESET_CYCLE_TEARDOWN_TIMEOUT_MS = 45_000
const powershell =
  process.platform === 'win32'
    ? join(
        process.env.SystemRoot || 'C:\\Windows',
        'System32',
        'WindowsPowerShell',
        'v1.0',
        'powershell.exe',
      )
    : null

const syntheticPackage = {
  name: 'taskdeck-dev-up-fixture',
  private: true,
  dependencies: {
    'existing-direct-dependency': '1.0.0',
    'newly-locked-direct-dependency': '1.0.0',
  },
}

const syntheticLock = {
  name: syntheticPackage.name,
  lockfileVersion: 3,
  requires: true,
  packages: {
    '': { name: syntheticPackage.name, dependencies: syntheticPackage.dependencies },
    'node_modules/existing-direct-dependency': { version: '1.0.0' },
    'node_modules/newly-locked-direct-dependency': { version: '1.0.0' },
  },
}

const helperSource = String.raw`
import fs from 'node:fs'
import http from 'node:http'
import path from 'node:path'
import { spawn } from 'node:child_process'

const [, , kind, ...args] = process.argv
const appendEvent = (event) => {
  if (!process.env.TASKDECK_NPM_LOG) return
  fs.appendFileSync(process.env.TASKDECK_NPM_LOG, JSON.stringify({
    kind,
    args,
    runId: process.env.TASKDECK_DEV_RUN_ID ?? null,
    taskdeckApiBaseUrl: process.env.TASKDECK_API_BASE_URL ?? null,
    viteApiBaseUrl: process.env.VITE_API_BASE_URL ?? null,
    pid: process.pid,
  }) + '\n')
}

const stopWithServer = (server) => {
  const stopDelayMs = Number(process.env.FAKE_STOP_DELAY_MS ?? 0)
  const stop = () => server.close(() => {
    if (Number.isFinite(stopDelayMs) && stopDelayMs > 0) {
      setTimeout(() => process.exit(0), stopDelayMs)
      return
    }
    process.exit(0)
  })
  process.on('SIGTERM', stop)
  process.on('SIGINT', stop)
}

if (kind === 'linger') {
  const stopDelayMs = Number(process.env.FAKE_STOP_DELAY_MS ?? 0)
  const stop = () => setTimeout(() => process.exit(0), stopDelayMs)
  process.on('SIGTERM', stop)
  process.on('SIGINT', stop)
  setInterval(() => {}, 1000)
} else if (kind === 'npm') {
  appendEvent({})
  if (args[0] === 'ci') {
    const mode = process.env.FAKE_NPM_CI_MODE ?? 'success'
    if (mode === 'hang') {
      while (!fs.existsSync(process.env.FAKE_NPM_RELEASE_FILE)) {
        await new Promise((resolve) => setTimeout(resolve, 50))
      }
      process.exit(46)
    }
    if (mode === 'fail') process.exit(42)
    fs.rmSync(path.join(process.cwd(), 'node_modules'), { recursive: true, force: true })
    fs.mkdirSync(path.join(process.cwd(), 'node_modules', 'existing-direct-dependency'), { recursive: true })
    fs.mkdirSync(path.join(process.cwd(), 'node_modules', 'newly-locked-direct-dependency'), { recursive: true })
    process.exit(0)
  }
  if (args[0] === 'run' && args[1] === 'demo:seed') {
    process.exit(process.env.FAKE_SEED_MODE === 'fail' ? 43 : 0)
  }
  if (args[0] !== 'run' || args[1] !== 'dev') process.exit(91)

  if (process.env.FAKE_STOP_DESCENDANT === '1') {
    spawn(process.execPath, [process.argv[1], 'linger'], {
      env: process.env,
      stdio: 'ignore',
    })
  }

  let port = Number(process.env.FAKE_FRONTEND_PORT)
  const mode = process.env.FAKE_FRONTEND_MODE ?? 'success'
  if (mode === 'transform-failure') {
    console.error('[dev] Taskdeck entry graph transform failed at "/src/main.ts"')
    process.exit(44)
  }
  const server = http.createServer((_request, response) => {
    response.writeHead(200, { 'content-type': 'text/html' })
    response.end('<!doctype html><title>Taskdeck</title><script type="module" src="/src/main.ts"></script>', () => {
      if (mode === 'exit-after-entry-response') server.close(() => process.exit(0))
    })
  })
  stopWithServer(server)
  const reportReady = () => {
    const markerPort = mode === 'spoof' ? Number(process.env.FAKE_SPOOF_PORT) : port
    const marker = 'TASKDECK_DEV_FRONTEND_READY ' + JSON.stringify({ schemaVersion: 1, url: 'http://localhost:' + markerPort + '/', port: markerPort })
    if (mode === 'high-volume') {
      for (let index = 0; index < 4000; index++) {
        console.log('stdout-' + index + '-' + 'x'.repeat(120))
        console.error('stderr-' + index + '-' + 'y'.repeat(120))
      }
      console.log(marker)
    } else if (mode === 'stderr-marker') {
      console.error(marker)
    } else if (mode === 'malformed') {
      console.log('TASKDECK_DEV_FRONTEND_READY {"schemaVersion":1')
    } else if (mode === 'duplicate-property') {
      console.log('TASKDECK_DEV_FRONTEND_READY {"schemaVersion":1,"url":"http://localhost:' + port + '/","url":"http://localhost:' + port + '/","port":' + port + '}')
    } else if (mode === 'duplicate') {
      console.log(marker)
      console.log(marker)
    } else if (mode === 'late') {
      setTimeout(() => console.log(marker), 1600)
    } else if (mode === 'missing') {
      console.log('[dev] listening without readiness marker')
    } else {
      console.log(marker)
    }
  }
  const listen = () => {
    const onListening = () => {
      server.removeListener('error', onError)
      reportReady()
    }
    const onError = (error) => {
      server.removeListener('listening', onListening)
      if (mode === 'fallback' && error.code === 'EADDRINUSE' && port < 65535) {
        port += 1
        listen()
        return
      }
      throw error
    }
    server.once('error', onError)
    server.once('listening', onListening)
    server.listen(port, 'localhost')
  }
  listen()
} else if (kind === 'dotnet') {
  appendEvent({})
  const mode = process.env.FAKE_API_MODE ?? 'ready'
  const proofMode = process.env.FAKE_API_PROOF_MODE ?? 'match'
  const expectedRunId = process.env.TASKDECK_DEV_RUN_ID ?? ''
  const mismatchRunId = '11111111-1111-4111-8111-111111111111'
  let readyRequestCount = 0
  if (mode === 'exit') {
    console.error('synthetic API exit')
    process.exit(45)
  }
  const urlsIndex = args.indexOf('--urls')
  if (urlsIndex < 0) process.exit(92)
  const port = Number(new URL(args[urlsIndex + 1]).port)
  let scheduledExit = false
  const seedHasRun = () => {
    try {
      return fs.readFileSync(process.env.TASKDECK_NPM_LOG, 'utf8')
        .trim()
        .split('\n')
        .filter(Boolean)
        .map((line) => JSON.parse(line))
        .some((event) => event.args?.join(' ') === 'run demo:seed')
    } catch {
      return false
    }
  }
  const finalStateHasBeenWritten = () => {
    try {
      return JSON.parse(fs.readFileSync(process.env.TASKDECK_TEST_STATE_FILE, 'utf8')).frontend !== null
    } catch {
      return false
    }
  }
  const runIdHeaderValues = () => {
    readyRequestCount += 1
    if (proofMode === 'missing') return []
    if (proofMode === 'mismatch') return [mismatchRunId]
    if (proofMode === 'duplicate') return [expectedRunId, expectedRunId]
    if (proofMode === 'flip-after-first-valid' && readyRequestCount > 1) return [mismatchRunId]
    if (proofMode === 'flip-after-seed' && seedHasRun()) return [mismatchRunId]
    if (proofMode === 'flip-after-final-state' && finalStateHasBeenWritten()) return [mismatchRunId]
    return [expectedRunId]
  }
  const server = http.createServer((request, response) => {
    if (request.url === '/health/ready' && (mode === 'ready' || mode === 'exit-after-ready')) {
      const runIdValues = runIdHeaderValues()
      if (runIdValues.length > 0) {
        response.setHeader('Taskdeck-Dev-Run-Id', runIdValues.length === 1 ? runIdValues[0] : runIdValues)
        response.setHeader('Cache-Control', 'no-store')
      }
      response.writeHead(200)
      response.end('ready', () => {
        if (mode === 'exit-after-ready' && !scheduledExit) {
          scheduledExit = true
          setTimeout(() => server.close(() => process.exit(0)), 250)
        }
      })
    } else {
      response.writeHead(503)
      response.end('not-ready')
    }
  })
  stopWithServer(server)
  server.listen(port, 'localhost')
} else {
  process.exit(93)
}
`

const interruptHarnessSource = String.raw`
param(
  [Parameter(Mandatory = $true)][string]$Launcher,
  [Parameter(Mandatory = $true)][int]$ApiPort,
  [Parameter(Mandatory = $true)][string]$StateFile
)
$ErrorActionPreference = 'Stop'
$job = Start-Job -ScriptBlock {
  param($LauncherPath, $SelectedApiPort)
  & $LauncherPath -ApiPort $SelectedApiPort
} -ArgumentList $Launcher, $ApiPort
try {
  $deadline = [DateTime]::UtcNow.AddSeconds(10)
  while (-not (Test-Path -LiteralPath $StateFile -PathType Leaf)) {
    if ($job.State -ne 'Running') {
      Receive-Job -Job $job -ErrorAction SilentlyContinue | Out-String | Write-Output
      throw 'Launcher exited before transactional state was written.'
    }
    if ([DateTime]::UtcNow -ge $deadline) { throw 'Timed out waiting for transactional state.' }
    Start-Sleep -Milliseconds 50
  }
  Stop-Job -Job $job
  Wait-Job -Job $job | Out-Null
  Receive-Job -Job $job -ErrorAction SilentlyContinue | Out-String | Write-Output
  if (Test-Path -LiteralPath $StateFile) { throw 'Pipeline cancellation retained state after proven cleanup.' }
} finally {
  if ($job.State -eq 'Running') { Stop-Job -Job $job -ErrorAction SilentlyContinue }
  Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
}
`

function normalise(text) {
  return text.replaceAll('\r\n', '\n')
}

function extractBashFunction(source, name) {
  const start = source.indexOf(`${name}() {`)
  assert.notEqual(start, -1, `missing Bash function ${name}`)
  const end = source.indexOf('\n}\n', start)
  assert.notEqual(end, -1, `unterminated Bash function ${name}`)
  return source.slice(start, end + 2)
}

// Git Bash launches native Windows Node in CI. A SIGTERM can terminate that process without
// invoking a JavaScript SIGTERM listener, so this seam proves the Bash cleanup contract through
// its identity probe instead of inferring signal handling from elapsed wall-clock time.
async function assertBashTermKillEscalation(fixture) {
  const source = normalise(await readFile(join(fixture.scriptsDir, 'dev-up.sh'), 'utf8'))
  const harness = join(fixture.root, 'term-kill-seam.sh')
  const signalLog = join(fixture.root, 'term-kill-seam.log')
  const probeState = join(fixture.root, 'term-kill-seam.state')
  const observedState = join(fixture.root, 'term-kill-seam.observed')
  await writeFile(
    harness,
    `#!/usr/bin/env bash
set -euo pipefail
term_sent=0
kill_sent=0
warn() { :; }
sleep() { :; }
process_identity_status() {
  if [[ "$term_sent" -eq 1 ]]; then printf '1\\n' > "$OBSERVED_STATE"; fi
  if [[ "$kill_sent" -eq 1 ]]; then printf 'missing\\n'; else printf 'match\\n'; fi
}
kill() {
  case "$1" in
    -TERM) term_sent=1 ;;
    -KILL) kill_sent=1 ;;
  esac
  printf '%s\\n' "$1" >> "$SIGNAL_LOG"
  return 0
}
${extractBashFunction(source, 'wait_for_identity_exit')}
${extractBashFunction(source, 'stop_exact_process')}
stop_exact_process api 4242 node recorded-token
printf '%s\\n' "$term_sent" "$kill_sent" > "$PROBE_STATE"
`,
  )
  const result = spawnSync(bash, [toPosixPath(harness)], {
    encoding: 'utf8',
    timeout: 5000,
    windowsHide: true,
    env: {
      ...process.env,
      SIGNAL_LOG: toPosixPath(signalLog),
      PROBE_STATE: toPosixPath(probeState),
      OBSERVED_STATE: toPosixPath(observedState),
    },
  })
  assert.ifError(result.error)
  assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`)
  assert.deepEqual((await readFile(probeState, 'utf8')).trim().split(/\r?\n/), ['1', '1'])
  assert.equal((await readFile(observedState, 'utf8')).trim(), '1')
  assert.deepEqual((await readFile(signalLog, 'utf8')).trim().split(/\r?\n/), ['-TERM', '-KILL'])
}

function toPosixPath(path) {
  if (process.platform !== 'win32') return path
  return path
    .replace(/^([A-Za-z]):/, (_, drive) => `/${drive.toLowerCase()}`)
    .replaceAll('\\', '/')
}

function findBash() {
  if (process.env.BASH_BIN) return process.env.BASH_BIN
  if (process.platform === 'win32') {
    const gitBash = 'C:\\Program Files\\Git\\bin\\bash.exe'
    return existsSync(gitBash) ? gitBash : null
  }
  const probe = spawnSync('bash', ['--version'], { encoding: 'utf8' })
  return probe.status === 0 ? 'bash' : null
}

const bash = findBash()
const platforms = [
  ...(powershell ? [{ name: 'PowerShell', launcher: 'dev-up.ps1' }] : []),
  ...(bash ? [{ name: 'Bash', launcher: 'dev-up.sh' }] : []),
]

async function readOptional(path) {
  try {
    return await readFile(path, 'utf8')
  } catch (error) {
    if (error?.code === 'ENOENT') return null
    throw error
  }
}

async function removeFixture(fixture) {
  await rm(fixture.root, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 })
}

async function createFixture(platform) {
  const root = await mkdtemp(join(tmpdir(), `taskdeck-dev-up-${platform.name.toLowerCase()}-`))
  const scriptsDir = join(root, 'scripts')
  const frontendDir = join(root, 'frontend', 'taskdeck-web')
  const fakeBin = join(root, 'fake-bin')
  const helper = join(root, 'fixture-helper.mjs')
  const interruptHarness = join(root, 'interrupt-launcher.ps1')
  const dataRoot = join(root, 'data')
  const launcherPath = join(scriptsDir, platform.launcher)

  await mkdir(scriptsDir, { recursive: true })
  await mkdir(join(frontendDir, 'node_modules', 'existing-direct-dependency'), { recursive: true })
  await mkdir(join(root, 'backend', 'src', 'Taskdeck.Api'), { recursive: true })
  await mkdir(fakeBin, { recursive: true })
  await copyFile(platform.launcher.endsWith('.ps1') ? powershellLauncher : bashLauncher, launcherPath)
  await writeFile(join(frontendDir, 'package.json'), `${JSON.stringify(syntheticPackage, null, 2)}\n`)
  await writeFile(join(frontendDir, 'package-lock.json'), `${JSON.stringify(syntheticLock, null, 2)}\n`)
  await writeFile(join(root, 'backend', 'src', 'Taskdeck.Api', 'Taskdeck.Api.csproj'), '<Project />\n')
  await writeFile(helper, helperSource)
  if (platform.name === 'PowerShell') {
    await writeFile(interruptHarness, interruptHarnessSource)
    await installPowerShellStubs(fakeBin)
  }
  else await installBashStubs(fakeBin)

  const stateDir =
    platform.name === 'PowerShell' ? join(dataRoot, 'Taskdeck') : join(dataRoot, 'taskdeck')
  return {
    root,
    scriptsDir,
    frontendDir,
    fakeBin,
    helper,
    interruptHarness,
    dataRoot,
    stateDir,
    stateFile: join(stateDir, 'dev-up.pids'),
    operationLock:
      platform.name === 'PowerShell'
        ? join(stateDir, 'dev-up.operation.lock')
        : join(stateDir, 'dev-up.operation.lock'),
    npmLog: join(root, 'events.jsonl'),
    npmReleaseFile: join(root, '.release-npm-ci'),
  }
}

async function installPostTaskkillUnknownProbe(fixture) {
  const launcherPath = join(fixture.scriptsDir, 'dev-up.ps1')
  const source = await readFile(launcherPath, 'utf8')
  const identityFunction = 'function Get-ProcessIdentityStatus {'
  const nextFunction = 'function Assert-ProcessIdentityMatch {'
  assert.equal(source.split(identityFunction).length - 1, 1, 'unexpected identity-function count')
  assert.equal(source.split(nextFunction).length - 1, 1, 'unexpected identity-assertion count')

  const instrumented = source
    .replace(identityFunction, 'function Get-RealProcessIdentityStatus {')
    .replace(
      nextFunction,
      String.raw`$script:IdentityProbeCounts = @{}
function Get-ProcessIdentityStatus {
    param($Record)
    $key = [string]$Record.Pid
    $actual = Get-RealProcessIdentityStatus -Record $Record
    if (-not $script:IdentityProbeCounts.ContainsKey($key)) { $script:IdentityProbeCounts[$key] = 0 }
    $script:IdentityProbeCounts[$key] = [int]$script:IdentityProbeCounts[$key] + 1
    if ([int]$script:IdentityProbeCounts[$key] -eq 2) {
        Write-Host '[dev-up-test] Forced transient post-taskkill identity: Unknown'
        return 'Unknown'
    }
    return $actual
}

function Assert-ProcessIdentityMatch {`,
    )
  await writeFile(launcherPath, instrumented)
}

async function installPowerShellStubs(fakeBin) {
  await writeFile(
    join(fakeBin, 'node.cmd'),
    [
      '@echo off',
      'if "%~1"=="-p" (',
      '  echo %FAKE_NODE_VERSION%',
      '  exit /b %FAKE_NODE_VERSION_EXIT%',
      ')',
      '"%TASKDECK_REAL_NODE%" %*',
      '',
    ].join('\r\n'),
  )
  await writeFile(
    join(fakeBin, 'npm.cmd'),
    '@echo off\r\n"%TASKDECK_REAL_NODE%" "%TASKDECK_HELPER%" npm %*\r\n',
  )
  await writeFile(
    join(fakeBin, 'dotnet.cmd'),
    '@echo off\r\n"%TASKDECK_REAL_NODE%" "%TASKDECK_HELPER%" dotnet %*\r\n',
  )
}

async function installBashStubs(fakeBin) {
  const stubs = {
    node: String.raw`#!/usr/bin/env bash
if [[ "$1" == "-p" ]]; then
  printf '%s\n' "$FAKE_NODE_VERSION"
  exit "$FAKE_NODE_VERSION_EXIT"
fi
exec "$TASKDECK_REAL_NODE" "$@"
`,
    npm: String.raw`#!/usr/bin/env bash
exec "$TASKDECK_REAL_NODE" "$TASKDECK_HELPER" npm "$@"
`,
    dotnet: String.raw`#!/usr/bin/env bash
exec "$TASKDECK_REAL_NODE" "$TASKDECK_HELPER" dotnet "$@"
`,
  }
  for (const [name, source] of Object.entries(stubs)) {
    const target = join(fakeBin, name)
    await writeFile(target, source)
    await chmod(target, 0o755)
  }
}

async function getFreePort() {
  const server = net.createServer()
  await new Promise((resolve, reject) => {
    server.once('error', reject)
    server.listen(0, '127.0.0.1', resolve)
  })
  const { port } = server.address()
  await new Promise((resolve) => server.close(resolve))
  return port
}

async function canBind(port, host = 'localhost') {
  const server = net.createServer()
  try {
    await new Promise((resolve, reject) => {
      server.once('error', reject)
      server.listen(port, host, resolve)
    })
    return true
  } catch {
    return false
  } finally {
    if (server.listening) await new Promise((resolve) => server.close(resolve))
  }
}

async function listenForeign(host = '127.0.0.1') {
  const server = net.createServer()
  await new Promise((resolve, reject) => {
    server.once('error', reject)
    server.listen(0, host, resolve)
  })
  return server
}

function fixtureEnvironment(platform, fixture, overrides = {}) {
  const common = {
    ...process.env,
    FAKE_NODE_VERSION: '24.13.1',
    FAKE_NODE_VERSION_EXIT: '0',
    FAKE_NPM_CI_MODE: 'success',
    FAKE_API_MODE: 'ready',
    FAKE_FRONTEND_MODE: 'success',
    TASKDECK_DEV_API_READY_TIMEOUT_SECONDS: '3',
    TASKDECK_DEV_FRONTEND_READY_TIMEOUT_SECONDS: '3',
    TASKDECK_DEV_MARKER_SETTLE_SECONDS: '1',
    TASKDECK_REAL_NODE: process.execPath,
    TASKDECK_HELPER: fixture.helper,
    TASKDECK_NPM_LOG: fixture.npmLog,
    TASKDECK_TEST_STATE_FILE: fixture.stateFile,
    FAKE_NPM_RELEASE_FILE: fixture.npmReleaseFile,
    ...overrides,
  }
  if (platform.name === 'PowerShell') {
    return {
      ...common,
      PATH: `${fixture.fakeBin}${delimiter}${process.env.PATH || ''}`,
      LOCALAPPDATA: fixture.dataRoot,
    }
  }
  return {
    ...common,
    PATH: `${toPosixPath(fixture.fakeBin)}:/usr/local/bin:/usr/bin:/bin`,
    XDG_DATA_HOME: toPosixPath(fixture.dataRoot),
    TASKDECK_REAL_NODE: toPosixPath(process.execPath),
    TASKDECK_HELPER: toPosixPath(fixture.helper),
    TASKDECK_NPM_LOG: toPosixPath(fixture.npmLog),
    FAKE_NPM_RELEASE_FILE: toPosixPath(fixture.npmReleaseFile),
  }
}

function launcherInvocation(platform, fixture, { apiPort, seed = false, resetSeed = false, stop = false } = {}) {
  if (platform.name === 'PowerShell') {
    const args = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', fixture.launcherPath ?? join(fixture.scriptsDir, 'dev-up.ps1')]
    if (apiPort) args.push('-ApiPort', String(apiPort))
    if (seed) args.push('-Seed')
    if (resetSeed) args.push('-ResetSeed')
    if (stop) args.push('-Stop')
    return { command: powershell, args }
  }
  const args = ['scripts/dev-up.sh']
  if (seed) args.push('--seed')
  if (resetSeed) args.push('--reset-seed')
  if (stop) args.push('--stop')
  return { command: bash, args }
}

function runLauncher(platform, fixture, options = {}) {
  const { apiPort, env = {}, timeout = 20_000 } = options
  const invocation = launcherInvocation(platform, fixture, options)
  const effectiveEnv = fixtureEnvironment(platform, fixture, {
    ...(platform.name === 'Bash' && apiPort ? { TASKDECK_API_PORT: String(apiPort) } : {}),
    ...env,
  })
  const captureId = `${process.pid}-${Date.now()}-${Math.random().toString(16).slice(2)}`
  const stdoutPath = join(fixture.root, `.launcher-${captureId}.stdout.log`)
  const stderrPath = join(fixture.root, `.launcher-${captureId}.stderr.log`)
  const stdoutFd = openSync(stdoutPath, 'w')
  const stderrFd = openSync(stderrPath, 'w')
  let result
  try {
    result = spawnSync(invocation.command, invocation.args, {
      cwd: fixture.root,
      timeout,
      windowsHide: true,
      env: effectiveEnv,
      stdio: ['ignore', stdoutFd, stderrFd],
    })
  } finally {
    closeSync(stdoutFd)
    closeSync(stderrFd)
  }
  result.stdout = readFileSync(stdoutPath, 'utf8')
  result.stderr = readFileSync(stderrPath, 'utf8')
  return result
}

function runPowerShellCancellation(fixture, apiPort) {
  const stdoutPath = join(fixture.root, '.interrupt.stdout.log')
  const stderrPath = join(fixture.root, '.interrupt.stderr.log')
  const stdoutFd = openSync(stdoutPath, 'w')
  const stderrFd = openSync(stderrPath, 'w')
  let result
  try {
    result = spawnSync(
      powershell,
      [
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        fixture.interruptHarness,
        '-Launcher',
        fixture.launcherPath ?? join(fixture.scriptsDir, 'dev-up.ps1'),
        '-ApiPort',
        String(apiPort),
        '-StateFile',
        fixture.stateFile,
      ],
      {
        cwd: fixture.root,
        timeout: 20_000,
        windowsHide: true,
        env: fixtureEnvironment({ name: 'PowerShell' }, fixture, { FAKE_API_MODE: 'timeout' }),
        stdio: ['ignore', stdoutFd, stderrFd],
      },
    )
  } finally {
    closeSync(stdoutFd)
    closeSync(stderrFd)
  }
  result.stdout = readFileSync(stdoutPath, 'utf8')
  result.stderr = readFileSync(stderrPath, 'utf8')
  return result
}

function startLauncher(platform, fixture, options = {}) {
  const { apiPort, env = {} } = options
  const invocation = launcherInvocation(platform, fixture, options)
  const effectiveEnv = fixtureEnvironment(platform, fixture, {
    ...(platform.name === 'Bash' && apiPort ? { TASKDECK_API_PORT: String(apiPort) } : {}),
    ...env,
  })
  const captureId = `${process.pid}-${Date.now()}-${Math.random().toString(16).slice(2)}`
  const stdoutPath = join(fixture.root, `.launcher-${captureId}.stdout.log`)
  const stderrPath = join(fixture.root, `.launcher-${captureId}.stderr.log`)
  const stdoutFd = openSync(stdoutPath, 'w')
  const stderrFd = openSync(stderrPath, 'w')
  let child
  try {
    child = spawn(invocation.command, invocation.args, {
      cwd: fixture.root,
      detached: process.platform !== 'win32',
      windowsHide: true,
      env: effectiveEnv,
      stdio: ['ignore', stdoutFd, stderrFd],
    })
  } finally {
    closeSync(stdoutFd)
    closeSync(stderrFd)
  }
  return { child, stdoutPath, stderrPath }
}

async function waitUntil(predicate, message, timeout = 5000) {
  const deadline = Date.now() + timeout
  while (Date.now() < deadline) {
    if (await predicate()) return
    await new Promise((resolve) => setTimeout(resolve, 50))
  }
  assert.fail(message)
}

async function terminateLauncherTree(child) {
  if (!child || child.exitCode !== null || child.signalCode !== null) return
  if (process.platform === 'win32') {
    spawnSync('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], {
      stdio: 'ignore',
      timeout: 5000,
      windowsHide: true,
    })
  } else {
    try {
      process.kill(-child.pid, 'SIGKILL')
    } catch (error) {
      if (error?.code !== 'ESRCH') throw error
    }
  }
  await Promise.race([
    new Promise((resolve) => child.once('exit', resolve)),
    new Promise((resolve) => setTimeout(resolve, 2000)),
  ])
}

async function releaseBlockedLauncher(started, fixture) {
  if (!started?.child || started.child.exitCode !== null || started.child.signalCode !== null) return
  await writeFile(fixture.npmReleaseFile, 'release\n')
  const exited = await new Promise((resolve) => {
    const timer = setTimeout(() => {
      started.child.removeListener('exit', onExit)
      resolve(false)
    }, 5000)
    const onExit = () => {
      clearTimeout(timer)
      resolve(true)
    }
    started.child.once('exit', onExit)
  })
  if (!exited) await terminateLauncherTree(started.child)
}

async function readEvents(fixture) {
  const text = await readOptional(fixture.npmLog)
  if (!text) return []
  return text
    .trim()
    .split(/\r?\n/)
    .filter(Boolean)
    .map(JSON.parse)
}

function combinedOutput(result) {
  return `${result.stdout ?? ''}\n${result.stderr ?? ''}`
}

function assertFailedClosed(result) {
  assert.ifError(result.error)
  assert.notEqual(result.status, 0)
  assert.doesNotMatch(combinedOutput(result), /Stack is up/)
}

async function assertNoStateAndPortsReleased(fixture, ports) {
  assert.equal(await readOptional(fixture.stateFile), null, 'failed startup retained state despite complete cleanup')
  for (const port of ports) assert.equal(await canBind(port), true, `port ${port} remained occupied`)
}

async function stopSuccessfulStack(platform, fixture, { timeout = 20_000 } = {}) {
  const stopResult = runLauncher(platform, fixture, { stop: true, timeout })
  assert.ifError(stopResult.error)
  assert.equal(stopResult.status, 0, combinedOutput(stopResult))
  assert.match(combinedOutput(stopResult), /Stack stopped/)
  assert.equal(await readOptional(fixture.stateFile), null)
}

async function assertResetSeedCycle(platform, fixture, { env = {} } = {}) {
  const apiPort = await getFreePort()
  const frontendPort = await getFreePort()
  const resetSeedEventsBefore = (await readEvents(fixture)).filter(
    (event) => event.args.join(' ') === 'run demo:seed -- --reset',
  ).length
  try {
    const result = runLauncher(platform, fixture, {
      apiPort,
      seed: true,
      resetSeed: true,
      env: { FAKE_FRONTEND_PORT: String(frontendPort), ...env },
    })
    assert.equal(result.status, 0, combinedOutput(result))
    const resetSeedEvents = (await readEvents(fixture)).filter(
      (event) => event.args.join(' ') === 'run demo:seed -- --reset',
    )
    assert.equal(resetSeedEvents.length - resetSeedEventsBefore, 1)
    assert.equal(resetSeedEvents.at(-1).taskdeckApiBaseUrl, `http://localhost:${apiPort}/api`)
    await stopSuccessfulStack(platform, fixture, { timeout: RESET_CYCLE_TEARDOWN_TIMEOUT_MS })
  } finally {
    if (existsSync(fixture.stateFile)) {
      runLauncher(platform, fixture, { stop: true, timeout: RESET_CYCLE_TEARDOWN_TIMEOUT_MS })
    }
  }
}

test('launchers encode the transactional lifecycle and custom-port environment boundary', async () => {
  const [powershellText, bashText, packageJson, nvmrc] = await Promise.all([
    readFile(powershellLauncher, 'utf8'),
    readFile(bashLauncher, 'utf8'),
    readFile(frontendPackage, 'utf8').then(JSON.parse),
    readFile(trackedNodeVersion, 'utf8'),
  ])
  const ps = normalise(powershellText)
  const sh = normalise(bashText)

  assert.equal(packageJson.engines.node, '>=24.13.1 <25')
  assert.equal(nvmrc.trim(), '24.13.1')
  assert.match(ps, /\$MinimumNodeVersion = \[version\]"24\.13\.1"/)
  assert.match(ps, /\[System\.IO\.FileShare\]::None/)
  assert.match(ps, /System\.Diagnostics\.ProcessStartInfo/)
  assert.match(ps, /UseShellExecute = \$false/)
  assert.match(ps, /\/d \/s \/c/)
  assert.match(ps, /EnvironmentVariables\["TASKDECK_DEV_EXECUTABLE"\]/)
  assert.match(ps, /TASKDECK_API_BASE_URL = \$apiBaseUrl/)
  assert.match(ps, /VITE_API_BASE_URL = \$apiBaseUrl/)
  assert.doesNotMatch(ps, /\$env:(?:TASKDECK_API_BASE_URL|VITE_API_BASE_URL)\s*=/)
  assert.match(ps, /creationToken/)
  assert.match(ps, /schemaVersion = \$StateVersion/)
  assert.match(ps, /TASKDECK_DEV_FRONTEND_READY/)
  assert.match(ps, /\$DevRunIdHeaderName = "Taskdeck-Dev-Run-Id"/)
  assert.match(ps, /AllowAutoRedirect = \$false/)
  assert.match(ps, /\$request\.Proxy = \$null/)
  assert.match(ps, /GetValues\(\$DevRunIdHeaderName\)/)
  assert.doesNotMatch(ps, /\$request\.Headers\[[^\]]*DevRunId/)

  assert.match(sh, /node_major != 24 \|\| node_minor < 13/)
  assert.match(sh, /mkdir "\$LOCK_DIR"/)
  assert.match(sh, /schemaVersion: Number\(stateVersion\)/)
  assert.match(sh, /creationToken/)
  assert.match(sh, /seed_args=\(run demo:seed\)/)
  assert.match(sh, /TASKDECK_API_BASE_URL="\$API_BASE_URL" "\$NPM_BIN" "\$\{seed_args\[@\]\}"/)
  assert.match(sh, /VITE_API_BASE_URL="\$API_BASE_URL" exec "\$NPM_BIN" run dev/)
  assert.match(sh, /redirect: "manual"/)
  assert.match(sh, /r\.headers\.get\("taskdeck-dev-run-id"\) === expectedRunId/)
  assert.doesNotMatch(sh, /export (?:TASKDECK_API_BASE_URL|VITE_API_BASE_URL)/)
})

for (const platform of platforms) {
  test(`${platform.name}: reset seed option is rejected before launcher side effects without seed`, { concurrency: false }, async () => {
    const fixture = await createFixture(platform)
    try {
      const result = runLauncher(platform, fixture, { resetSeed: true })
      assertFailedClosed(result)
      assert.match(combinedOutput(result), /reset.?seed.*only with.*seed/i)
      assert.deepEqual(await readEvents(fixture), [])
      assert.equal(await readOptional(fixture.stateFile), null)
    } finally {
      await removeFixture(fixture)
    }
  })

  test(
    `${platform.name}: reset seed forwards --reset and preserves ordinary seed arguments on first use`,
    { concurrency: false, timeout: 50_000 },
    async () => {
      const fixture = await createFixture(platform)
      try {
        await assertResetSeedCycle(platform, fixture)
      } finally {
        await removeFixture(fixture)
      }
    },
  )

  test(
    `${platform.name}: reset seed forwards --reset again after a clean stop`,
    { concurrency: false, timeout: 100_000 },
    async () => {
      const fixture = await createFixture(platform)
      try {
        await assertResetSeedCycle(platform, fixture)
        await assertResetSeedCycle(platform, fixture)
      } finally {
        await removeFixture(fixture)
      }
    },
  )
}

if (bash) {
  test(
    'Bash: reset seed teardown allows the complete bounded TERM/KILL path',
    { concurrency: false, timeout: 50_000 },
    async () => {
      const platform = { name: 'Bash', launcher: 'dev-up.sh' }
      const fixture = await createFixture(platform)
      try {
        await assertResetSeedCycle(platform, fixture, {
          env: {
            FAKE_STOP_DESCENDANT: '1',
          },
        })
        await assertBashTermKillEscalation(fixture)
      } finally {
        await removeFixture(fixture)
      }
    },
  )
}

if (powershell) {
  test('PowerShell: pipeline cancellation runs transactional cleanup from finally', { concurrency: false }, async () => {
    const platform = { name: 'PowerShell', launcher: 'dev-up.ps1' }
    const fixture = await createFixture(platform)
    const apiPort = await getFreePort()
    try {
      const result = runPowerShellCancellation(fixture, apiPort)
      assert.ifError(result.error)
      assert.equal(result.status, 0, combinedOutput(result))
      assert.doesNotMatch(combinedOutput(result), /Stack is up/)
      await assertNoStateAndPortsReleased(fixture, [apiPort])
      const events = await readEvents(fixture)
      assert.ok(events.some((event) => event.kind === 'dotnet'))
      assert.equal(events.some((event) => event.args.join(' ') === 'run dev'), false)
    } finally {
      if (existsSync(fixture.stateFile)) runLauncher(platform, fixture, { stop: true })
      await removeFixture(fixture)
    }
  })

  test('PowerShell: Stop retries transient post-taskkill Unknown until both trees are missing', { concurrency: false }, async () => {
    const platform = { name: 'PowerShell', launcher: 'dev-up.ps1' }
    const fixture = await createFixture(platform)
    const apiPort = await getFreePort()
    const frontendPort = await getFreePort()
    const foreign = await listenForeign()
    try {
      const startResult = runLauncher(platform, fixture, {
        apiPort,
        env: { FAKE_FRONTEND_PORT: String(frontendPort) },
      })
      assert.ifError(startResult.error)
      assert.equal(startResult.status, 0, combinedOutput(startResult))
      assert.equal(await canBind(apiPort), false)
      assert.equal(await canBind(frontendPort), false)

      await installPostTaskkillUnknownProbe(fixture)
      const stopResult = runLauncher(platform, fixture, { stop: true })
      assert.ifError(stopResult.error)
      assert.equal(stopResult.status, 0, combinedOutput(stopResult))
      assert.match(combinedOutput(stopResult), /Forced transient post-taskkill identity: Unknown/)
      assert.match(combinedOutput(stopResult), /Stack stopped/)
      assert.equal(await readOptional(fixture.stateFile), null)
      assert.equal(await canBind(apiPort), true)
      assert.equal(await canBind(frontendPort), true)
      assert.equal(foreign.listening, true, 'Stop killed an unrelated listener')
    } finally {
      if (existsSync(fixture.stateFile)) runLauncher(platform, fixture, { stop: true })
      await new Promise((resolve) => foreign.close(resolve))
      await removeFixture(fixture)
    }
  })
}

if (bash) {
  test('Bash: Match to Mismatch after TERM never escalates to KILL', { concurrency: false }, async () => {
    const source = normalise(await readFile(bashLauncher, 'utf8'))
    const fixtureRoot = await mkdtemp(join(tmpdir(), 'taskdeck-dev-up-identity-seam-'))
    const harness = join(fixtureRoot, 'identity-seam.sh')
    const identityFile = join(fixtureRoot, 'identity-count')
    const killLog = join(fixtureRoot, 'kill.log')
    try {
      await writeFile(identityFile, '0\n')
      await writeFile(
        harness,
        `#!/usr/bin/env bash
set -euo pipefail
warn() { :; }
process_identity_status() {
  local count
  count="$(<"$IDENTITY_FILE")"
  printf '%s\n' "$((count + 1))" > "$IDENTITY_FILE"
  if [[ "$count" -eq 0 ]]; then printf 'match\n'; else printf 'mismatch\n'; fi
}
kill() { printf '%s\n' "$*" >> "$KILL_LOG"; return 0; }
${extractBashFunction(source, 'wait_for_identity_exit')}
${extractBashFunction(source, 'stop_exact_process')}
stop_exact_process api 4242 node recorded-token
`,
      )
      const result = spawnSync(bash, [toPosixPath(harness)], {
        encoding: 'utf8',
        timeout: 5000,
        windowsHide: true,
        env: {
          ...process.env,
          IDENTITY_FILE: toPosixPath(identityFile),
          KILL_LOG: toPosixPath(killLog),
        },
      })
      assert.ifError(result.error)
      assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`)
      assert.deepEqual((await readFile(killLog, 'utf8')).trim().split(/\r?\n/), ['-TERM 4242'])
    } finally {
      await rm(fixtureRoot, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 })
    }
  })

  test('Bash: reparented descendant capture fails closed without signalling', { concurrency: false }, async () => {
    const source = normalise(await readFile(bashLauncher, 'utf8'))
    const fixtureRoot = await mkdtemp(join(tmpdir(), 'taskdeck-dev-up-ancestry-seam-'))
    const harness = join(fixtureRoot, 'ancestry-seam.sh')
    const parentReadFile = join(fixtureRoot, 'parent-read-count')
    const signalLog = join(fixtureRoot, 'signal.log')
    try {
      await writeFile(parentReadFile, '0\n')
      await writeFile(
        harness,
        `#!/usr/bin/env bash
set -euo pipefail
warn() { :; }
step() { :; }
process_identity_status() { printf 'match\n'; }
list_child_pids() { [[ "$1" == "100" ]] && printf '200\n'; }
process_parent_pid() {
  local count
  count="$(<"$PARENT_READ_FILE")"
  printf '%s\n' "$((count + 1))" > "$PARENT_READ_FILE"
  if [[ "$count" -eq 0 ]]; then printf '100\n'; else printf '999\n'; fi
}
process_name() { printf 'foreign-node\n'; }
process_creation_token() { printf 'foreign-token\n'; }
stop_exact_process() { printf '%s\n' "$*" >> "$SIGNAL_LOG"; return 0; }
${extractBashFunction(source, 'capture_descendant_records')}
${extractBashFunction(source, 'stop_recorded_process')}
if stop_recorded_process api 100 root root-token; then exit 90; fi
[[ ! -e "$SIGNAL_LOG" ]]
`,
      )
      const result = spawnSync(bash, [toPosixPath(harness)], {
        encoding: 'utf8',
        timeout: 5000,
        windowsHide: true,
        env: {
          ...process.env,
          PARENT_READ_FILE: toPosixPath(parentReadFile),
          SIGNAL_LOG: toPosixPath(signalLog),
        },
      })
      assert.ifError(result.error)
      assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`)
      assert.equal(await readOptional(signalLog), null)
    } finally {
      await rm(fixtureRoot, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 })
    }
  })

  test('Bash: Windows fallback binds MSYS PID to StartTime and mismatch sends no signal', { concurrency: false }, async () => {
    const source = normalise(await readFile(bashLauncher, 'utf8'))
    const fixtureRoot = await mkdtemp(join(tmpdir(), 'taskdeck-dev-up-windows-identity-'))
    const harness = join(fixtureRoot, 'windows-identity.sh')
    const fakePowerShell = join(fixtureRoot, 'fake-powershell')
    const tickFile = join(fixtureRoot, 'ticks')
    const windowsPidLog = join(fixtureRoot, 'windows-pid.log')
    const signalLog = join(fixtureRoot, 'signal.log')
    try {
      await writeFile(tickFile, '638000000000000001\n')
      await writeFile(
        fakePowerShell,
        `#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$TASKDECK_DEV_WINDOWS_PID" > "$WINDOWS_PID_LOG"
tr -d '\\r\\n' < "$TICK_FILE"
`,
      )
      await chmod(fakePowerShell, 0o755)
      await writeFile(
        harness,
        `#!/usr/bin/env bash
set -euo pipefail
SYSTEM_BOOT_ID=''
windows_powershell_bin() { printf '%s\n' "$FAKE_POWERSHELL"; }
ps() {
  if [[ "$#" -eq 2 && "$1" == '-p' && "$2" == '42424242' ]]; then
    printf '%s\n' '      PID    PPID    PGID     WINPID   TTY         UID    STIME COMMAND'
    printf '%s\n' ' 42424242       1 42424242       9001  ?        197609 01:23:45 /usr/bin/bash'
    return 0
  fi
  printf 'ps -o lstart is unavailable\n' >&2
  return 2
}
process_name() { printf 'bash\n'; }
warn() { :; }
kill() {
  if [[ "$1" == '-0' ]]; then return 0; fi
  printf '%s\n' "$*" >> "$SIGNAL_LOG"
}
${extractBashFunction(source, 'msys_windows_pid')}
${extractBashFunction(source, 'windows_process_creation_token')}
${extractBashFunction(source, 'process_creation_token')}
${extractBashFunction(source, 'process_identity_status')}
${extractBashFunction(source, 'wait_for_identity_exit')}
${extractBashFunction(source, 'stop_exact_process')}
expected="$(process_creation_token 42424242)"
[[ "$expected" == 'windows:9001:638000000000000001' ]]
[[ "$(<"$WINDOWS_PID_LOG")" == '9001' ]]
printf '638000000000000002\n' > "$TICK_FILE"
[[ "$(process_creation_token 42424242)" == 'windows:9001:638000000000000002' ]]
stop_exact_process api 42424242 bash "$expected"
[[ ! -e "$SIGNAL_LOG" ]]
`,
      )
      const result = spawnSync(bash, [toPosixPath(harness)], {
        encoding: 'utf8',
        timeout: 5000,
        windowsHide: true,
        env: {
          ...process.env,
          FAKE_POWERSHELL: toPosixPath(fakePowerShell),
          TICK_FILE: toPosixPath(tickFile),
          WINDOWS_PID_LOG: toPosixPath(windowsPidLog),
          SIGNAL_LOG: toPosixPath(signalLog),
        },
      })
      assert.ifError(result.error)
      assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`)
      assert.equal(await readOptional(signalLog), null)
    } finally {
      await rm(fixtureRoot, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 })
    }
  })
}

for (const platform of platforms) {
  test(`${platform.name}: unsupported and failed Node probes run no npm or API`, { concurrency: false }, async (t) => {
    for (const [version, probeExit] of [
      ['24.13.0', '0'],
      ['25.0.0', '0'],
      ['24.13.1', '9'],
    ]) {
      await t.test(`${version} exit ${probeExit}`, async () => {
        const fixture = await createFixture(platform)
        try {
          const result = runLauncher(platform, fixture, {
            env: { FAKE_NODE_VERSION: version, FAKE_NODE_VERSION_EXIT: probeExit },
          })
          assertFailedClosed(result)
          const output = combinedOutput(result)
          if (probeExit === '0') assert.match(output, new RegExp(`found v${version.replaceAll('.', '\\.')}`))
          else assert.match(output, /Could not read a supported Node\.js version/)
          assert.deepEqual(await readEvents(fixture), [])
          assert.equal(await readOptional(fixture.stateFile), null)
        } finally {
          await removeFixture(fixture)
        }
      })
    }
  })

  test(`${platform.name}: stale dependency tree is reconciled and npm failure starts no server`, { concurrency: false }, async () => {
    const fixture = await createFixture(platform)
    try {
      const result = runLauncher(platform, fixture, { env: { FAKE_NPM_CI_MODE: 'fail' } })
      assertFailedClosed(result)
      assert.match(combinedOutput(result), /Frontend dependency reconciliation failed/)
      const events = await readEvents(fixture)
      assert.deepEqual(events.map((event) => [event.kind, ...event.args]), [
        ['npm', 'ci', '--no-audit', '--no-fund'],
      ])
      assert.equal(await readOptional(fixture.stateFile), null)
      assert.equal(existsSync(join(fixture.frontendDir, 'node_modules', 'newly-locked-direct-dependency')), false)
    } finally {
      await removeFixture(fixture)
    }
  })

  test(`${platform.name}: concurrent launchers serialize before changing state`, { concurrency: false }, async () => {
    const fixture = await createFixture(platform)
    const apiPort = await getFreePort()
    let first
    try {
      first = startLauncher(platform, fixture, {
        apiPort,
        env: { FAKE_NPM_CI_MODE: 'hang' },
      })
      await waitUntil(
        async () => (await readEvents(fixture)).some((event) => event.args.join(' ') === 'ci --no-audit --no-fund'),
        'first launcher never reached the dependency reconciliation seam',
      )
      assert.equal(existsSync(fixture.operationLock), true)

      const second = runLauncher(platform, fixture, { apiPort, timeout: 5000 })
      assertFailedClosed(second)
      assert.match(combinedOutput(second), /Another dev-up start\/stop operation is active/)
      assert.equal(await readOptional(fixture.stateFile), null)
    } finally {
      await releaseBlockedLauncher(first, fixture)
      await removeFixture(fixture)
    }
  })

  test(`${platform.name}: dead legacy PID state is discarded without killing anything`, { concurrency: false }, async () => {
    const fixture = await createFixture(platform)
    try {
      await mkdir(fixture.stateDir, { recursive: true })
      await writeFile(fixture.stateFile, '2147483646 node\n2147483645 dotnet\n')
      const result = runLauncher(platform, fixture, { stop: true })
      assert.ifError(result.error)
      assert.equal(result.status, 0, combinedOutput(result))
      assert.match(combinedOutput(result), /Removed legacy PID state only after every referenced PID was absent/)
      assert.equal(await readOptional(fixture.stateFile), null)
    } finally {
      await removeFixture(fixture)
    }
  })

  test(`${platform.name}: malformed legacy PID state is retained even without a final newline`, { concurrency: false }, async () => {
    const fixture = await createFixture(platform)
    const malformed = '2147483646 node\n2147483645 dotnet unexpected'
    try {
      await mkdir(fixture.stateDir, { recursive: true })
      await writeFile(fixture.stateFile, malformed)
      const result = runLauncher(platform, fixture, { stop: true })
      assertFailedClosed(result)
      assert.match(combinedOutput(result), /malformed|unsupported|legacy/i)
      assert.equal(await readFile(fixture.stateFile, 'utf8'), malformed)
    } finally {
      await removeFixture(fixture)
    }
  })

  test(`${platform.name}: foreign API owner survives and a checked free custom port is printed`, { concurrency: false }, async () => {
    const fixture = await createFixture(platform)
    const foreign = await listenForeign()
    const foreignPort = foreign.address().port
    try {
      const result = runLauncher(platform, fixture, { apiPort: foreignPort })
      assertFailedClosed(result)
      const output = combinedOutput(result)
      assert.match(output, new RegExp(`API port ${foreignPort} is already owned`))
      const commandMatch =
        platform.name === 'PowerShell'
          ? output.match(/Checked custom-port command: .* -ApiPort (\d+)/)
          : output.match(/Checked custom-port command: TASKDECK_API_PORT=(\d+)/)
      assert.ok(commandMatch, output)
      assert.equal(await canBind(Number(commandMatch[1])), true)
      assert.equal(foreign.listening, true)
      assert.deepEqual(await readEvents(fixture), [])
      assert.equal(await readOptional(fixture.stateFile), null)
    } finally {
      await new Promise((resolve) => foreign.close(resolve))
      await removeFixture(fixture)
    }
  })

  test(`${platform.name}: API exit, readiness timeout, seed failure, and reset-seed failure clean transactionally`, { concurrency: false }, async (t) => {
    for (const scenario of [
      { name: 'API exit', env: { FAKE_API_MODE: 'exit' }, seed: false },
      { name: 'API timeout', env: { FAKE_API_MODE: 'timeout' }, seed: false },
      { name: 'API exits after readiness', env: { FAKE_API_MODE: 'exit-after-ready' }, seed: false },
      { name: 'seed failure', env: { FAKE_SEED_MODE: 'fail' }, seed: true },
      { name: 'reset seed failure', env: { FAKE_SEED_MODE: 'fail' }, seed: true, resetSeed: true },
    ]) {
      await t.test(scenario.name, async () => {
        const fixture = await createFixture(platform)
        const apiPort = await getFreePort()
        const frontendPort = await getFreePort()
        try {
          const result = runLauncher(platform, fixture, {
            apiPort,
            seed: scenario.seed,
            resetSeed: scenario.resetSeed,
            env: {
              ...scenario.env,
              FAKE_FRONTEND_PORT: String(frontendPort),
              ...(scenario.name === 'API timeout'
                ? { TASKDECK_DEV_API_READY_TIMEOUT_SECONDS: '1' }
                : {}),
            },
          })
          assertFailedClosed(result)
          await assertNoStateAndPortsReleased(fixture, [apiPort, frontendPort])
          if (scenario.seed) {
            const seedEvent = (await readEvents(fixture)).find((event) => event.args[0] === 'run' && event.args[1] === 'demo:seed')
            assert.equal(seedEvent.taskdeckApiBaseUrl, `http://localhost:${apiPort}/api`)
          }
        } finally {
          await removeFixture(fixture)
        }
      })
    }
  })

  test(`${platform.name}: HTTP 200 without one exact matching run identity never seeds or succeeds`, { concurrency: false }, async (t) => {
    for (const proofMode of ['missing', 'mismatch', 'duplicate']) {
      await t.test(proofMode, async () => {
        const fixture = await createFixture(platform)
        const apiPort = await getFreePort()
        const frontendPort = await getFreePort()
        try {
          const result = runLauncher(platform, fixture, {
            apiPort,
            seed: true,
            resetSeed: true,
            env: {
              FAKE_API_PROOF_MODE: proofMode,
              FAKE_FRONTEND_PORT: String(frontendPort),
              TASKDECK_DEV_API_READY_TIMEOUT_SECONDS: '1',
            },
          })
          assertFailedClosed(result)
          await assertNoStateAndPortsReleased(fixture, [apiPort, frontendPort])
          const events = await readEvents(fixture)
          assert.equal(events.some((event) => event.args[0] === 'run' && event.args[1] === 'demo:seed'), false)
          assert.equal(events.some((event) => event.args.join(' ') === 'run dev'), false)
        } finally {
          await removeFixture(fixture)
        }
      })
    }
  })

  for (const scenario of [
    {
      name: 'changes before seed',
      proofMode: 'flip-after-first-valid',
      seed: true,
      expectedSeedCount: 0,
      expectedFailure: /before demo seeding/,
    },
    {
      name: 'changes after seed',
      proofMode: 'flip-after-seed',
      seed: true,
      expectedSeedCount: 1,
      expectedFailure: /after demo seeding/,
    },
    {
      name: 'changes after final state write',
      proofMode: 'flip-after-final-state',
      seed: false,
      expectedSeedCount: 0,
      expectedFailure: /after final state commit/,
    },
  ]) {
    test(`${platform.name}: run identity ${scenario.name}`, { concurrency: false, timeout: 30_000 }, async () => {
      const fixture = await createFixture(platform)
      const apiPort = await getFreePort()
      const frontendPort = await getFreePort()
      try {
        const result = runLauncher(platform, fixture, {
          apiPort,
          seed: scenario.seed,
          env: {
            FAKE_API_PROOF_MODE: scenario.proofMode,
            FAKE_FRONTEND_PORT: String(frontendPort),
          },
        })
        assertFailedClosed(result)
        assert.match(combinedOutput(result), scenario.expectedFailure)
        await assertNoStateAndPortsReleased(fixture, [apiPort, frontendPort])
        const events = await readEvents(fixture)
        assert.equal(
          events.filter((event) => event.args.join(' ') === 'run demo:seed').length,
          scenario.expectedSeedCount,
        )
        assert.equal(
          events.some((event) => event.args.join(' ') === 'run dev'),
          scenario.proofMode === 'flip-after-final-state',
        )
      } finally {
        await removeFixture(fixture)
      }
    })
  }

  for (const mode of ['missing', 'malformed', 'duplicate-property', 'duplicate', 'late', 'transform-failure', 'exit-after-entry-response', 'stderr-marker', 'spoof']) {
    const isSpoofMode = mode === 'spoof'
    test(`${platform.name}: invalid Vite outcome ${mode} cleans both trees and never reports success`, {
      concurrency: false,
      ...(isSpoofMode ? { timeout: 40_000 } : {}),
    }, async () => {
      const fixture = await createFixture(platform)
      const apiPort = await getFreePort()
      const frontendPort = await getFreePort()
      const spoofPort = await getFreePort()
      try {
        const result = runLauncher(platform, fixture, {
          apiPort,
          ...(isSpoofMode ? { timeout: 30_000 } : {}),
          env: {
            FAKE_FRONTEND_PORT: String(frontendPort),
            FAKE_FRONTEND_MODE: mode,
            FAKE_SPOOF_PORT: String(spoofPort),
            TASKDECK_DEV_FRONTEND_READY_TIMEOUT_SECONDS: '1',
          },
        })
        assertFailedClosed(result)
        await assertNoStateAndPortsReleased(fixture, [apiPort, frontendPort, spoofPort])
        const viteEvent = (await readEvents(fixture)).find((event) => event.args.join(' ') === 'run dev')
        assert.equal(viteEvent.viteApiBaseUrl, `http://localhost:${apiPort}/api`)
      } finally {
        await removeFixture(fixture)
      }
    })
  }

  test(`${platform.name}: success uses exact marker URL, isolated env, versioned identity state, and safe Stop`, { concurrency: false }, async () => {
    const fixture = await createFixture(platform)
    const apiPort = await getFreePort()
    const frontendPort = await getFreePort()
    const foreign = await listenForeign()
    let recoverableState = null
    try {
      await mkdir(fixture.stateDir, { recursive: true })
      await writeFile(
        join(fixture.stateDir, 'dev-up-frontend.stdout.log'),
        'TASKDECK_DEV_FRONTEND_READY {"schemaVersion":1,"url":"http://localhost:9/","port":9}\n',
      )
      const result = runLauncher(platform, fixture, {
        apiPort,
        seed: true,
        env: {
          FAKE_FRONTEND_PORT: String(frontendPort),
          TASKDECK_API_BASE_URL: 'http://poison.invalid/api',
          VITE_API_BASE_URL: 'http://poison.invalid/api',
          TASKDECK_DEV_RUN_ID: 'poison-run',
        },
      })
      assert.equal(result.error, undefined, combinedOutput(result))
      assert.equal(
        result.status,
        0,
        `${combinedOutput(result)}\nevents=${JSON.stringify(await readEvents(fixture))}\nstate=${await readOptional(fixture.stateFile)}`,
      )
      const output = combinedOutput(result)
      assert.match(output, /Stack is up/)
      assert.match(output, new RegExp(`Frontend: http://localhost:${frontendPort}/`))
      assert.doesNotMatch(output, /Frontend: http:\/\/localhost:9\//)

      assert.equal(existsSync(join(fixture.frontendDir, 'node_modules', 'newly-locked-direct-dependency')), true)
      const events = await readEvents(fixture)
      const seedEvent = events.find((event) => event.args.join(' ') === 'run demo:seed')
      const viteEvent = events.find((event) => event.args.join(' ') === 'run dev')
      assert.equal(seedEvent.taskdeckApiBaseUrl, `http://localhost:${apiPort}/api`)
      assert.equal(viteEvent.viteApiBaseUrl, `http://localhost:${apiPort}/api`)
      assert.match(seedEvent.runId, /^[0-9a-f-]{36}$/i)
      assert.equal(viteEvent.runId, seedEvent.runId)
      assert.notEqual(seedEvent.runId, 'poison-run')

      const state = JSON.parse(await readFile(fixture.stateFile, 'utf8'))
      recoverableState = `${JSON.stringify(state, null, 2)}\n`
      assert.equal(state.schemaVersion, 1)
      assert.equal(state.runId, seedEvent.runId)
      assert.equal(state.apiPort, apiPort)
      assert.deepEqual(state.frontend, { url: `http://localhost:${frontendPort}/`, port: frontendPort })
      assert.deepEqual(state.processes.map((record) => record.role), ['api', 'frontend'])
      for (const record of state.processes) {
        assert.ok(Number.isSafeInteger(record.pid) && record.pid > 0)
        assert.ok(record.name)
        assert.ok(record.creationToken)
      }
      for (const path of Object.values(state.logs)) assert.match(path, new RegExp(state.runId))

      const collision = runLauncher(platform, fixture, { apiPort, env: { FAKE_FRONTEND_PORT: String(frontendPort) } })
      assertFailedClosed(collision)
      assert.match(combinedOutput(collision), /launcher-owned stack is already running/)
      assert.equal(await canBind(apiPort), false)
      assert.equal(await canBind(frontendPort), false)
      assert.equal(foreign.listening, true, 'unrelated fallback-port owner was disturbed')

      const legacyState = `${state.processes.map((record) => `${record.pid} ${record.name}`).join('\n')}\n`
      await writeFile(fixture.stateFile, legacyState)
      const legacyStop = runLauncher(platform, fixture, { stop: true })
      assertFailedClosed(legacyStop)
      assert.match(combinedOutput(legacyStop), /live legacy process|legacy state without creation tokens/i)
      assert.equal(await readFile(fixture.stateFile, 'utf8'), legacyState)
      assert.equal(await canBind(apiPort), false, 'live legacy API was killed without a creation token')
      assert.equal(await canBind(frontendPort), false, 'live legacy frontend was killed without a creation token')
      await writeFile(fixture.stateFile, recoverableState)

      await stopSuccessfulStack(platform, fixture)
      assert.equal(await canBind(apiPort), true)
      assert.equal(await canBind(frontendPort), true)
      assert.equal(foreign.listening, true, 'Stop killed an unrelated listener')
    } finally {
      if (existsSync(fixture.stateFile) && recoverableState) {
        await writeFile(fixture.stateFile, recoverableState)
      }
      if (existsSync(fixture.stateFile)) runLauncher(platform, fixture, { stop: true })
      await new Promise((resolve) => foreign.close(resolve))
      await removeFixture(fixture)
    }
  })

  test(`${platform.name}: Vite fallback leaves the foreign frontend-port owner alive`, { concurrency: false }, async () => {
    const fixture = await createFixture(platform)
    const foreign = await listenForeign('localhost')
    const foreignPort = foreign.address().port
    let apiPort
    do apiPort = await getFreePort()
    while (apiPort === foreignPort)
    try {
      const result = runLauncher(platform, fixture, {
        apiPort,
        env: {
          FAKE_FRONTEND_PORT: String(foreignPort),
          FAKE_FRONTEND_MODE: 'fallback',
        },
      })
      assert.ifError(result.error)
      assert.equal(result.status, 0, combinedOutput(result))
      const state = JSON.parse(await readFile(fixture.stateFile, 'utf8'))
      assert.notEqual(state.frontend.port, foreignPort)
      assert.match(combinedOutput(result), new RegExp(`Frontend: http://localhost:${state.frontend.port}/`))
      assert.equal(foreign.listening, true)

      await stopSuccessfulStack(platform, fixture)
      assert.equal(foreign.listening, true, 'Stop killed the foreign fallback-port owner')
      assert.equal(await canBind(state.frontend.port), true)
    } finally {
      if (existsSync(fixture.stateFile)) runLauncher(platform, fixture, { stop: true })
      await new Promise((resolve) => foreign.close(resolve))
      await removeFixture(fixture)
    }
  })

  test(`${platform.name}: high-volume stdout and stderr cannot deadlock marker acceptance`, { concurrency: false }, async () => {
    const fixture = await createFixture(platform)
    const apiPort = await getFreePort()
    const frontendPort = await getFreePort()
    try {
      const result = runLauncher(platform, fixture, {
        apiPort,
        timeout: 30_000,
        env: { FAKE_FRONTEND_PORT: String(frontendPort), FAKE_FRONTEND_MODE: 'high-volume' },
      })
      assert.ifError(result.error)
      assert.equal(result.status, 0, combinedOutput(result))
      assert.match(combinedOutput(result), /Stack is up/)
      await stopSuccessfulStack(platform, fixture)
    } finally {
      if (existsSync(fixture.stateFile)) runLauncher(platform, fixture, { stop: true })
      await removeFixture(fixture)
    }
  })

  test(`${platform.name}: PID reuse mismatch is retained and the foreign process survives`, { concurrency: false }, async () => {
    const fixture = await createFixture(platform)
    if (platform.name === 'Bash') {
      const apiPort = await getFreePort()
      const frontendPort = await getFreePort()
      let recoverableState = null
      try {
        const start = runLauncher(platform, fixture, {
          apiPort,
          env: { FAKE_FRONTEND_PORT: String(frontendPort) },
        })
        assert.equal(start.status, 0, combinedOutput(start))
        const state = JSON.parse(await readFile(fixture.stateFile, 'utf8'))
        recoverableState = `${JSON.stringify(state, null, 2)}\n`
        const originalToken = state.processes[0].creationToken
        state.processes[0].creationToken = 'definitely-not-this-process'
        await writeFile(fixture.stateFile, `${JSON.stringify(state, null, 2)}\n`)

        const result = runLauncher(platform, fixture, { stop: true })
        assertFailedClosed(result)
        assert.match(combinedOutput(result), /different name or creation token|identity is mismatch/i)
        assert.equal(existsSync(fixture.stateFile), true)
        assert.equal(await canBind(apiPort), false, 'mismatched API identity was killed')

        state.processes[0].creationToken = originalToken
        await writeFile(fixture.stateFile, `${JSON.stringify(state, null, 2)}\n`)
        await stopSuccessfulStack(platform, fixture)
      } finally {
        if (existsSync(fixture.stateFile) && recoverableState) {
          await writeFile(fixture.stateFile, recoverableState)
        }
        if (existsSync(fixture.stateFile)) runLauncher(platform, fixture, { stop: true })
        await removeFixture(fixture)
      }
      return
    }

    const foreignProcess = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], {
      stdio: 'ignore',
      windowsHide: true,
    })
    const runId = '11111111-1111-4111-8111-111111111111'
    const apiPort = await getFreePort()
    try {
      await mkdir(fixture.stateDir, { recursive: true })
      const prefix = join(fixture.stateDir, `dev-up-${runId}`)
      await writeFile(
        fixture.stateFile,
        `${JSON.stringify(
          {
            schemaVersion: 1,
            runId,
            apiPort,
            frontend: null,
            logs: {
              apiStdout: `${prefix}-api.stdout.log`,
              apiStderr: `${prefix}-api.stderr.log`,
              frontendStdout: `${prefix}-frontend.stdout.log`,
              frontendStderr: `${prefix}-frontend.stderr.log`,
            },
            processes: [
              { role: 'api', pid: foreignProcess.pid, name: 'node', creationToken: 'definitely-not-this-process' },
            ],
          },
          null,
          2,
        )}\n`,
      )
      const result = runLauncher(platform, fixture, { stop: true })
      assertFailedClosed(result)
      assert.match(combinedOutput(result), /identity is mismatch|different name or creation token/i)
      assert.equal(existsSync(fixture.stateFile), true)
      assert.equal(foreignProcess.exitCode, null)
    } finally {
      foreignProcess.kill('SIGKILL')
      await removeFixture(fixture)
    }
  })
}
