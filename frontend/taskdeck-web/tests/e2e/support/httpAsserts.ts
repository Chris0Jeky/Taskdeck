import type { APIResponse } from '@playwright/test'

export async function assertOk(response: APIResponse, context: string): Promise<void> {
  if (response.ok()) {
    return
  }

  const body = await response.text()
  throw new Error(`${context} failed with ${response.status()} ${response.statusText()}. Body: ${body}`)
}
