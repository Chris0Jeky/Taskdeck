#!/usr/bin/env node
/**
 * demo-director.mjs
 *
 * One-command demo runner that orchestrates:
 * 1) seed
 * 2) scenario
 * 3) optional autopilot
 * 4) guided Playwright clickthrough
 * 5) artifact collection
 */

import { spawnSync } from 'node:child_process'
import fs from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import {
  resetDemoDirectorArtifacts,
  resetDemoDirectorE2EDb,
  resolveDemoDirectorRuntime,
} from './demo-director-lib.mjs'

const PLAYWRIGHT_SPAWN_MAX_BUFFER_BYTES = 50 * 1024 * 1024

function nowStamp() {
  const date = new Date()
  const pad = (value) => String(value).padStart(2, '0')
  const padMs = (value) => String(value).padStart(3, '0')
  const entropy = Math.random().toString(36).slice(2, 6)
  return (
    `${date.getFullYear()}${pad(date.getMonth() + 1)}${pad(date.getDate())}-` +
    `${pad(date.getHours())}${pad(date.getMinutes())}${pad(date.getSeconds())}${padMs(date.getMilliseconds())}-${entropy}`
  )
}

function parseArgs(argv) {
  const args = {
    runId: null,
    outputDir: null,
    e2eDb: null,
    resetE2EDb: false,
    freshServers: false,
    scenario: 'engineering-sprint',
    skipSeed: false,
    skipLlm: false,

    turns: 12,
    loop: 'mixed',
    brain: 'heuristic',
    intervalMs: 700,
    autopilotBoard: null,
    rngSeed: null,

    project: null,
    headed: false,
    playwrightArgs: [],
  }

  for (let i = 2; i < argv.length; i++) {
    const value = argv[i]

    if (value === '--run-id') args.runId = argv[++i]
    else if (value === '--output-dir') args.outputDir = argv[++i]
    else if (value === '--e2e-db') args.e2eDb = argv[++i]
    else if (value === '--reset-e2e-db') args.resetE2EDb = true
    else if (value === '--fresh-servers') args.freshServers = true
    else if (value === '--scenario') args.scenario = argv[++i] || args.scenario
    else if (value === '--skip-seed') args.skipSeed = true
    else if (value === '--skip-llm') args.skipLlm = true
    else if (value === '--turns') args.turns = Number(argv[++i] || args.turns)
    else if (value === '--loop') args.loop = argv[++i] || args.loop
    else if (value === '--brain') args.brain = argv[++i] || args.brain
    else if (value === '--interval-ms') args.intervalMs = Number(argv[++i] || args.intervalMs)
    else if (value === '--autopilot-board') args.autopilotBoard = argv[++i]
    else if (value === '--rng-seed') args.rngSeed = argv[++i]
    else if (value === '--project') args.project = argv[++i] || null
    else if (value === '--headed') args.headed = true
    else if (value === '--') {
      args.playwrightArgs = argv.slice(i + 1)
      break
    } else {
      args.playwrightArgs.push(value)
    }
  }

  if (!['queue', 'capture', 'mixed'].includes(args.loop)) {
    throw new Error(`Invalid --loop value: ${args.loop}`)
  }

  if (!['heuristic', 'taskdeck-chat'].includes(args.brain)) {
    throw new Error(`Invalid --brain value: ${args.brain}`)
  }

  if (!Number.isFinite(args.turns) || !Number.isInteger(args.turns) || args.turns < 0) {
    throw new Error(`Invalid --turns value: ${String(args.turns)}`)
  }

  if (!Number.isFinite(args.intervalMs) || !Number.isInteger(args.intervalMs) || args.intervalMs < 0) {
    throw new Error(`Invalid --interval-ms value: ${String(args.intervalMs)}`)
  }

  return args
}

async function walk(dirPath) {
  const output = []
  const entries = await fs.readdir(dirPath, { withFileTypes: true })
  for (const entry of entries) {
    const absolute = path.join(dirPath, entry.name)
    if (entry.isDirectory()) {
      output.push(...(await walk(absolute)))
    } else {
      output.push(absolute)
    }
  }
  return output
}

