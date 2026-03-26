import { parseTrueishEnv } from './scripts/demo-shared.mjs'

type DemoProvider = 'OpenAI' | 'Gemini'

const deterministicMockLlmEnv: Record<string, string> = {
  Llm__EnableLiveProviders: 'false',
  Llm__AllowLiveProvidersInDevelopment: 'false',
  Llm__Provider: 'Mock',
}

export function resolvePlaywrightBackendLlmEnv(env: NodeJS.ProcessEnv): Record<string, string> {
  return {
    ...deterministicMockLlmEnv,
    ...resolveDemoBackendLlmEnv(env),
  }
}

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
  const isLiveLlmTestRun = parseTrueishEnv(env.TASKDECK_RUN_LIVE_LLM_TESTS)
  if (!isDemoRun && !isLiveLlmTestRun) {
    return false
  }

  if (parseTrueishEnv(env.TASKDECK_DEMO_SKIP_LLM)) {
    return false
  }

  if (parseTrueishEnv(env.TASKDECK_DEMO_DISABLE_LIVE_LLM)) {
    return false
  }

  const explicitDemoProvider = normalizeProvider(env.TASKDECK_DEMO_LLM_PROVIDER)
  if (explicitDemoProvider === 'Mock') {
    return false
  }

  return true
}

function resolveDemoProvider(env: NodeJS.ProcessEnv): DemoProvider | null {
  const explicitDemoProvider = normalizeProvider(env.TASKDECK_DEMO_LLM_PROVIDER)
  if (explicitDemoProvider === 'Mock') {
    return null
  }

  if (explicitDemoProvider) {
    return explicitDemoProvider
  }

  const baseProvider = normalizeProvider(env.Llm__Provider)
  if (baseProvider === 'Gemini' && hasGeminiApiKey(env)) {
    return 'Gemini'
  }

  if (baseProvider === 'OpenAI' && hasOpenAiApiKey(env)) {
    return 'OpenAI'
  }

  if (hasGeminiApiKey(env)) {
    return 'Gemini'
  }

  if (hasOpenAiApiKey(env)) {
    return 'OpenAI'
  }

  return null
}

function hasGeminiApiKey(env: NodeJS.ProcessEnv): boolean {
  return firstNonEmpty(env.Llm__Gemini__ApiKey, env.TASKDECK_DEMO_GEMINI_API_KEY, env.GEMINI_API_KEY) !== null
}

function hasOpenAiApiKey(env: NodeJS.ProcessEnv): boolean {
  return firstNonEmpty(env.Llm__OpenAi__ApiKey, env.TASKDECK_DEMO_OPENAI_API_KEY, env.OPENAI_API_KEY) !== null
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

