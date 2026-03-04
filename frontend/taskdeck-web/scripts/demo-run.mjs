#!/usr/bin/env node

import { TaskdeckApiClient, cleanupDemoBoards, ensureUser, getDemoConfig } from './demo-lib.mjs'

function parseArgs(argv) {
  const args = {
    scenario: null,
    clean: false,
    dryRun: false,
    list: false,
  }

  for (let i = 2; i < argv.length; i++) {
    const a = argv[i]
    if (!a) continue
    if (a === '--clean') args.clean = true
    else if (a === '--dry-run') args.dryRun = true
    else if (a === '--list') args.list = true
    else if (!a.startsWith('--') && !args.scenario) args.scenario = a
  }

  return args
}

const args = parseArgs(process.argv)

const scenarios = {
  'engineering-sprint': () => import('./scenarios/engineering-sprint.mjs'),
  'support-triage': () => import('./scenarios/support-triage.mjs'),
  'content-calendar': () => import('./scenarios/content-calendar.mjs'),
}

async function main() {
  if (args.list) {
    console.log('Available scenarios:')
    Object.keys(scenarios)
      .sort()
      .forEach((s) => console.log(`- ${s}`))
    console.log('\nUsage:')
    console.log('  npm run demo:run -- <scenario> [--clean] [--dry-run]')
    console.log('  npm run demo:run -- --list')
    process.exit(0)
  }

  const config = getDemoConfig()

  const scenarioName = args.scenario || 'engineering-sprint'
  const loader = scenarios[scenarioName]
  if (!loader) {
    console.error(`Unknown scenario: ${scenarioName}`)
    console.error('Run with --list to see available scenarios.')
    process.exit(1)
  }

  const api = new TaskdeckApiClient({ apiBaseUrl: config.apiBaseUrl })
  const demoLogin = await ensureUser(api, config.demoUser)
  const authed = api.withToken(demoLogin.token)

  if (args.clean) {
    const result = await cleanupDemoBoards(authed, { prefix: 'DEMO:', dryRun: args.dryRun })
    console.log(args.dryRun ? 'Would archive demo boards:' : 'Archived demo boards:')
    console.log(JSON.stringify(result, null, 2))
    if (args.dryRun) return
  }

  const mod = await loader()
  if (typeof mod.run !== 'function') {
    throw new Error(`Scenario module "${scenarioName}" must export async function run(ctx)`)
  }

  const summary = await mod.run({ api: authed, config })
  console.log('\nScenario complete.')
  if (summary) console.log(JSON.stringify(summary, null, 2))
}

main().catch((err) => {
  console.error(String(err?.stack || err))
  process.exit(1)
})