async function copyIfExists(sourcePath, destinationPath) {
  try {
    await fs.mkdir(path.dirname(destinationPath), { recursive: true })
    await fs.copyFile(sourcePath, destinationPath)
    return true
  } catch {
    return false
  }
}

function defaultBoardNameForScenario(scenarioId) {
  const normalized = String(scenarioId || '').trim().toLowerCase()
  if (normalized === 'engineering-sprint') return 'DEMO: Engineering Sprint'
  if (normalized === 'content-calendar') return 'DEMO: Content Calendar'
  if (normalized === 'support-triage') return 'DEMO: Support Triage'
  return 'DEMO: Engineering Sprint'
}

async function readNdjson(filePath) {
  try {
    const raw = await fs.readFile(filePath, 'utf8')
    const rows = []
    for (const line of raw.split('\n')) {
      const trimmed = line.trim()
      if (!trimmed) continue
      try {
        rows.push(JSON.parse(trimmed))
      } catch {
        // Ignore malformed trace rows.
      }
    }
    return rows
  } catch {
    return []
  }
}

function summarizeEvents(events) {
  const byType = {}
  for (const event of events) {
    const type = event?.type || 'unknown'
    byType[type] = (byType[type] || 0) + 1
  }

  const proposalEvents = events.filter((event) => event?.type === 'proposal.execute' || event?.type === 'queue.applied')
  const proposalsByKey = new Map()
  let fallbackKeyCounter = 0
  for (const event of proposalEvents) {
    const proposalId = typeof event?.proposalId === 'string' && event.proposalId.trim() ? event.proposalId.trim() : null
    const requestId = typeof event?.requestId === 'string' && event.requestId.trim() ? event.requestId.trim() : null
    const boardId = typeof event?.boardId === 'string' && event.boardId.trim() ? event.boardId.trim() : null
    const key = proposalId ? `proposal:${proposalId}` : requestId ? `request:${requestId}` : `event:${fallbackKeyCounter++}`

    const existing = proposalsByKey.get(key)
    if (!existing) {
      proposalsByKey.set(key, {
        ts: event.ts,
        type: event.type,
        proposalId,
        requestId,
        boardId,
      })
      continue
    }

    // Prefer proposal.execute metadata when both queue.applied and proposal.execute exist for one proposal.
    proposalsByKey.set(key, {
      ts: existing.ts || event.ts,
      type: existing.type === 'proposal.execute' ? existing.type : event.type,
      proposalId: existing.proposalId || proposalId,
      requestId: existing.requestId || requestId,
      boardId: existing.boardId || boardId,
    })
  }
  const proposals = Array.from(proposalsByKey.values())

  const captures = events
    .filter((event) => event?.type === 'capture.create' || event?.type === 'capture.triage.outcome')
    .map((event) => ({
      ts: event.ts,
      type: event.type,
      captureItemId: event.captureItemId || null,
      boardId: event.boardId || null,
      outcome: event.outcome || null,
      proposalId: event.proposalId || null,
    }))

  const autopilot = {
    starts: events.filter((event) => event?.type === 'autopilot.start').length,
    ends: events.filter((event) => event?.type === 'autopilot.end').length,
    turnsOk: events.filter((event) => event?.type === 'autopilot.turn.ok').length,
    turnsError: events.filter((event) => event?.type === 'autopilot.turn.error').length,
  }

  return { byType, proposals, captures, autopilot }
}

function listScenarioSteps(events, { limit = 80 } = {}) {
  const rows = events
    .filter((event) =>
      ['scenario.step.ok', 'scenario.step.skipped', 'scenario.step.error'].includes(String(event?.type || '')),
    )
    .map((event) => ({
      ts: event.ts,
      status: String(event.type || '').split('.').pop(),
      stepIndex: typeof event.stepIndex === 'number' ? event.stepIndex : null,
      stepLabel: event.stepLabel || null,
      stepType: event.stepType || null,
      reason: event.reason || null,
      error: event.error || null,
    }))

  rows.sort((left, right) => {
    if (left.stepIndex != null && right.stepIndex != null) {
      return left.stepIndex - right.stepIndex
    }
    return String(left.ts || '').localeCompare(String(right.ts || ''))
  })

  return rows.slice(0, limit)
}

