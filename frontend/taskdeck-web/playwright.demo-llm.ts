import { parseTrueishEnv } from './scripts/demo-shared.mjs'

type DemoProvider = 'OpenAI'

const retiredGeminiConfigurationMessage =
  'Gemini provider support was removed from Taskdeck demos. Use OpenAI or Mock and remove Taskdeck-specific Gemini provider settings.'

const deterministicMockLlmEnv: Record<string, string> = {
  Llm__EnableLiveProviders: 'false',
  Llm__AllowLiveProvidersInDevelopment: 'false',
  Llm__Provider: 'Mock',
}

export function resolvePlaywrightBackendLlmEnv(env: NodeJS.ProcessEnv): Record<string, string> {
  rejectRetiredGeminiConfiguration(env)
  return {
    ...deterministicMockLlmEnv,
    ...resolveDemoBackendLlmEnv(env),
  }
}

export function resolveDemoBackendLlmEnv(env: NodeJS.ProcessEnv): Record<string, string> {
  rejectRetiredGeminiConfiguration(env)
  if (!shouldEnableLiveDemoLlm(env)) {
    return {}
  }

  const provider = resolveDemoProvider(env)
  if (!provider) {
    return {}
  }

  const apiKey = firstNonEmpty(
    env.Llm__OpenAi__ApiKey,
    env.TASKDECK_DEMO_OPENAI_API_KEY,
    env.OPENAI_API_KEY,
  )
  if (!apiKey) {
    return {}
  }

  const liveEnv: Record<string, string> = {
    Llm__EnableLiveProviders: 'true',
    Llm__AllowLiveProvidersInDevelopment: 'true',
    Llm__Provider: provider,
    Llm__OpenAi__ApiKey: apiKey,
  }

  const model = firstNonEmpty(env.TASKDECK_DEMO_OPENAI_MODEL, env.Llm__OpenAi__Model)
  if (model) {
    liveEnv.Llm__OpenAi__Model = model
  }

  return liveEnv
}

function rejectRetiredGeminiConfiguration(env: NodeJS.ProcessEnv): void {
  const selectedProviders = [env.TASKDECK_DEMO_LLM_PROVIDER, env.Llm__Provider]
  const selectsRetiredProvider = selectedProviders.some(
    (value) => value?.trim().toLowerCase() === 'gemini',
  )
  const hasTaskdeckGeminiSettings = Object.entries(env).some(([name, value]) => {
    if (typeof value !== 'string' || value.trim().length === 0) {
      return false
    }

    const normalizedName = name.toLowerCase()
    return (
      normalizedName.startsWith('llm__gemini__') ||
      normalizedName.startsWith('taskdeck_demo_gemini_') ||
      normalizedName.startsWith('taskdeck_llm_gemini_')
    )
  })

  if (selectsRetiredProvider || hasTaskdeckGeminiSettings) {
    throw new Error(retiredGeminiConfigurationMessage)
  }
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

  return normalizeProvider(env.TASKDECK_DEMO_LLM_PROVIDER) !== 'Mock'
}

function resolveDemoProvider(env: NodeJS.ProcessEnv): DemoProvider | null {
  const explicitDemoProvider = normalizeProvider(env.TASKDECK_DEMO_LLM_PROVIDER)
  if (explicitDemoProvider === 'Mock') {
    return null
  }

  if (explicitDemoProvider === 'OpenAI') {
    return explicitDemoProvider
  }

  const baseProvider = normalizeProvider(env.Llm__Provider)
  if (baseProvider === 'OpenAI' && hasOpenAiApiKey(env)) {
    return 'OpenAI'
  }

  return hasOpenAiApiKey(env) ? 'OpenAI' : null
}

function hasOpenAiApiKey(env: NodeJS.ProcessEnv): boolean {
  return firstNonEmpty(env.Llm__OpenAi__ApiKey, env.TASKDECK_DEMO_OPENAI_API_KEY, env.OPENAI_API_KEY) !== null
}

function normalizeProvider(value: string | undefined): DemoProvider | 'Mock' | null {
  const normalized = value?.trim().toLowerCase()
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
