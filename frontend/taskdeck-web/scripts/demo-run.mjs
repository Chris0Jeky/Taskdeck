#!/usr/bin/env node

import { TaskdeckApiClient, cleanupDemoBoards, ensureUser, getDemoConfig } from './demo-lib.mjs'
import { listJsonScenarioIds, loadJsonScenario, runJsonScenario } from './scenario-json-runner.mjs'

function parseArgs(argv) {
  const args = {
    scenario: null,
    clean: false,
    dryRun: false,
    list: false,
    skipLlm: false,
    continueOnError: false,
  }

  for (let i = 2; i < argv.length; i++) {
    const value = argv[i]
    if (!value) continue

    if (value === '--clean') args.clean = true
    else if (value === '--dry-run') args.dryRun = true
    else if (value === '--list') args.list = true
    else if (value === '--skip-llm') args.skipLlm = true
    else if (value === '--continue-on-error') args.continueOnError = true
    else if (!value.startsWith('--') && !args.scenario) args.scenario = value
  }

  return args
}

const args = parseArgs(process.argv)
const config = getDemoConfig()

const jsScenarios = {
  'engineering-sprint': () => import('./scenarios/engineering-sprint.mjs'),
  'support-triage': () => import('./scenarios/support-triage.mjs'),
  'content-calendar': () => import('./scenarios/content-calendar.mjs'),
}

async function main() {
  const jsonScenarioIds = await listJsonScenarioIds()

  if (args.list) {
    console.log('Available JSON scenarios:')
    jsonScenarioIds.forEach((scenarioId) => console.log(`- ${scenarioId}`))

    const jsOnlyScenarios = Object.keys(jsScenarios)
      .filter((scenarioId) => !jsonScenarioIds.includes(scenarioId))
      .sort()

    if (jsOnlyScenarios.length > 0) {
      console.log('\nLegacy JS scenarios:')
      jsOnlyScenarios.forEach((scenarioId) => console.log(`- ${scenarioId}`))
    }

    console.log('\nUsage:')
    console.log('  npm run demo:run -- <scenario> [--clean] [--dry-run] [--skip-llm] [--continue-on-error]')
    console.log('  npm run demo:run -- --list')
    process.exit(0)
  }

  const scenarioName = args.scenario || 'engineering-sprint'

  const api = new TaskdeckApiClient({ apiBaseUrl: config.apiBaseUrl })
  const demoLogin = await ensureUser(api, config.demoUser)
  const authed = api.withToken(demoLogin.token)

  if (args.clean) {
    const result = await cleanupDemoBoards(authed, { prefix: 'DEMO:', dryRun: args.dryRun })
    console.log(args.dryRun ? 'Would archive demo boards:' : 'Archived demo boards:')
    console.log(JSON.stringify(result, null, 2))
    if (args.dryRun) return
  }

  const shouldRunJson = jsonScenarioIds.includes(scenarioName) || scenarioName.endsWith('.json')
  if (shouldRunJson) {
    const scenario = await loadJsonScenario(scenarioName)
    const summary = await runJsonScenario({
      api: authed,
      config,
      scenario,
      options: {
        skipLlm: args.skipLlm,
        continueOnError: args.continueOnError,
      },
    })

    console.log('\nScenario complete.')
    if (summary) console.log(JSON.stringify(summary, null, 2))
    return
  }

  const jsLoader = jsScenarios[scenarioName]
  if (!jsLoader) {
    console.error(`Unknown scenario: ${scenarioName}`)
    console.error('Run with --list to see available scenarios.')
    process.exit(1)
  }

  const jsScenario = await jsLoader()
  if (typeof jsScenario.run !== 'function') {
    throw new Error(`Scenario module "${scenarioName}" must export async function run(ctx)`)
  }

  const summary = await jsScenario.run({ api: authed, config })
  console.log('\nScenario complete.')
  if (summary) console.log(JSON.stringify(summary, null, 2))
}

main().catch((err) => {
  console.error(String(err?.stack || err))
  process.exit(1)
})