function listAutopilotTurns(events, { limit = 12 } = {}) {
  const rows = events
    .filter((event) => String(event?.type || '') === 'autopilot.turn.start')
    .map((event) => ({
      ts: event.ts,
      turn: event.turn,
      decision: event.decision,
    }))

  rows.sort((left, right) => (left.turn || 0) - (right.turn || 0))
  return rows.slice(0, limit)
}

async function writeJson(filePath, value) {
  await fs.mkdir(path.dirname(filePath), { recursive: true })
  await fs.writeFile(filePath, JSON.stringify(value, null, 2), 'utf8')
}

async function main() {
  const args = parseArgs(process.argv)

  const __dirname = path.dirname(fileURLToPath(import.meta.url))
  const webRoot = path.resolve(__dirname, '..')
  const runtime = resolveDemoDirectorRuntime({
    webRoot,
    e2eDb: args.e2eDb,
    resetE2EDb: args.resetE2EDb,
    freshServers: args.freshServers,
  })

  const runId = args.runId || nowStamp()
  const artifactDir = path.resolve(args.outputDir || path.join(webRoot, 'demo-artifacts', `run-${runId}`))
  const logsDir = path.join(artifactDir, 'logs')
  const screenshotsDir = path.join(artifactDir, 'screenshots')
  const playwrightOutDir = path.join(artifactDir, 'playwright')

  await resetDemoDirectorArtifacts(artifactDir)
  await resetDemoDirectorE2EDb(runtime.e2eDbPath)
  await fs.mkdir(logsDir, { recursive: true })
  await fs.mkdir(screenshotsDir, { recursive: true })
  await fs.mkdir(playwrightOutDir, { recursive: true })

  const tracePath = path.join(artifactDir, 'trace.ndjson')
  const snapshotPath = path.join(artifactDir, 'snapshot.json')
  const autopilotBoard = args.autopilotBoard || defaultBoardNameForScenario(args.scenario)

  const env = {
    ...process.env,
    TASKDECK_RUN_DEMO: '1',
    TASKDECK_DEMO_DIRECTOR: '1',
    TASKDECK_DEMO_ARTIFACT_DIR: artifactDir,
    TASKDECK_DEMO_TRACE_PATH: tracePath,
    TASKDECK_DEMO_SNAPSHOT_PATH: snapshotPath,
    TASKDECK_DEMO_SCENARIO: args.scenario,
    TASKDECK_DEMO_SKIP_SEED: args.skipSeed ? '1' : '0',
    TASKDECK_DEMO_SKIP_LLM: args.skipLlm ? '1' : '0',
    TASKDECK_DEMO_AUTOPILOT_TURNS: String(args.turns || 0),
    TASKDECK_DEMO_AUTOPILOT_BOARD: autopilotBoard,
    TASKDECK_DEMO_AUTOPILOT_LOOP: args.loop,
    TASKDECK_DEMO_AUTOPILOT_BRAIN: args.brain,
    TASKDECK_DEMO_AUTOPILOT_INTERVAL_MS: String(args.intervalMs),
    TASKDECK_DEMO_AUTOPILOT_RNG_SEED: args.rngSeed || '',
    ...(runtime.e2eDbPath ? { TASKDECK_E2E_DB: runtime.e2eDbPath } : {}),
    ...(runtime.forceFreshServers ? { TASKDECK_E2E_REUSE_EXISTING_SERVER: '0' } : {}),
  }

  const pwArgs = [
    'playwright',
    'test',
    'tests/e2e/stakeholder-demo.spec.ts',
    '--output',
    playwrightOutDir,
    '--reporter',
    'line',
    ...args.playwrightArgs,
  ]
  if (args.project) {
    pwArgs.splice(3, 0, '--project', args.project)
  }
  if (args.headed) {
    pwArgs.push('--headed')
  }

  const command = process.platform === 'win32' ? process.env.ComSpec || 'cmd.exe' : 'npx'
  const commandArgs = process.platform === 'win32' ? ['/d', '/s', '/c', 'npx', ...pwArgs] : pwArgs

  const startedAt = new Date().toISOString()
  const playwrightResult = spawnSync(command, commandArgs, {
    cwd: webRoot,
    env,
    encoding: 'utf8',
    maxBuffer: PLAYWRIGHT_SPAWN_MAX_BUFFER_BYTES,
  })

  if (playwrightResult.error) {
    throw new Error(`Failed to launch Playwright via npx: ${String(playwrightResult.error.message || playwrightResult.error)}`)
  }

  const playwrightLog = `${playwrightResult.stdout || ''}${playwrightResult.stderr || ''}`
  await fs.writeFile(path.join(logsDir, 'playwright.log'), playwrightLog, 'utf8')

  const screenshots = []
  try {
    const files = await walk(playwrightOutDir)
    const pngs = files.filter((filePath) => filePath.toLowerCase().endsWith('.png'))
    pngs.sort((left, right) => path.basename(left).localeCompare(path.basename(right)))

    for (const sourcePath of pngs) {
      const fileName = path.basename(sourcePath)
      const destinationPath = path.join(screenshotsDir, fileName)
      if (await copyIfExists(sourcePath, destinationPath)) {
        screenshots.push({ name: fileName, path: `screenshots/${fileName}` })
      }
    }
  } catch {
    // Best-effort screenshot copy.
  }

  const endedAt = new Date().toISOString()
  const events = await readNdjson(tracePath)
  const summary = summarizeEvents(events)
  const scenarioSteps = listScenarioSteps(events)
  const autopilotTurns = listAutopilotTurns(events)
  const playwrightExitCode = playwrightResult.status
  const playwrightSignal = playwrightResult.signal || null
  const runStatus =
    playwrightExitCode === 0 ? 'ok' : playwrightSignal ? `error (signal ${playwrightSignal})` : 'error'

  const runSummary = {
    runId,
    startedAt,
    endedAt,
    status: runStatus,
    playwrightExitCode,
    playwrightSignal,
    scenario: args.scenario,
    skipSeed: args.skipSeed,
    skipLlm: args.skipLlm,
    autopilot: {
      enabled: args.turns > 0,
      turns: args.turns,
      board: autopilotBoard,
      loop: args.loop,
      brain: args.brain,
      intervalMs: args.intervalMs,
      rngSeed: args.rngSeed || null,
    },
    artifacts: {
      trace: 'trace.ndjson',
      snapshot: 'snapshot.json',
      logsDir: 'logs/',
      screenshotsDir: 'screenshots/',
      playwrightDir: 'playwright/',
    },
    screenshots,
    stats: {
      events: events.length,
      byType: summary.byType,
      autopilot: summary.autopilot,
      proposals: summary.proposals.length,
      captures: summary.captures.length,
    },
  }

  await writeJson(path.join(artifactDir, 'run-summary.json'), runSummary)

  const lines = []
  lines.push(`# Taskdeck demo run: ${runId}`)
  lines.push('')
  lines.push(`- Scenario: **${args.scenario}**${args.skipLlm ? ' (LLM steps skipped)' : ''}`)
  lines.push(`- Seed: ${args.skipSeed ? 'skipped' : 'enabled'}`)
  lines.push(
    `- Autopilot: ${
      args.turns > 0
        ? `enabled (${args.turns} turns, ${args.brain}/${args.loop}${args.rngSeed ? `, seed=${args.rngSeed}` : ''})`
        : 'disabled'
    }`,
  )
  lines.push(`- Playwright: project=${args.project || '(default)'}${args.headed ? ', headed' : ''}`)
  lines.push(`- Status: **${runSummary.status}** (exit=${playwrightExitCode}${playwrightSignal ? `, signal=${playwrightSignal}` : ''})`)
  lines.push('')
  lines.push('## Artifacts')
  lines.push('')
  lines.push('- Trace (NDJSON): `trace.ndjson`')
  lines.push('- Snapshot: `snapshot.json`')
  lines.push('- Logs: `logs/`')
  lines.push('- Raw Playwright output: `playwright/`')
  lines.push('')
  lines.push('## Walkthrough screenshots')
  lines.push('')

  if (screenshots.length === 0) {
    lines.push('_No screenshots copied. Check `playwright/` for raw output._')
  } else {
    for (const screenshot of screenshots.sort((left, right) => left.name.localeCompare(right.name))) {
      lines.push(`- [${screenshot.name}](${screenshot.path})`)
    }
  }

  lines.push('')
  lines.push('## Key counters')
  lines.push('')
  lines.push(`- Events in trace: ${events.length}`)
  lines.push(`- Proposals executed: ${summary.proposals.length}`)
  lines.push(`- Capture items (create/outcome events): ${summary.captures.length}`)
  lines.push(`- Autopilot turns OK / error: ${summary.autopilot.turnsOk} / ${summary.autopilot.turnsError}`)
  lines.push('')

  if (scenarioSteps.length > 0) {
    lines.push('## Scenario steps')
    lines.push('')
    for (const step of scenarioSteps) {
      const status = step.status === 'ok' ? 'ok' : step.status === 'skipped' ? 'skipped' : 'error'
      const label = step.stepLabel || `step ${step.stepIndex ?? '?'}`
      const extra = step.reason ? ` - ${step.reason}` : step.error ? ` - ${step.error}` : ''
      lines.push(`- [${status}] ${label}${extra}`)
    }
    lines.push('')
  }

  if (autopilotTurns.length > 0) {
    lines.push('## Autopilot sample turns')
    lines.push('')
    for (const turn of autopilotTurns) {
      const decision = turn.decision || {}
      if (decision.kind === 'instruction') {
        lines.push(`- Turn ${turn.turn}: instruction - ${String(decision.instruction || '').slice(0, 160)}`)
      } else if (decision.kind === 'capture') {
        lines.push(`- Turn ${turn.turn}: capture - ${String(decision.text || '').slice(0, 160)}`)
      } else {
        lines.push(`- Turn ${turn.turn}: ${JSON.stringify(decision).slice(0, 160)}`)
      }
    }
    lines.push('')
  }

  if (summary.proposals.length > 0) {
    lines.push('## Proposals executed')
    lines.push('')
    for (const proposal of summary.proposals.slice(0, 30)) {
      const proposalToken = proposal.proposalId ? `proposal=${proposal.proposalId}` : ''
      const requestToken = proposal.requestId ? `request=${proposal.requestId}` : ''
      const boardToken = proposal.boardId ? `board=${proposal.boardId}` : ''
      const meta = [proposalToken, requestToken, boardToken].filter(Boolean).join(' ')
      lines.push(`- ${proposal.ts || ''} ${proposal.type}${meta ? ` - ${meta}` : ''}`)
    }
    lines.push('')
  }

  lines.push('## Next steps')
  lines.push('')
  lines.push('- Use `snapshot.json` to verify that key surfaces have data.')
  lines.push('- Use `trace.ndjson` to inspect scenario/autopilot behavior.')
  lines.push('- For CI, use `--rng-seed <fixed>` and `--skip-llm` for deterministic runs.')
  lines.push('')

  await fs.writeFile(path.join(artifactDir, 'README.md'), lines.join('\n'), 'utf8')

  process.exitCode = typeof playwrightExitCode === 'number' ? playwrightExitCode : 1
  console.log(`\nDemo artifacts written to: ${artifactDir}`)
  console.log(`Status: ${runSummary.status} (exit=${playwrightExitCode}${playwrightSignal ? `, signal=${playwrightSignal}` : ''})`)
}

main().catch((err) => {
  console.error(err)
  process.exit(1)
})
