type DemoProvider = 'OpenAI' | 'Gemini'

export function resolveDemoBackendLlmEnv(env: NodeJS.ProcessEnv): Record<string, string> {
  if (!shouldEnableLiveDemoLlm(env)) {
    return {}
  }

  const provider = resolveDemoProvider(env)
  if (!provider) {
    return {}
  }

  const liveEnv: Record<string, string> = {
    Llm__EnableLiveProviders: 'true',
    Llm__AllowLiveProvidersInDevelopment: 'true',
    Llm__Provider: provider,
  }

  if (provider === 'Gemini') {
    const apiKey = firstNonEmpty(env.Llm__Gemini__ApiKey, env.TASKDECK_DEMO_GEMINI_API_KEY, env.GEMINI_API_KEY)
    if (!apiKey) {
      return {}
    }

    liveEnv.Llm__Gemini__ApiKey = apiKey

    const model = firstNonEmpty(env.TASKDECK_DEMO_GEMINI_MODEL, env.Llm__Gemini__Model)
    if (model) {
      liveEnv.Llm__Gemini__Model = model
    }

    return liveEnv
  }

  const apiKey = firstNonEmpty(env.Llm__OpenAi__ApiKey, env.TASKDECK_DEMO_OPENAI_API_KEY, env.OPENAI_API_KEY)
  if (!apiKey) {
    return {}
  }

  liveEnv.Llm__OpenAi__ApiKey = apiKey

  const model = firstNonEmpty(env.TASKDECK_DEMO_OPENAI_MODEL, env.Llm__OpenAi__Model)
  if (model) {
    liveEnv.Llm__OpenAi__Model = model
  }

  return liveEnv
}

function shouldEnableLiveDemoLlm(env: NodeJS.ProcessEnv): boolean {
  const isDemoRun = parseTrueishEnv(env.TASKDECK_RUN_DEMO) || parseTrueishEnv(env.TASKDECK_DEMO_DIRECTOR)
  if (!isDemoRun) {
    return false
  }

  if (parseTrueishEnv(env.TASKDECK_DEMO_SKIP_LLM)) {
    return false
  }

  if (parseTrueishEnv(env.TASKDECK_DEMO_DISABLE_LIVE_LLM)) {
    return false
  }

  if (normalizeProvider(env.Llm__Provider) === 'Mock') {
    return false
  }

  return true
}

function resolveDemoProvider(env: NodeJS.ProcessEnv): DemoProvider | null {
  const explicitProvider =
    normalizeProvider(env.TASKDECK_DEMO_LLM_PROVIDER) ??
    normalizeProvider(env.Llm__Provider)
  if (explicitProvider === 'Mock') {
    return null
  }

  if (explicitProvider) {
    return explicitProvider
  }

  if (firstNonEmpty(env.Llm__Gemini__ApiKey, env.TASKDECK_DEMO_GEMINI_API_KEY, env.GEMINI_API_KEY)) {
    return 'Gemini'
  }

  if (firstNonEmpty(env.Llm__OpenAi__ApiKey, env.TASKDECK_DEMO_OPENAI_API_KEY, env.OPENAI_API_KEY)) {
    return 'OpenAI'
  }

  return null
}

function normalizeProvider(value: string | undefined): DemoProvider | 'Mock' | null {
  const normalized = value?.trim().toLowerCase()
  if (!normalized) {
    return null
  }

  if (normalized === 'gemini') {
    return 'Gemini'
  }

  if (normalized === 'openai') {
    return 'OpenAI'
  }

  if (normalized === 'mock') {
    return 'Mock'
  }

  return null
}

function firstNonEmpty(...values: Array<string | undefined>): string | null {
  for (const value of values) {
    if (typeof value === 'string' && value.trim().length > 0) {
      return value.trim()
    }
  }

  return null
}

function parseTrueishEnv(value: string | undefined): boolean {
  const normalized = value?.trim().toLowerCase()
  return normalized === '1' || normalized === 'true' || normalized === 'yes' || normalized === 'on'
}
