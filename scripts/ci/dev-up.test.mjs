import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { existsSync } from 'node:fs'
import {
  chmod,
  copyFile,
  mkdir,
  mkdtemp,
  readFile,
  rm,
  writeFile,
} from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { delimiter, join } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const repoRoot = fileURLToPath(new URL('../../', import.meta.url))
const powershellLauncher = join(repoRoot, 'scripts', 'dev-up.ps1')
const bashLauncher = join(repoRoot, 'scripts', 'dev-up.sh')
const frontendPackage = join(repoRoot, 'frontend', 'taskdeck-web', 'package.json')
const trackedNodeVersion = join(repoRoot, '.nvmrc')

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
    '': {
      name: syntheticPackage.name,
      dependencies: syntheticPackage.dependencies,
    },
    'node_modules/existing-direct-dependency': { version: '1.0.0' },
    'node_modules/newly-locked-direct-dependency': { version: '1.0.0' },
  },
}

function normalise(text) {
  return text.replaceAll('\r\n', '\n')
}

async function readOptional(path) {
  try {
    return await readFile(path, 'utf8')
  } catch (error) {
    if (error?.code === 'ENOENT') return null
    throw error
  }
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

async function createFixture(launcherName) {
  const root = await mkdtemp(join(tmpdir(), `taskdeck-dev-up-${launcherName}-`))
  const scriptsDir = join(root, 'scripts')
  const frontendDir = join(root, 'frontend', 'taskdeck-web')
  const fakeBin = join(root, 'fake-bin')
  const launcherPath = join(scriptsDir, launcherName)

  await mkdir(scriptsDir, { recursive: true })
  await mkdir(join(frontendDir, 'node_modules', 'existing-direct-dependency'), { recursive: true })
  await mkdir(join(root, 'backend', 'src', 'Taskdeck.Api'), { recursive: true })
  await mkdir(fakeBin, { recursive: true })
  await copyFile(launcherName.endsWith('.ps1') ? powershellLauncher : bashLauncher, launcherPath)
  await writeFile(join(frontendDir, 'package.json'), `${JSON.stringify(syntheticPackage, null, 2)}\n`)
  await writeFile(join(frontendDir, 'package-lock.json'), `${JSON.stringify(syntheticLock, null, 2)}\n`)
  await writeFile(join(root, 'backend', 'src', 'Taskdeck.Api', 'Taskdeck.Api.csproj'), '<Project />\n')
  if (launcherName.endsWith('.sh')) await chmod(launcherPath, 0o755)

  return {
    root,
    fakeBin,
    launcherPath,
    npmLog: join(root, 'npm.log'),
    dotnetLog: join(root, 'dotnet.log'),
    dataDir: join(root, 'data'),
  }
}

async function installPowerShellStubs(fixture) {
  await writeFile(
    join(fixture.fakeBin, 'node.cmd'),
    '@echo off\r\necho %FAKE_NODE_VERSION%\r\n',
  )
  await writeFile(
    join(fixture.fakeBin, 'npm.cmd'),
    [
      '@echo off',
      '> "%TASKDECK_NPM_LOG%" echo %*',
      'if not exist "node_modules\\existing-direct-dependency" exit /b 80',
      'if exist "node_modules\\newly-locked-direct-dependency" exit /b 81',
      'exit /b 42',
      '',
    ].join('\r\n'),
  )
  await writeFile(
    join(fixture.fakeBin, 'dotnet.cmd'),
    '@echo off\r\n> "%TASKDECK_DOTNET_LOG%" echo %*\r\nexit /b 41\r\n',
  )
}

async function installBashStubs(fixture) {
  const stubs = {
    node: '#!/usr/bin/env bash\nprintf \'%s\\n\' "${FAKE_NODE_VERSION}"\n',
    npm: [
      '#!/usr/bin/env bash',
      'printf \'%s\\n\' "$*" > "${TASKDECK_NPM_LOG}"',
      '[[ -d node_modules/existing-direct-dependency ]] || exit 80',
      '[[ ! -e node_modules/newly-locked-direct-dependency ]] || exit 81',
      'exit 42',
      '',
    ].join('\n'),
    dotnet: [
      '#!/usr/bin/env bash',
      'printf \'%s\\n\' "$*" > "${TASKDECK_DOTNET_LOG}"',
      'exit 41',
      '',
    ].join('\n'),
  }

  for (const [name, source] of Object.entries(stubs)) {
    const path = join(fixture.fakeBin, name)
    await writeFile(path, source)
    await chmod(path, 0o755)
  }
}

async function runPowerShellFixture(nodeVersion) {
  const fixture = await createFixture('dev-up.ps1')
  await installPowerShellStubs(fixture)
  const powershell = join(
    process.env.SystemRoot || 'C:\\Windows',
    'System32',
    'WindowsPowerShell',
    'v1.0',
    'powershell.exe',
  )
  const result = spawnSync(
    powershell,
    ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', fixture.launcherPath],
    {
      cwd: fixture.root,
      encoding: 'utf8',
      timeout: 15_000,
      env: {
        ...process.env,
        PATH: `${fixture.fakeBin}${delimiter}${process.env.PATH || ''}`,
        LOCALAPPDATA: fixture.dataDir,
        FAKE_NODE_VERSION: nodeVersion,
        TASKDECK_NPM_LOG: fixture.npmLog,
        TASKDECK_DOTNET_LOG: fixture.dotnetLog,
      },
    },
  )
  return { fixture, result }
}

async function runBashFixture(nodeVersion, bash) {
  const fixture = await createFixture('dev-up.sh')
  await installBashStubs(fixture)
  const result = spawnSync(bash, ['scripts/dev-up.sh'], {
    cwd: fixture.root,
    encoding: 'utf8',
    timeout: 15_000,
    env: {
      ...process.env,
      PATH: `${toPosixPath(fixture.fakeBin)}:/usr/local/bin:/usr/bin:/bin`,
      XDG_DATA_HOME: toPosixPath(fixture.dataDir),
      FAKE_NODE_VERSION: nodeVersion,
      TASKDECK_NPM_LOG: toPosixPath(fixture.npmLog),
      TASKDECK_DOTNET_LOG: toPosixPath(fixture.dotnetLog),
    },
  })
  return { fixture, result }
}

async function assertUnsupported(result, fixture, expectedVersion) {
  assert.ifError(result.error)
  assert.notEqual(result.status, 0, `unsupported Node ${expectedVersion} unexpectedly passed`)
  const output = `${result.stdout}\n${result.stderr}`
  assert.match(output, new RegExp(`found v${expectedVersion.replaceAll('.', '\\.')}`))
  assert.doesNotMatch(output, /Stack is up/)
  assert.equal(await readOptional(fixture.npmLog), null, 'npm ran after an unsupported Node version')
  assert.equal(await readOptional(fixture.dotnetLog), null, 'the API started after an unsupported Node version')
}

async function assertStaleTreeReconciled(result, fixture) {
  assert.ifError(result.error)
  assert.notEqual(result.status, 0, 'the synthetic npm failure must fail the launcher closed')
  const output = `${result.stdout}\n${result.stderr}`
  assert.match(output, /Reconciling frontend dependencies from package-lock\.json \(npm ci\)/)
  assert.match(output, /No server was started/)
  assert.doesNotMatch(output, /Stack is up/)
  assert.equal((await readFile(fixture.npmLog, 'utf8')).trim(), 'ci --no-audit --no-fund')
  assert.equal(await readOptional(fixture.dotnetLog), null, 'the API started before npm ci succeeded')
}

test('both launchers pin the tracked Node range and lockfile-first preflight', async () => {
  const [powershell, bash, packageJson, nvmrc] = await Promise.all([
    readFile(powershellLauncher, 'utf8'),
    readFile(bashLauncher, 'utf8'),
    readFile(frontendPackage, 'utf8').then(JSON.parse),
    readFile(trackedNodeVersion, 'utf8'),
  ])
  const ps = normalise(powershell)
  const sh = normalise(bash)

  assert.equal(packageJson.engines.node, '>=24.13.1 <25')
  assert.equal(nvmrc.trim(), '24.13.1')

  assert.match(ps, /\$MinimumNodeVersion = \[version\]"24\.13\.1"/)
  assert.match(ps, /\$MaximumNodeVersion = \[version\]"25\.0\.0"/)
  assert.match(ps, /Resolve-RequiredApplication -Name "npm\.cmd"/)
  assert.match(ps, /& \$script:NpmCmd ci --no-audit --no-fund/)
  assert.match(ps, /Start-Process -FilePath \$script:NpmCmd/)
  assert.doesNotMatch(ps, /Start-Process -FilePath "npm"/)
  const psPreflightCall = ps.lastIndexOf('\nSync-FrontendDependencies\n')
  const psApiStart = ps.indexOf('Write-Step "Starting API (dotnet run)')
  assert.notEqual(psPreflightCall, -1, 'PowerShell preflight call is missing')
  assert.ok(psPreflightCall < psApiStart, 'PowerShell starts the API before dependency reconciliation')

  assert.match(sh, /node_major != 24 \|\| node_minor < 13/)
  assert.match(sh, /"\$NPM_BIN" ci --no-audit --no-fund/)
  const shPreflightCall = sh.indexOf('if ! ( cd "$FRONTEND_DIR" && "$NPM_BIN" ci --no-audit --no-fund )')
  const shApiStart = sh.indexOf('step "Starting API (dotnet run)')
  assert.notEqual(shPreflightCall, -1, 'Bash preflight call is missing')
  assert.ok(shPreflightCall < shApiStart, 'Bash starts the API before dependency reconciliation')
})

test(
  'PowerShell rejects unsupported Node boundaries before npm or dotnet',
  { skip: process.platform !== 'win32' },
  async (t) => {
    for (const version of ['24.13.0', '25.0.0']) {
      await t.test(version, async () => {
        const { fixture, result } = await runPowerShellFixture(version)
        try {
          await assertUnsupported(result, fixture, version)
        } finally {
          await rm(fixture.root, { recursive: true, force: true })
        }
      })
    }
  },
)

test(
  'PowerShell reconciles a stale existing tree before starting the API',
  { skip: process.platform !== 'win32' },
  async () => {
    const { fixture, result } = await runPowerShellFixture('24.13.1')
    try {
      await assertStaleTreeReconciled(result, fixture)
    } finally {
      await rm(fixture.root, { recursive: true, force: true })
    }
  },
)

const bash = findBash()

test('Bash rejects unsupported Node boundaries before npm or dotnet', { skip: !bash }, async (t) => {
  for (const version of ['24.13.0', '25.0.0']) {
    await t.test(version, async () => {
      const { fixture, result } = await runBashFixture(version, bash)
      try {
        await assertUnsupported(result, fixture, version)
      } finally {
        await rm(fixture.root, { recursive: true, force: true })
      }
    })
  }
})

test('Bash reconciles a stale existing tree before starting the API', { skip: !bash }, async () => {
  const { fixture, result } = await runBashFixture('24.13.1', bash)
  try {
    await assertStaleTreeReconciled(result, fixture)
  } finally {
    await rm(fixture.root, { recursive: true, force: true })
  }
})
