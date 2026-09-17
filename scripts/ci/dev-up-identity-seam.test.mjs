import assert from 'node:assert/strict'
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { spawnSync } from 'node:child_process'
import { test } from 'node:test'

const launcherSource = await readFile(new URL('../dev-up.sh', import.meta.url), 'utf8')

function extractBashFunction(name) {
  const lines = launcherSource.replaceAll('\r\n', '\n').split('\n')
  const start = lines.findIndex(line => line === `${name}() {`)
  assert.notEqual(start, -1, `missing Bash function: ${name}`)

  const nextFunction = lines.findIndex(
    (line, index) => index > start && /^[A-Za-z_][A-Za-z0-9_]*\(\) \{$/.test(line),
  )
  return `${lines.slice(start, nextFunction === -1 ? lines.length : nextFunction).join('\n')}\n`
}

const identityFunctions = [
  'wait_for_identity_exit',
  'stop_exact_process',
  'stop_recorded_process',
].map(extractBashFunction).join('\n')

function fixtureSource() {
  return `#!/usr/bin/env bash
set -euo pipefail

${identityFunctions}

declare -A PROCESS_STATE=([100]=match [200]=match)
declare -A SLEEP_COUNTS=([100]=0 [200]=0)
KILL_LOG_FILE="$1"
LAST_PROBE_FILE="$2"

warn() { :; }
step() { :; }

capture_descendant_records() {
  printf '%s\\n' '200|child|child-token'
}

process_identity_status() {
  local pid="$1"
  printf '%s' "$pid" > "$LAST_PROBE_FILE"
  printf '%s\\n' "\${PROCESS_STATE[$pid]:-missing}"
}

sleep() {
  local duration="\${1:-}"
  [[ "$duration" == '0.1' ]] || {
    printf 'unexpected sleep duration: %s\\n' "$duration" >&2
    return 91
  }

  local pid
  pid="$(cat "$LAST_PROBE_FILE")"
  SLEEP_COUNTS[$pid]=$(( \${SLEEP_COUNTS[$pid]:-0} + 1 ))
}

kill() {
  printf '%s\\n' "$*" >> "$KILL_LOG_FILE"
  case "\${1:-}" in
    -TERM)
      ;;
    -KILL)
      PROCESS_STATE[$2]=missing
      ;;
    *)
      printf 'unexpected signal invocation: %s\\n' "$*" >&2
      return 92
      ;;
  esac
}

stop_recorded_process 'API' '100' 'root' 'root-token'

printf 'sleep-100=%s\\n' "\${SLEEP_COUNTS[100]:-0}"
printf 'sleep-200=%s\\n' "\${SLEEP_COUNTS[200]:-0}"
printf 'kills='
paste -sd '|' "$KILL_LOG_FILE"
printf '\\n'
`
}

async function runIdentityFixture() {
  const root = await mkdtemp(join(tmpdir(), 'taskdeck-dev-up-identity-'))
  try {
    const harnessPath = join(root, 'identity-fixture.sh')
    const killLogPath = join(root, 'kill.log')
    const lastProbePath = join(root, 'last-probe.txt')
    await writeFile(harnessPath, fixtureSource(), 'utf8')
    await writeFile(killLogPath, '', 'utf8')
    await writeFile(lastProbePath, '', 'utf8')

    const result = spawnSync('bash', [harnessPath, killLogPath, lastProbePath], {
      encoding: 'utf8',
      timeout: 10_000,
    })
    assert.equal(
      result.status,
      0,
      `identity fixture failed\nstdout:\n${result.stdout}\nstderr:\n${result.stderr}`,
    )

    const values = new Map()
    for (const line of result.stdout.trim().split('\n')) {
      const separator = line.indexOf('=')
      if (separator >= 0) values.set(line.slice(0, separator), line.slice(separator + 1))
    }
    return values
  } finally {
    await rm(root, { recursive: true, force: true })
  }
}

test('stop_exact_process preserves the full TERM grace period before KILL', async () => {
  const values = await runIdentityFixture()
  assert.equal(values.get('sleep-200'), '100', 'descendant TERM grace was shortened')
  assert.equal(values.get('sleep-100'), '100', 'root TERM grace was shortened')
})

test('signals retain the exact recorded PID target', async () => {
  const values = await runIdentityFixture()
  const invocations = values.get('kills').split('|')
  assert.deepEqual(invocations, ['-TERM 200', '-KILL 200', '-TERM 100', '-KILL 100'])
  assert.ok(invocations.every(entry => / -(?:TERM|KILL) /.test(` ${entry} `)))
})

test('stop_recorded_process reaches KILL through descendant-first traversal', async () => {
  const values = await runIdentityFixture()
  const invocations = values.get('kills').split('|')
  assert.deepEqual(invocations.slice(0, 2), ['-TERM 200', '-KILL 200'])
  assert.deepEqual(invocations.slice(2), ['-TERM 100', '-KILL 100'])
})
