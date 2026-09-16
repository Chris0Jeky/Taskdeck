import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import TranscriptEvidenceViewer from '../../../components/review/TranscriptEvidenceViewer.vue'
import { transcriptsApi } from '../../../api/transcriptsApi'
import { i18n } from '../../../i18n'

vi.mock('../../../api/transcriptsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/transcriptsApi')>()
  return {
    ...actual,
    transcriptsApi: { getById: vi.fn() },
  }
})

describe('TranscriptEvidenceViewer error announcement', () => {
  const previousLocale = i18n.global.locale.value

  afterEach(() => {
    i18n.global.locale.value = previousLocale
    vi.clearAllMocks()
  })

  it('retranslates visible copy without re-announcing the same stale failure', async () => {
    i18n.global.locale.value = 'en'
    vi.mocked(transcriptsApi.getById).mockRejectedValue({ response: { status: 404 } })

    const wrapper = mount(TranscriptEvidenceViewer, {
      props: { transcriptId: 'missing', spanStart: 0, spanEnd: 4 },
    })
    await flushPromises()

    const visibleError = wrapper.get('[data-testid="transcript-evidence-error"]')
    const announcement = wrapper.get('[data-testid="transcript-evidence-error-announcement"]')
    expect(visibleError.attributes('role')).toBeUndefined()
    expect(visibleError.text()).toBe('This transcript is no longer available.')
    expect(announcement.attributes('role')).toBe('alert')
    expect(announcement.text()).toBe('This transcript is no longer available.')

    i18n.global.locale.value = 'it'
    await flushPromises()

    expect(visibleError.text()).toBe('Questa trascrizione non è più disponibile.')
    expect(announcement.text()).toBe('This transcript is no longer available.')
    wrapper.unmount()
  })
})
