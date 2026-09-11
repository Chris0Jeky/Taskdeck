import { createPinia, setActivePinia } from 'pinia'
import { reactive } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useWorkspaceExperimentStore } from '../../store/workspaceExperimentStore'

const session = reactive({ userId: 'first' as string | null })
vi.mock('../../store/sessionStore', () => ({ useSessionStore: () => session }))
const trial = {
  experience: 'studio' as const,
  presentation: 'zen' as const,
  theme: 'grove',
  build: 'v0.3.0',
  scenario: 'capture-review-board' as const,
  completionOutcome: 'completed' as const,
  ease: 4,
  note: 'I found the next step.',
}

describe('workspace comparison observations', () => {
  beforeEach(() => { setActivePinia(createPinia()); session.userId = 'first'; localStorage.clear() })
  afterEach(() => vi.unstubAllGlobals())
  it('exports only manually recorded trials and keeps notes out of browser storage', () => {
    const store = useWorkspaceExperimentStore()
    expect(store.record(trial)).toBe(true)
    const exported = JSON.parse(store.exportJson())
    expect(exported.version).toBe(3)
    expect(exported.trials[0]).toMatchObject(trial)
    expect(localStorage.length).toBe(0)
  })
  it('keeps an unselected ease rating unobserved and accepts an unavailable build', () => {
    const store = useWorkspaceExperimentStore()
    expect(store.record({ ...trial, build: null, ease: null })).toBe(true)
    expect(store.trials[0]).toMatchObject({ build: null, ease: null })
  })
  it('clears observations immediately on identity change or sign out', () => {
    const store = useWorkspaceExperimentStore()
    store.record(trial)
    session.userId = 'second'
    expect(store.trials).toEqual([])
    store.record(trial)
    session.userId = null
    expect(store.trials).toEqual([])
    expect(store.record(trial)).toBe(false)
  })
  it('rejects invalid ratings and unrecognized experiences', () => {
    const store = useWorkspaceExperimentStore()
    expect(store.record({ ...trial, ease: 0 })).toBe(false)
    expect(store.record({ ...trial, ease: 4.5 })).toBe(false)
    expect(store.record({ ...trial, experience: 'automatic' as never })).toBe(false)
    expect(store.record({ ...trial, presentation: 'unsupported' as never })).toBe(false)
    expect(store.record({ ...trial, scenario: 'free-form' as never })).toBe(false)
    expect(store.record({ ...trial, completionOutcome: 'assumed' as never })).toBe(false)
    expect(store.trials).toEqual([])
  })
  it('round trips retained files, skips duplicates and keeps the original build attribution', async () => {
    const store = useWorkspaceExperimentStore()
    store.record(trial)
    const original = store.exportJson()
    store.clear()
    expect(await store.importJson(original)).toBe(1)
    expect(await store.importJson(original)).toBe(0)
    expect(JSON.parse(store.exportJson())).toEqual(JSON.parse(original))
    expect(localStorage.length).toBe(0)
  })
  it('admits version2 files without inventing frontend attribution and deduplicates repeated imports', async () => {
    const store = useWorkspaceExperimentStore()
    const old = JSON.stringify({ kind: 'taskdeck-workspace-comparison', version: 2, trials: [{ ...trial, recordedAt: '2026-09-01T00:00:00Z' }] })
    expect(await store.importJson(old)).toBe(1)
    expect(await store.importJson(old)).toBe(0)
    expect(store.trials[0]).toMatchObject({ frontendBuild: null, build: trial.build })
    expect(store.trials[0]!.id).toMatch(/^legacy-[a-f0-9]{64}$/)
  })
  it('records and round trips observations when only the HTTP LAN crypto API is available', async () => {
    const store = useWorkspaceExperimentStore()
    const old = JSON.stringify({ kind: 'taskdeck-workspace-comparison', version: 2, trials: [{ ...trial, recordedAt: '2026-09-01T00:00:00Z' }] })
    await store.importJson(old)
    const secureId = store.trials[0]!.id
    store.clear()
    vi.stubGlobal('crypto', { getRandomValues: globalThis.crypto.getRandomValues.bind(globalThis.crypto) })
    expect(store.record(trial)).toBe(true)
    expect(store.trials[0]!.id).toMatch(/^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/)
    await store.importJson(old)
    expect(store.trials[1]!.id).toBe(secureId)
    expect(await store.importJson(old)).toBe(0)
    const retained = store.exportJson(); store.clear()
    expect(await store.importJson(retained)).toBe(2)
  })
  it('rejects an invalid or conflicting batch atomically', async () => {
    const store = useWorkspaceExperimentStore()
    store.record(trial)
    const original = store.exportJson()
    const conflicting = JSON.parse(original)
    conflicting.trials[0].note = 'Changed note for the same observation'
    await expect(store.importJson(JSON.stringify(conflicting))).rejects.toThrow('conflicting observation IDs')
    const invalid = JSON.parse(original)
    invalid.trials.push({ ...invalid.trials[0], id: crypto.randomUUID(), ease: 99 })
    await expect(store.importJson(JSON.stringify(invalid))).rejects.toThrow('invalid observation')
    expect(store.exportJson()).toBe(original)
    await expect(store.importJson(' '.repeat(2 * 1024 * 1024 + 1))).rejects.toThrow('2 MiB')
  })
  it('rejects imported choices outside the canonical workspace layout contract', async () => {
    const store = useWorkspaceExperimentStore()
    store.record(trial)
    const invalid = JSON.parse(store.exportJson())
    invalid.trials[0].presentation = 'unsupported'
    await expect(store.importJson(JSON.stringify(invalid))).rejects.toThrow('invalid observation')
    expect(store.trials).toHaveLength(1)
  })
  it('groups comparable conditions while leaving unobserved ratings out of the average', () => {
    const store = useWorkspaceExperimentStore()
    store.record(trial)
    store.record({ ...trial, ease: null, completionOutcome: 'blocked' })
    store.record({ ...trial, build: 'another-release' })
    expect(store.groups).toHaveLength(2)
    expect(store.groups[0]).toMatchObject({ count: 2, completed: 1, blocked: 1, rated: 1, easeTotal: 4 })
  })
  it.each(['account', 'clear'])('abandons asynchronous legacy imports on %s', async change => {
    const store = useWorkspaceExperimentStore()
    const old = JSON.stringify({ kind: 'taskdeck-workspace-comparison', version: 2, trials: [{ ...trial, recordedAt: '2026-09-01T00:00:00Z' }] })
    const importing = store.importJson(old)
    if (change === 'clear') store.clear()
    else session.userId = 'second'
    await expect(importing).rejects.toThrow('session changed')
    expect(store.trials).toEqual([])
  })
})
