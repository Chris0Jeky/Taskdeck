import { describe, expect, it, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import TranscriptEvidenceViewer from '../../../components/review/TranscriptEvidenceViewer.vue'
import { transcriptsApi, type TranscriptDto } from '../../../api/transcriptsApi'

vi.mock('../../../api/transcriptsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/transcriptsApi')>()
  return {
    ...actual,
    transcriptsApi: { getById: vi.fn() },
  }
})

function makeTranscript(text: string, overrides: Partial<TranscriptDto> = {}): TranscriptDto {
  return {
    id: 't-1',
    boardId: null,
    captureSource: 2,
    text,
    segments: [],
    createdFromCaptureId: null,
    createdAt: '2026-08-19T10:00:00Z',
    ...overrides,
  }
}

async function mountViewer(props: {
  transcriptId?: string
  spanStart: number
  spanEnd: number
  label?: string
}) {
  const wrapper = mount(TranscriptEvidenceViewer, {
    props: { transcriptId: 't-1', ...props },
  })
  await flushPromises()
  return wrapper
}

describe('TranscriptEvidenceViewer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches the transcript and highlights exactly the requested span', async () => {
    const text = 'Ada: ship the export fix\nGrace: I take the migration'
    vi.mocked(transcriptsApi.getById).mockResolvedValue(makeTranscript(text))

    const wrapper = await mountViewer({ spanStart: 5, spanEnd: 24 })

    expect(transcriptsApi.getById).toHaveBeenCalledWith(
      't-1',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
    const highlight = wrapper.get('[data-testid="transcript-evidence-highlight"]')
    expect(highlight.text()).toBe('ship the export fix')
    expect(wrapper.get('[data-testid="transcript-evidence-body"]').text()).toContain(
      'Grace: I take the migration',
    )
  })

  it('highlights the same characters the backend counted across multi-byte text', async () => {
    // Both .NET char offsets and JS string indices are UTF-16 code units, so an
    // astral-plane emoji costs two positions in each. This fixture fails the moment
    // the component starts indexing by code point instead of by code unit.
    const quote = 'déployer le correctif 🚀 aujourd’hui'
    const text = `Zoë 🧭 : contexte\n${quote}\nfin 🎉`
    const start = text.indexOf(quote)
    expect(start).toBeGreaterThan(0)
    vi.mocked(transcriptsApi.getById).mockResolvedValue(makeTranscript(text))

    const wrapper = await mountViewer({ spanStart: start, spanEnd: start + quote.length })

    expect(wrapper.get('[data-testid="transcript-evidence-highlight"]').text()).toBe(quote)
  })

  it('never splits a surrogate pair when a span lands mid-character', async () => {
    const text = 'a🚀b'
    vi.mocked(transcriptsApi.getById).mockResolvedValue(makeTranscript(text))

    // 2 is the low half of the rocket's surrogate pair; the highlight must widen to
    // the whole code point rather than emit a lone surrogate.
    const wrapper = await mountViewer({ spanStart: 2, spanEnd: 3 })

    const highlighted = wrapper.get('[data-testid="transcript-evidence-highlight"]').text()
    expect(highlighted).toBe('🚀')
    expect(highlighted).not.toContain('�')
  })

  it('clamps a span that runs past the end of the transcript', async () => {
    vi.mocked(transcriptsApi.getById).mockResolvedValue(makeTranscript('short text'))

    const wrapper = await mountViewer({ spanStart: 6, spanEnd: 9000 })

    expect(wrapper.get('[data-testid="transcript-evidence-highlight"]').text()).toBe('text')
  })

  it('reports an unresolved span instead of highlighting nothing silently', async () => {
    vi.mocked(transcriptsApi.getById).mockResolvedValue(makeTranscript('short text'))

    const wrapper = await mountViewer({ spanStart: 400, spanEnd: 401 })

    expect(wrapper.find('[data-testid="transcript-evidence-highlight"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="transcript-evidence-unresolved"]').text()).toContain(
      'no longer resolves',
    )
  })

  it('attributes the highlighted line to its segment speaker', async () => {
    const text = 'Ada: first line\nGrace: second line'
    vi.mocked(transcriptsApi.getById).mockResolvedValue(
      makeTranscript(text, {
        segments: [
          { startLine: 0, endLine: 0, speaker: 'Ada', timestampMilliseconds: 0 },
          { startLine: 1, endLine: 1, speaker: 'Grace', timestampMilliseconds: 4200 },
        ],
      }),
    )

    const wrapper = await mountViewer({
      spanStart: text.indexOf('second line'),
      spanEnd: text.length,
    })

    expect(wrapper.text()).toContain('Speaker: Grace')
  })

  it('surfaces a removed transcript as an explicit error, not an empty panel', async () => {
    vi.mocked(transcriptsApi.getById).mockRejectedValue({ response: { status: 404 } })

    const wrapper = await mountViewer({ spanStart: 0, spanEnd: 4 })

    expect(wrapper.get('[data-testid="transcript-evidence-error"]').text()).toContain(
      'no longer available',
    )
    expect(wrapper.find('[data-testid="transcript-evidence-body"]').exists()).toBe(false)
  })

  it('surfaces an unexpected transport failure', async () => {
    vi.mocked(transcriptsApi.getById).mockRejectedValue(new Error('boom'))

    const wrapper = await mountViewer({ spanStart: 0, spanEnd: 4 })

    expect(wrapper.get('[data-testid="transcript-evidence-error"]').text()).toContain(
      'could not be loaded',
    )
  })
})
