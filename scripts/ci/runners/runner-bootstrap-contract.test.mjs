import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const sourceUrl = (name) => new URL(name, import.meta.url)
const readSource = (name) => readFileSync(sourceUrl(name), 'utf8').replace(/\r\n/g, '\n')

const linuxBootstrap = readSource('bootstrap-linux.sh')
const windowsBootstrap = readSource('bootstrap-windows.ps1')
const linuxCleanup = readSource('cleanup-linux.sh')
const windowsCleanup = readSource('cleanup-windows.ps1')

const executableSources = [linuxBootstrap, windowsBootstrap, linuxCleanup, windowsCleanup]

test('runner scripts expose Check by default and privileged Apply explicitly', () => {
  assert.match(linuxBootstrap, /ACTION='Check'/)
  assert.match(linuxBootstrap, /Check\|Apply/)
  assert.match(linuxBootstrap, /--dry-run/)
  assert.match(linuxBootstrap, /EUID[^\n]+-ne 0/)

  assert.match(windowsBootstrap, /\[ValidateSet\('Check', 'Apply'\)\]/)
  assert.match(windowsBootstrap, /\[string\]\$Action = 'Check'/)
  assert.match(windowsBootstrap, /SupportsShouldProcess = \$true/)
  assert.match(windowsBootstrap, /WindowsPrincipal/)
  assert.match(windowsBootstrap, /Administrator/)
})

test('bootstraps pin the supported architecture and toolchain without installing packages', () => {
  for (const source of [linuxBootstrap, windowsBootstrap]) {
    assert.match(source, /24\.13\.1/)
    assert.match(source, /8\.0\.415/)
    assert.match(source, /x64/i)
    assert.match(source, /git/i)
    assert.doesNotMatch(source, /\b(?:apt(?:-get)?|dnf|yum|zypper|winget|choco)\b/i)
    assert.doesNotMatch(source, /\b(?:curl|wget|Invoke-WebRequest|Start-BitsTransfer)\b/i)
    assert.doesNotMatch(source, /(?:@|:|\binstall\s+)latest\b/i)
  }

  assert.match(linuxBootstrap, /SecurityOptions/)
  assert.match(linuxBootstrap, /rootless/)
  assert.match(linuxBootstrap, /docker buildx version/)
  assert.match(linuxBootstrap, /docker buildx prune --help/)
  assert.match(linuxBootstrap, /--max-used-space/)
})

test('Linux creates only the fixed locked, unprivileged runner account', () => {
  assert.match(linuxBootstrap, /RUNNER_ACCOUNT='taskdeck-runner'/)
  assert.match(linuxBootstrap, /useradd/)
  assert.match(linuxBootstrap, /passwd[^\n]+--lock/)
  assert.match(linuxBootstrap, /sudo|wheel/)
  assert.match(linuxBootstrap, /docker/)
  assert.match(linuxBootstrap, /id -u/)
  assert.match(linuxBootstrap, /id -G/)
  assert.match(linuxBootstrap, /group_ids\[@\][^\n]+-eq 1/)
  assert.doesNotMatch(linuxBootstrap, /usermod[^\n]+(?:sudo|wheel|docker)/)
})

test('Windows accepts only an existing local non-admin runner account', () => {
  assert.match(windowsBootstrap, /Get-LocalUser/)
  assert.match(windowsBootstrap, /Get-LocalGroupMember/)
  assert.match(windowsBootstrap, /S-1-5-32-544/)
  assert.match(windowsBootstrap, /S-1-5-32-545/)
  assert.match(windowsBootstrap, /memberships\.Count -ne 1/)
  assert.match(windowsBootstrap, /foreach \(\$member in \$members\)/)
  assert.doesNotMatch(windowsBootstrap, /\$members\.SID/)
  assert.doesNotMatch(windowsBootstrap, /New-LocalUser|Set-LocalUser|ConvertTo-SecureString/)
  assert.doesNotMatch(windowsBootstrap, /\bPassword\b/)
})

