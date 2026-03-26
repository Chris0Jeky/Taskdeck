function readOptionValue(argv, index, optionName) {
  const nextValue = argv[index + 1]
  if (!nextValue || nextValue === '--' || nextValue.startsWith('--')) {
    throw new Error(`Option ${optionName} requires a value`)
  }

  return nextValue
}

export function parseDemoDirectorArgs(argv) {
  const args = {
    runId: null,
    outputDir: null,
    e2eDb: null,
    resetE2EDb: false,
    freshServers: false,
    scenario: 'client-onboarding',
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

    if (value === '--run-id') args.runId = readOptionValue(argv, i++, '--run-id')
    else if (value === '--output-dir') args.outputDir = readOptionValue(argv, i++, '--output-dir')
    else if (value === '--e2e-db') args.e2eDb = readOptionValue(argv, i++, '--e2e-db')
    else if (value === '--reset-e2e-db') args.resetE2EDb = true
    else if (value === '--fresh-servers') args.freshServers = true
    else if (value === '--scenario') args.scenario = readOptionValue(argv, i++, '--scenario')
    else if (value === '--skip-seed') args.skipSeed = true
    else if (value === '--skip-llm') args.skipLlm = true
    else if (value === '--turns') args.turns = Number(readOptionValue(argv, i++, '--turns'))
    else if (value === '--loop') args.loop = readOptionValue(argv, i++, '--loop')
    else if (value === '--brain') args.brain = readOptionValue(argv, i++, '--brain')
    else if (value === '--interval-ms') args.intervalMs = Number(readOptionValue(argv, i++, '--interval-ms'))
    else if (value === '--autopilot-board') args.autopilotBoard = readOptionValue(argv, i++, '--autopilot-board')
    else if (value === '--rng-seed') args.rngSeed = readOptionValue(argv, i++, '--rng-seed')
    else if (value === '--project') args.project = readOptionValue(argv, i++, '--project')
    else if (value === '--headed') args.headed = true
    else if (value === '--') {
      args.playwrightArgs = argv.slice(i + 1)
      break
    } else if (value.startsWith('--')) throw new Error(`Unknown demo-director option: ${value}`)
    else throw new Error(`Unexpected argument before "--": ${value}`)
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
