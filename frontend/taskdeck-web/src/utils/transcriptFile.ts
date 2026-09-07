import { MAX_TRANSCRIPT_FILE_BYTES, MAX_TRANSCRIPT_LENGTH } from '../constants/capture'

/** The formats already accepted by the Legacy transcript upload path. */
export const TRANSCRIPT_FILE_ACCEPT = '.txt,text/plain'

export type TranscriptFileReadError = 'type' | 'size' | 'tooLong' | 'unreadable'

export type TranscriptFileReadResult =
  | { ok: true; text: string }
  | { ok: false; error: TranscriptFileReadError }

/** Keep the Paper control aligned with the existing Legacy `.txt` contract. */
export function isTranscriptFile(file: File): boolean {
  const [extension, mimeType] = TRANSCRIPT_FILE_ACCEPT.split(',')
  return file.name.toLowerCase().endsWith(extension) || file.type === mimeType
}

/**
 * Read one transcript file for the Paper composer.
 *
 * The API still owns the capture contract. These checks keep an upload that
 * cannot be accepted from travelling over the wire, while the decoded-length
 * check preserves the server's 200,000-character transcript cap for UTF-8
 * files whose byte size is otherwise within the transport bound.
 */
export function readTranscriptFile(file: File): Promise<TranscriptFileReadResult> {
  if (!isTranscriptFile(file)) {
    return Promise.resolve({ ok: false, error: 'type' })
  }

  if (file.size > MAX_TRANSCRIPT_FILE_BYTES) {
    return Promise.resolve({ ok: false, error: 'size' })
  }

  return new Promise((resolve) => {
    let reader: FileReader
    try {
      reader = new FileReader()
    } catch {
      resolve({ ok: false, error: 'unreadable' })
      return
    }

    const finish = (result: TranscriptFileReadResult) => {
      resolve(result)
    }

    reader.onload = () => {
      if (typeof reader.result !== 'string') {
        finish({ ok: false, error: 'unreadable' })
        return
      }

      if (reader.result.length > MAX_TRANSCRIPT_LENGTH) {
        finish({ ok: false, error: 'tooLong' })
        return
      }

      finish({ ok: true, text: reader.result })
    }
    reader.onerror = () => finish({ ok: false, error: 'unreadable' })
    reader.onabort = () => finish({ ok: false, error: 'unreadable' })

    try {
      reader.readAsText(file)
    } catch {
      finish({ ok: false, error: 'unreadable' })
    }
  })
}
