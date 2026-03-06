#!/usr/bin/env node

import path from 'node:path'
import { fileURLToPath } from 'node:url'

import { TaskdeckApiClient, cleanupDemoBoards, ensureUser, getDemoConfig } from './demo-lib.mjs'
import { parseDemoRunArgs } from './demo-run-lib.mjs'
import { listJsonScenarioIds, loadJsonScenario, runJsonScenario } from './scenario-json-runner.mjs'

const jsScenarios = {
  'engineering-sprint': () => import('./scenarios/engineering-sprint.mjs'),
  'support-triage': () => import('./scenarios/support-triage.mjs'),
  'content-calendar': () => import('./scenarios/content-calendar.mjs'),
}

export async function main(argv = process.argv) {
  const args = parseDemoRunArgs(argv)
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
    console.log('  npm run demo:run -- [--clean [--dry-run]] <scenario> [--skip-llm] [--continue-on-error]')
    console.log('  npm run demo:run -- --list')
    process.exit(0)
  }

  const config = getDemoConfig()

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

const entryPath = process.argv[1] ? path.resolve(process.argv[1]) : null
const currentPath = fileURLToPath(import.meta.url)

if (entryPath && currentPath === entryPath) {
  main().catch((err) => {
    console.error(String(err?.stack || err))
    process.exit(1)
  })
}
