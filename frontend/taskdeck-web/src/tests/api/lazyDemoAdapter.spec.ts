import type { InternalAxiosRequestConfig } from 'axios'
import { describe, expect, it, vi } from 'vitest'

const mock = vi.hoisted(() => ({
  moduleFactory: vi.fn(),
  adapter: vi.fn(),
}))

vi.mock('../../api/demoAdapter', () => {
  mock.moduleFactory()
  return { demoHttpAdapter: mock.adapter }
})

import { lazyDemoHttpAdapter } from '../../api/lazyDemoAdapter'

function config(url: string): InternalAxiosRequestConfig {
  return {
    method: 'get',
    url,
    baseURL: '',
    headers: { 'Content-Type': 'application/json' },
  } as InternalAxiosRequestConfig
}

describe('lazyDemoHttpAdapter', () => {
  it('does not initialize demo fixtures until the first demo request and reuses the loaded adapter', async () => {
    const firstConfig = config('/health/live')
    const secondConfig = config('/automation/proposals')
    mock.adapter
      .mockResolvedValueOnce({
        data: { status: 'Healthy' },
        status: 200,
        statusText: 'OK',
        headers: {},
        config: firstConfig,
      })
      .mockResolvedValueOnce({
        data: [],
        status: 200,
        statusText: 'OK',
        headers: {},
        config: secondConfig,
      })

    expect(mock.moduleFactory).not.toHaveBeenCalled()

    await expect(lazyDemoHttpAdapter(firstConfig)).resolves.toMatchObject({
      status: 200,
      data: { status: 'Healthy' },
    })
    await expect(lazyDemoHttpAdapter(secondConfig)).resolves.toMatchObject({
      status: 200,
      data: [],
    })

    expect(mock.moduleFactory).toHaveBeenCalledTimes(1)
    expect(mock.adapter).toHaveBeenNthCalledWith(1, firstConfig)
    expect(mock.adapter).toHaveBeenNthCalledWith(2, secondConfig)
  })
})
