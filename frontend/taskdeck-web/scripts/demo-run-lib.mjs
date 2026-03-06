function assert(condition, message) {
  if (!condition) throw new Error(message)
}

export function parseDemoRunArgs(argv) {
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
    else if (value.startsWith('--')) throw new Error(`Unknown demo-run option: ${value}`)
    else throw new Error(`Unexpected extra argument: ${value}`)
  }

  assert(!args.dryRun || args.clean, '--dry-run is only supported together with --clean')

  return args
}
