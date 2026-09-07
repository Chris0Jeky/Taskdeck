import { afterEach, describe, expect, it, vi } from 'vitest'
import { MAX_TRANSCRIPT_FILE_BYTES, MAX_TRANSCRIPT_LENGTH } from '../../constants/capture'
import {
  isTranscriptFile,
  readTranscriptFile,
  TRANSCRIPT_FILE_ACCEPT,
} from '../../utils/transcriptFile'

type ReaderMode = 'load' | 'error' | 'abort' | 'throw'

class MockFileReader {
  result: string | ArrayBuffer | null = null
  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  onabort: (() => void) | null = null

  private readonly mode: ReaderMode

  constructor(mode: ReaderMode = 'load') {
    this.mode = mode
  }

  readAsText = vi.fn(() => {
    if (this.mode === 'throw') throw new Error('read failed')
    queueMicrotask(() => {
      if (this.mode === 'error') this.onerror?.()
      else if (this.mode === 'abort') this.onabort?.()
      else this.onload?.()
    })
  })
}

const OriginalFileReader = globalThis.FileReader

function installReader(mode: ReaderMode = 'load') {
  let reader: MockFileReader | null = null
  class Reader extends MockFileReader {
    constructor() {
      super(mode)
      reader = this
    }
  }
  globalThis.FileReader = Reader as unknown as typeof FileReader
  return { getReader: () => reader }
}

function file(name: string, type = 'text/plain', bytes: BlobPart = 'transcript') {
  return new File([bytes], name, { type })
}

afterEach(() => {
  globalThis.FileReader = OriginalFileReader
})

describe('transcriptFile', () => {
  it('accepts the same plain-text filename and MIME shapes as the Legacy input', () => {
    expect(TRANSCRIPT_FILE_ACCEPT).toBe('.txt,text/plain')
    expect(isTranscriptFile(file('meeting.txt'))).toBe(true)
    expect(isTranscriptFile(file('meeting.TXT', 'application/octet-stream'))).toBe(true)
    expect(isTranscriptFile(file('meeting', 'text/plain'))).toBe(true)
    expect(isTranscriptFile(file('meeting.pdf', 'application/pdf'))).toBe(false)
  })

  it('rejects unsupported types before constructing a reader', async () => {
    const reader = installReader()
    const result = await readTranscriptFile(file('meeting.pdf', 'application/pdf'))

    expect(result).toEqual({ ok: false, error: 'type' })
    expect(reader.getReader()).toBeNull()
  })

  it('rejects files over the transport byte bound before reading', async () => {
    const reader = installReader()
    const result = await readTranscriptFile(
      file('large.txt', 'text/plain', new Uint8Array(MAX_TRANSCRIPT_FILE_BYTES + 1)),
    )

    expect(result).toEqual({ ok: false, error: 'size' })
    expect(reader.getReader()).toBeNull()
  })

  it('accepts a file at the exact transport byte bound', async () => {
    const reader = installReader()
    const exactSizeFile = file('exact.txt')
    Object.defineProperty(exactSizeFile, 'size', { value: MAX_TRANSCRIPT_FILE_BYTES })
    const resultPromise = readTranscriptFile(exactSizeFile)
    reader.getReader()!.result = 'exact size'

    await expect(resultPromise).resolves.toEqual({ ok: true, text: 'exact size' })
  })

  it('returns decoded text and accepts the exact character bound', async () => {
    const reader = installReader()
    const resultPromise = readTranscriptFile(file('meeting.txt'))
    const resultReader = reader.getReader()
    expect(resultReader).not.toBeNull()
    resultReader!.result = 'x'.repeat(MAX_TRANSCRIPT_LENGTH)
    const result = await resultPromise

    expect(result).toEqual({ ok: true, text: 'x'.repeat(MAX_TRANSCRIPT_LENGTH) })
  })

  it('rejects decoded text over the server character bound', async () => {
    const reader = installReader()
    const resultPromise = readTranscriptFile(file('meeting.txt'))
    reader.getReader()!.result = 'x'.repeat(MAX_TRANSCRIPT_LENGTH + 1)

    expect(await resultPromise).toEqual({ ok: false, error: 'tooLong' })
  })

  it.each(['error', 'abort'] as const)('surfaces a %s reader outcome as unreadable', async (mode) => {
    installReader(mode)

    await expect(readTranscriptFile(file('meeting.txt'))).resolves.toEqual({
      ok: false,
      error: 'unreadable',
    })
  })

  it('surfaces a non-text reader result as unreadable', async () => {
    const reader = installReader()
    const resultPromise = readTranscriptFile(file('meeting.txt'))
    reader.getReader()!.result = new ArrayBuffer(0)

    await expect(resultPromise).resolves.toEqual({ ok: false, error: 'unreadable' })
  })

  it('surfaces a synchronous reader failure as unreadable', async () => {
    installReader('throw')

    await expect(readTranscriptFile(file('meeting.txt'))).resolves.toEqual({
      ok: false,
      error: 'unreadable',
    })
  })

  it('surfaces a reader construction failure as unreadable', async () => {
    globalThis.FileReader = class {
      constructor() {
        throw new Error('reader unavailable')
      }
    } as unknown as typeof FileReader

    await expect(readTranscriptFile(file('meeting.txt'))).resolves.toEqual({
      ok: false,
      error: 'unreadable',
    })
  })
})
