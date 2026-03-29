import { describe, expect, it } from 'vitest'
import { extractParseHint } from '../../utils/chat'

describe('extractParseHint', () => {
  it('returns null when content has no parse hint marker', () => {
    expect(extractParseHint('Just a normal message')).toBeNull()
  })

  it('extracts hint payload from content with valid marker and JSON', () => {
    const payload = {
      supportedPatterns: ['create card "title"', 'archive card {id}'],
      exampleInstruction: 'create card "My task"',
      closestPattern: 'create card "title"',
      detectedIntent: 'create',
    }
    const content = `Some context text.\nCould not parse instruction into a proposal.[PARSE_HINT]${JSON.stringify(payload)}`

    const result = extractParseHint(content)

    expect(result).not.toBeNull()
    expect(result!.textBeforeHint).toBe('Some context text.\nCould not parse instruction into a proposal.')
    expect(result!.hint.supportedPatterns).toEqual(payload.supportedPatterns)
    expect(result!.hint.exampleInstruction).toBe(payload.exampleInstruction)
    expect(result!.hint.closestPattern).toBe(payload.closestPattern)
    expect(result!.hint.detectedIntent).toBe('create')
  })

  it('handles null detectedIntent', () => {
    const payload = {
      supportedPatterns: ['create card "title"'],
      exampleInstruction: 'create card "My task"',
      closestPattern: 'create card "title"',
      detectedIntent: null,
    }
    const content = `Text[PARSE_HINT]${JSON.stringify(payload)}`

    const result = extractParseHint(content)

    expect(result).not.toBeNull()
    expect(result!.hint.detectedIntent).toBeNull()
  })

  it('returns null when JSON after marker is invalid', () => {
    const content = 'Text[PARSE_HINT]{invalid json'
    expect(extractParseHint(content)).toBeNull()
  })

  it('returns null when JSON is valid but missing supportedPatterns array', () => {
    const content = 'Text[PARSE_HINT]{"exampleInstruction":"test"}'
    expect(extractParseHint(content)).toBeNull()
  })

  it('trims trailing whitespace from text before hint', () => {
    const payload = {
      supportedPatterns: ['create card "title"'],
      exampleInstruction: 'create card "test"',
      closestPattern: 'create card "title"',
      detectedIntent: null,
    }
    const content = `Some text   \n  [PARSE_HINT]${JSON.stringify(payload)}`

    const result = extractParseHint(content)

    expect(result).not.toBeNull()
    expect(result!.textBeforeHint).toBe('Some text')
  })
})