test('policy, hooks, work, temp and caches use fixed contained guest-local roots', () => {
  assert.match(linuxBootstrap, /\/etc\/taskdeck-runner/)
  assert.match(linuxBootstrap, /\/var\/lib\/taskdeck-runner\/work/)
  assert.match(linuxBootstrap, /\/var\/lib\/taskdeck-runner\/home/)
  assert.match(linuxBootstrap, /\/var\/lib\/taskdeck-runner\/temp/)
  assert.match(linuxBootstrap, /\/var\/cache\/taskdeck-runner/)
  assert.match(linuxBootstrap, /realpath -m/)
  assert.match(linuxBootstrap, /findmnt/)
  assert.match(linuxBootstrap, /9p\|virtiofs\|cifs\|nfs/)

  assert.match(windowsBootstrap, /C:\\ProgramData\\TaskdeckRunner\\Policy/)
  assert.match(windowsBootstrap, /C:\\TaskdeckRunner\\Work/)
  assert.match(windowsBootstrap, /C:\\TaskdeckRunner\\Profile/)
  assert.match(windowsBootstrap, /C:\\TaskdeckRunner\\ProfileState/)
  assert.match(windowsBootstrap, /C:\\TaskdeckRunner\\Temp/)
  assert.match(windowsBootstrap, /C:\\TaskdeckRunner\\Cache/)
  assert.match(windowsBootstrap, /GetPathRoot/)
  assert.match(windowsBootstrap, /ReparsePoint/)
  assert.match(windowsBootstrap, /DriveType[^\n]+Fixed/)

  for (const source of [windowsBootstrap, windowsCleanup]) {
    assert.match(source, /StartsWith\('\\\\'/)
    assert.match(source, /\^\[A-Za-z\]:\\\\/)
    assert.doesNotMatch(source, /IsPathFullyQualified/)
  }
})

test('cleanup trusts only immutable fixed policy and removes children, never roots', () => {
  assert.match(linuxCleanup, /CONFIG_PATH='\/etc\/taskdeck-runner\/policy\.conf'/)
  assert.match(linuxCleanup, /stat -c/)
  assert.match(linuxCleanup, /-mindepth 1/)
  assert.match(linuxCleanup, /-maxdepth 1/)
  assert.match(linuxCleanup, /--one-file-system/)
  assert.doesNotMatch(linuxCleanup, /rm\s+-rf\s+--?\s+"?\$(?:WORK|TEMP|CACHE|ROOT)/)
  assert.match(linuxCleanup, /clear_children "\$HOME_ROOT" 'home_clear'/)

  assert.match(windowsCleanup, /C:\\ProgramData\\TaskdeckRunner\\Policy\\RunnerPolicy\.psd1/)
  assert.match(windowsCleanup, /GetAccessRules/)
  assert.match(windowsCleanup, /Get-ChildItem[^\n]+-LiteralPath/)
  assert.match(windowsCleanup, /Remove-Item[^\n]+-LiteralPath/)
  assert.match(windowsCleanup, /Clear-Children -Root \$profileStateRoot/)
  assert.match(windowsCleanup, /child_reparse/)
  assert.doesNotMatch(windowsCleanup, /Clear-Children -Root \$accountProfileRoot/)
  assert.doesNotMatch(windowsCleanup, /\$members\.SID/)
  assert.match(windowsCleanup, /\$PSCommandPath/)
  assert.doesNotMatch(windowsCleanup, /Remove-Item[^\n]+(?:WorkRoot|TempRoot|CacheRoot)/)

  for (const source of [linuxCleanup, windowsCleanup]) {
    assert.doesNotMatch(source, /RUNNER_WORKSPACE|GITHUB_WORKSPACE|RUNNER_TEMP/)
  }
  assert.doesNotMatch(windowsCleanup, /\$env:(?:TEMP|TMP|HOME|USERPROFILE)/i)
  assert.doesNotMatch(linuxCleanup, /\$(?:TMPDIR|HOME)\b/)
})

test('cleanup is bounded, supports a dry run and treats every failure as nonzero', () => {
  assert.match(linuxCleanup, /\/usr\/bin\/timeout/)
  assert.match(linuxCleanup, /--dry-run/)
  assert.match(linuxCleanup, /exit 1/)
  assert.doesNotMatch(linuxCleanup, /\|\|\s*true|set \+e/)
  assert.doesNotMatch(linuxCleanup, /docker system prune|--volumes/)
  assert.match(linuxCleanup, /docker_runner buildx prune/)
  assert.match(linuxCleanup, /until=168h/)
  assert.match(linuxCleanup, /--max-used-space/)
  assert.match(linuxCleanup, /CONTAINER_STATE_MAX_BYTES/)
  assert.match(linuxCleanup, /container rm --force/)
  assert.match(linuxCleanup, /volume rm --force/)

  assert.match(windowsCleanup, /SupportsShouldProcess = \$true/)
  assert.match(windowsCleanup, /Start-Job/)
  assert.match(windowsCleanup, /Wait-Job[^\n]+-Timeout 120/)
  assert.match(windowsCleanup, /exit 1/)
  assert.doesNotMatch(windowsCleanup, /SilentlyContinue|catch\s*\{\s*\}/)
})

test('scripts exclude runner registration, credentials and download shortcuts', () => {
  const dangerous = [
    /config\.(?:sh|cmd)/i,
    /--token\b/i,
    /RUNNER_TOKEN/i,
    /\bgh\s+(?:api|auth)\b/i,
    /Authorization:/i,
    /persist-credentials/i,
    /\b(?:PAT|password|secret)\b/i,
    /curl[^\n|]*\|/i,
  ]

  for (const source of executableSources) {
    for (const pattern of dangerous) assert.doesNotMatch(source, pattern)
  }
})

test('cleanup output is limited to stable codes and counts', () => {
  assert.match(linuxCleanup, /RUNNER_CLEANUP (?:OK|ERROR|ACTION)/)
  assert.doesNotMatch(linuxCleanup, /(?:printf|echo)[^\n]+\$(?:WORK|TEMP|CACHE|CONFIG|RUNNER_ACCOUNT)/)
  assert.match(windowsCleanup, /RUNNER_CLEANUP \$Text/)
  assert.doesNotMatch(windowsCleanup, /Write-(?:Output|Host)[^\n]+\$(?:.*Path|.*Root|.*Account|env:)/i)
})

test('Linux runner entrypoints are tracked as executable', () => {
  const scriptDirectory = fileURLToPath(new URL('.', import.meta.url))
  const result = spawnSync(
    'git',
    ['ls-files', '--stage', '--full-name', '--', 'bootstrap-linux.sh', 'cleanup-linux.sh'],
    { cwd: scriptDirectory, encoding: 'utf8' },
  )
  assert.equal(result.status, 0, result.stderr)
  for (const path of [
    'scripts/ci/runners/bootstrap-linux.sh',
    'scripts/ci/runners/cleanup-linux.sh',
  ]) {
    assert.ok(
      result.stdout.split(/\r?\n/).some((line) => line.startsWith('100755 ') && line.endsWith(`\t${path}`)),
      `${path} must be tracked as mode 100755`,
    )
  }
})

const bashProbe = spawnSync('bash', ['--version'], { encoding: 'utf8' })
for (const script of ['bootstrap-linux.sh', 'cleanup-linux.sh']) {
  test(`${script} parses as Bash`, { skip: bashProbe.status !== 0 }, () => {
    const scriptDirectory = fileURLToPath(new URL('.', import.meta.url))
    const result = spawnSync('bash', ['-n', script], { cwd: scriptDirectory, encoding: 'utf8' })
    assert.equal(result.status, 0, result.stderr)
  })
}
