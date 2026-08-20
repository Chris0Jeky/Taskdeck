import { describe, expect, it } from 'vitest'

import { resolveDemoBackendLlmEnv, resolvePlaywrightBackendLlmEnv } from '../playwright.demo-llm'

describe('demo live llm env resolution', () => {
  it('auto-enables OpenAI for full demo runs when an OpenAI key is present', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      OPENAI_API_KEY: 'openai-key',
    })

    expect(env).toEqual({
      Llm__EnableLiveProviders: 'true',
      Llm__AllowLiveProvidersInDevelopment: 'true',
      Llm__Provider: 'OpenAI',
      Llm__OpenAi__ApiKey: 'openai-key',
    })
  })

  it('allows an explicit OpenAI demo provider and model', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      TASKDECK_DEMO_LLM_PROVIDER: 'OpenAI',
      TASKDECK_DEMO_OPENAI_API_KEY: 'openai-key',
      TASKDECK_DEMO_OPENAI_MODEL: 'gpt-5.6-luna',
    })

    expect(env).toEqual({
      Llm__EnableLiveProviders: 'true',
      Llm__AllowLiveProvidersInDevelopment: 'true',
      Llm__Provider: 'OpenAI',
      Llm__OpenAi__ApiKey: 'openai-key',
      Llm__OpenAi__Model: 'gpt-5.6-luna',
    })
  })

  it('auto-enables a demo-specific OpenAI key even when the base provider is Mock', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      Llm__Provider: 'Mock',
      TASKDECK_DEMO_OPENAI_API_KEY: 'openai-key',
    })

    expect(env.Llm__Provider).toBe('OpenAI')
    expect(env.Llm__OpenAi__ApiKey).toBe('openai-key')
  })

  it('respects an explicit Mock override even when an OpenAI key is present', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      TASKDECK_DEMO_LLM_PROVIDER: 'Mock',
      OPENAI_API_KEY: 'openai-key',
    })

    expect(env).toEqual({})
  })

  it('ignores an ambient Gemini CLI key instead of treating it as Taskdeck configuration', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      GEMINI_API_KEY: 'ambient-cli-key',
    })

    expect(env).toEqual({})
  })

  it('does not enable live providers outside demo or explicit live-test runs', () => {
    const env = resolveDemoBackendLlmEnv({
      OPENAI_API_KEY: 'openai-key',
      GEMINI_API_KEY: 'ambient-cli-key',
    })

    expect(env).toEqual({})
  })

  it('rejects an explicit retired demo selector before a skip override', () => {
    expect(() =>
      resolveDemoBackendLlmEnv({
        TASKDECK_RUN_DEMO: '1',
        TASKDECK_DEMO_SKIP_LLM: '1',
        TASKDECK_DEMO_LLM_PROVIDER: 'Gemini',
      }),
    ).toThrow(/Gemini provider support was removed/)
  })

  it('rejects a retired base selector instead of silently forcing Mock', () => {
    expect(() =>
      resolvePlaywrightBackendLlmEnv({
        Llm__Provider: 'gemini',
      }),
    ).toThrow(/Gemini provider support was removed/)
  })

  it.each(['Llm__Gemini__ApiKey', 'TASKDECK_DEMO_GEMINI_API_KEY', 'TASKDECK_LLM_GEMINI_API_KEY'])(
    'rejects the Taskdeck-specific retired provider setting %s without reading its value',
    (settingName) => {
      expect(() =>
        resolveDemoBackendLlmEnv({
          Llm__Provider: 'Mock',
          [settingName]: 'stale-test-key',
        }),
      ).toThrow(/remove Taskdeck-specific Gemini provider settings/)
    },
  )
})

describe('playwright backend llm env resolution', () => {
  it('forces deterministic Mock mode for non-demo runs even when an OpenAI key exists', () => {
    const env = resolvePlaywrightBackendLlmEnv({
      Llm__Provider: 'OpenAI',
      Llm__OpenAi__ApiKey: 'openai-key',
    })

    expect(env).toEqual({
      Llm__EnableLiveProviders: 'false',
      Llm__AllowLiveProvidersInDevelopment: 'false',
      Llm__Provider: 'Mock',
    })
  })

  it('lets full demo runs override Mock mode with OpenAI settings', () => {
    const env = resolvePlaywrightBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      OPENAI_API_KEY: 'openai-key',
    })

    expect(env).toEqual({
      Llm__EnableLiveProviders: 'true',
      Llm__AllowLiveProvidersInDevelopment: 'true',
      Llm__Provider: 'OpenAI',
      Llm__OpenAi__ApiKey: 'openai-key',
    })
  })

  it('lets opt-in live llm e2e runs enable OpenAI outside demo mode', () => {
    const env = resolvePlaywrightBackendLlmEnv({
      TASKDECK_RUN_LIVE_LLM_TESTS: '1',
      OPENAI_API_KEY: 'openai-key',
    })

    expect(env.Llm__Provider).toBe('OpenAI')
    expect(env.Llm__OpenAi__ApiKey).toBe('openai-key')
  })
})
