import { expect, test } from '@playwright/test'
import { API_BASE_URL, API_ORIGIN, registerAndAttachSession } from './support/authSession'
import { apiRoutePath } from './support/apiRoutePath'

// Real authentication and UI; synthetic capture transport makes provider timing deterministic.
// No provider call, proposal approval, Apply, or board write is made by this journey.
for (const theme of ['paper', 'legacy'] as const) {
  test(`${theme} keeps both accepted triages watched through a status timeout and fresh recovery`, async ({ page, request }, testInfo) => {
    await registerAndAttachSession(page, request, `inbox-poll-${theme}`, { theme })
    await page.clock.install()
    const ids = ['11111111-1111-4111-8111-111111111111', '22222222-2222-4222-8222-222222222222']
    const boardId = '33333333-3333-4333-8333-333333333333'
    const states = new Map(ids.map(id => [id, 'New']))
    const reads = new Map(ids.map(id => [id, 0]))
    const hydrated = new Set<string>()
    const writes: string[] = []
    let recover = false
    let releaseLate!: () => void
    const lateResponse = new Promise<void>(resolve => { releaseLate = resolve })
    const path = apiRoutePath(API_BASE_URL, 'capture/items')
    const summary = (id: string) => ({
      id, userId: 'synthetic-owner', boardId, status: states.get(id), source: 'Typed',
      textExcerpt: id === ids[0] ? 'Synthetic alpha capture' : 'Synthetic beta capture',
      createdAt: '2026-09-10T10:00:00Z', processedAt: null, errorMessage: null,
      disposition: null, canEditSuggestion: states.get(id) === 'New',
    })
    await page.route(url => url.origin === API_ORIGIN && url.pathname.startsWith(path), async route => {
      const url = new URL(route.request().url())
      const method = route.request().method()
      const [id, action] = url.pathname.slice(path.length + 1).split('/')
      const json = (value: unknown) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) })
      if (url.pathname === path && method === 'GET') return json(ids.map(summary))
      if (!id || !ids.includes(id)) return route.abort()
      if (method === 'POST') {
        writes.push(action ?? '')
        expect(action).toBe('triage')
        states.set(id, 'Triaging')
        return json({ id, status: 'Triaging', alreadyTriaging: false })
      }
      if (action === 'status') {
        const attempt = (reads.get(id) ?? 0) + 1
        reads.set(id, attempt)
        if (id === ids[0] && attempt === 1) await lateResponse
        const value = recover ? 'Triaged' : 'Triaging'
        states.set(id, value)
        return json({ id, status: value, processedAt: null, errorMessage: null, disposition: null, canEditSuggestion: value === 'Triaged' })
      }
      if (recover && states.get(id) === 'Triaged') hydrated.add(id)
      return json({ ...summary(id), rawText: `${summary(id).textExcerpt}${recover ? ' fresh terminal detail' : ''}`, retryCount: 0, provenance: null })
    })
    await page.goto('/workspace/inbox')
    const notice = page.getByTestId('inbox-polling-notice')
    for (const id of ids) {
      if (theme === 'paper') {
        const row = page.locator(`.paper-triage__row[data-item-id="${id}"]`)
        await expect(row).toBeVisible()
        await row.locator('[data-action="accept"]').click()
      } else {
        await page.getByTestId('inbox-item').filter({ hasText: id === ids[0] ? 'Synthetic alpha capture' : 'Synthetic beta capture' }).click()
        await page.getByRole('button', { name: 'Start Triage', exact: true }).click()
      }
    }
    await expect.poll(() => writes.length).toBe(2)
    await expect(notice).toContainText('Waiting for triage')
    await page.clock.runFor(2_001)
    await expect.poll(() => reads.get(ids[0]!)).toBe(1)
    expect(reads.get(ids[1]!)).toBe(0) // one read in flight
    await page.clock.runFor(10_001)
    await expect(notice).toContainText('Retrying automatically')
    await expect.poll(() => reads.get(ids[1]!)).toBe(1)
    await page.screenshot({ path: testInfo.outputPath(`${theme}-status-retrying.png`), fullPage: true })
    recover = true
    releaseLate() // the abandoned first response must not retire the current watch
    await page.clock.runFor(4_001)
    await expect.poll(() => hydrated.size).toBe(2)
    await expect(notice).toHaveCount(0)
    expect(reads.get(ids[0]!)).toBeGreaterThan(1)
    expect(reads.get(ids[1]!)).toBeGreaterThan(1)
    expect(writes).toEqual(['triage', 'triage'])
    await page.screenshot({ path: testInfo.outputPath(`${theme}-triages-recovered.png`), fullPage: true })
  })
}
