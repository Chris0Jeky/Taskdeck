import { describe, expect, it } from 'vitest'

import { resolveDemoBackendLlmEnv, resolvePlaywrightBackendLlmEnv } from '../playwright.demo-llm'

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

  it('auto-enables a demo-specific Gemini key even when the base development provider is mock', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      Llm__Provider: 'Mock',
      TASKDECK_DEMO_GEMINI_API_KEY: 'gemini-key',
    })

    expect(env).toEqual({
      Llm__EnableLiveProviders: 'true',
      Llm__AllowLiveProvidersInDevelopment: 'true',
      Llm__Provider: 'Gemini',
      Llm__Gemini__ApiKey: 'gemini-key',
    })
  })

  it('lets an explicit demo provider override take precedence over a mock base environment', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      TASKDECK_DEMO_LLM_PROVIDER: 'Gemini',
      Llm__Provider: 'Mock',
      Llm__Gemini__ApiKey: 'gemini-key',
    })

    expect(env).toEqual({
      Llm__EnableLiveProviders: 'true',
      Llm__AllowLiveProvidersInDevelopment: 'true',
      Llm__Provider: 'Gemini',
      Llm__Gemini__ApiKey: 'gemini-key',
    })
  })

  it('auto-enables Gemini for demo-director runs when the shell provides Llm__Gemini__ApiKey', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_DEMO_DIRECTOR: '1',
      Llm__Provider: 'Gemini',
      Llm__Gemini__ApiKey: 'shell-gemini-key',
    })

    expect(env).toEqual({
      Llm__EnableLiveProviders: 'true',
      Llm__AllowLiveProvidersInDevelopment: 'true',
      Llm__Provider: 'Gemini',
      Llm__Gemini__ApiKey: 'shell-gemini-key',
    })
  })

  it('respects an explicit mock demo-provider override even when live keys are present', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      TASKDECK_DEMO_LLM_PROVIDER: 'Mock',
      GEMINI_API_KEY: 'gemini-key',
      OPENAI_API_KEY: 'openai-key',
    })

    expect(env).toEqual({})
  })

  it('falls back to another available live provider when the configured base provider has no key', () => {
    const env = resolveDemoBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      Llm__Provider: 'OpenAI',
      TASKDECK_DEMO_GEMINI_API_KEY: 'gemini-key',
    })

    expect(env).toEqual({
      Llm__EnableLiveProviders: 'true',
      Llm__AllowLiveProvidersInDevelopment: 'true',
      Llm__Provider: 'Gemini',
      Llm__Gemini__ApiKey: 'gemini-key',
    })
  })

  it('does not enable live providers outside demo runs', () => {
    const env = resolveDemoBackendLlmEnv({
      GEMINI_API_KEY: 'gemini-key',
    })

    expect(env).toEqual({})
  })
})

describe('playwright backend llm env resolution', () => {
  it('forces deterministic mock mode for non-demo Playwright runs even when shell keys exist', () => {
    const env = resolvePlaywrightBackendLlmEnv({
      Llm__Provider: 'Gemini',
      Llm__Gemini__ApiKey: 'gemini-key',
    })

    expect(env).toEqual({
      Llm__EnableLiveProviders: 'false',
      Llm__AllowLiveProvidersInDevelopment: 'false',
      Llm__Provider: 'Mock',
    })
  })

  it('keeps deterministic mock mode for demo smoke runs that skip llm steps', () => {
    const env = resolvePlaywrightBackendLlmEnv({
      TASKDECK_RUN_DEMO: '1',
      TASKDECK_DEMO_SKIP_LLM: '1',
      GEMINI_API_KEY: 'gemini-key',
    })

    expect(env).toEqual({
      Llm__EnableLiveProviders: 'false',
      Llm__AllowLiveProvidersInDevelopment: 'false',
      Llm__Provider: 'Mock',
    })
  })

  it('lets full demo runs override mock mode with live Gemini settings', () => {
    const env = resolvePlaywrightBackendLlmEnv({
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
})
