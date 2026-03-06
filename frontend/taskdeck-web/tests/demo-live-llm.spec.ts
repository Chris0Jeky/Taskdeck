import { describe, expect, it } from 'vitest'

import { resolveDemoBackendLlmEnv } from '../playwright.demo-llm'

describe('demo live llm env resolution', () => {
  it('auto-enables Gemini for full demo runs when a Gemini key is present', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      GEMINI_API_KEY: 'gemini-key',
    })

    expect(env).toEqual({
      Llm__EnableLiveProviders: 'true',
      Llm__AllowLiveProvidersInDevelopment: 'true',
      Llm__Provider: 'Gemini',
      Llm__Gemini__ApiKey: 'gemini-key',
    })
  })

  it('keeps deterministic demo smoke runs on mock by skipping live overrides when llm steps are disabled', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      TASKDECK_DEMO_SKIP_LLM: '1',
      GEMINI_API_KEY: 'gemini-key',
    })

    expect(env).toEqual({})
  })

  it('allows forcing OpenAI for full demos through the demo provider override', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      TASKDECK_DEMO_LLM_PROVIDER: 'OpenAI',
      OPENAI_API_KEY: 'openai-key',
      TASKDECK_DEMO_OPENAI_MODEL: 'gpt-4o-mini',
    })

    expect(env).toEqual({
      Llm__EnableLiveProviders: 'true',
      Llm__AllowLiveProvidersInDevelopment: 'true',
      Llm__Provider: 'OpenAI',
      Llm__OpenAi__ApiKey: 'openai-key',
      Llm__OpenAi__Model: 'gpt-4o-mini',
    })
  })

  it('respects an explicit mock override for demo runs', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      Llm__Provider: 'Mock',
      GEMINI_API_KEY: 'gemini-key',
    })

    expect(env).toEqual({})
  })

  it('does not enable live providers outside demo runs', () => {
    const env = resolveDemoBackendLlmEnv({
      GEMINI_API_KEY: 'gemini-key',
    })

    expect(env).toEqual({})
  })
})
