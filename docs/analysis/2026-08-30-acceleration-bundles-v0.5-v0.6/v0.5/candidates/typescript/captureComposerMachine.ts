export type ComposerState =
  | { type: 'idle' }
  | { type: 'capturing'; sessionId: string; bytes: number }
  | { type: 'finalizing'; sessionId: string }
  | { type: 'submitted'; captureId: string }
  | { type: 'failed'; recoverable: boolean; message: string }

export type ComposerEvent =
  | { type: 'START'; sessionId: string }
  | { type: 'CHUNK'; bytes: number }
  | { type: 'FINALIZE' }
  | { type: 'SUBMITTED'; captureId: string }
  | { type: 'FAIL'; recoverable: boolean; message: string }
  | { type: 'RESET' }

export function reduceComposer(state: ComposerState, event: ComposerEvent): ComposerState {
  switch (event.type) {
    case 'START': return state.type === 'idle' ? { type: 'capturing', sessionId: event.sessionId, bytes: 0 } : state
    case 'CHUNK': return state.type === 'capturing' ? { ...state, bytes: state.bytes + event.bytes } : state
    case 'FINALIZE': return state.type === 'capturing' ? { type: 'finalizing', sessionId: state.sessionId } : state
    case 'SUBMITTED': return state.type === 'finalizing' ? { type: 'submitted', captureId: event.captureId } : state
    case 'FAIL': return { type: 'failed', recoverable: event.recoverable, message: event.message }
    case 'RESET': return { type: 'idle' }
  }
}
