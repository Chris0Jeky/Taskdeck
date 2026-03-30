import { describe, expect, it } from 'vitest'
import { extractParseHint, normalizeChatRole, normalizeChatSessionStatus } from '../../utils/chat'

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

describe('normalizeChatRole', () => {
  it('resolves numeric index 0 to User', () => {
    expect(normalizeChatRole(0)).toBe('User')
  })

  it('resolves numeric index 1 to Assistant', () => {
    expect(normalizeChatRole(1)).toBe('Assistant')
  })

  it('resolves numeric index 2 to System', () => {
    expect(normalizeChatRole(2)).toBe('System')
  })

  it('falls back to User for out-of-range numeric index', () => {
    expect(normalizeChatRole(99)).toBe('User')
  })

  it('resolves string value case-insensitively', () => {
    expect(normalizeChatRole('assistant')).toBe('Assistant')
    expect(normalizeChatRole('SYSTEM')).toBe('System')
  })

  it('falls back to User for unrecognized string', () => {
    expect(normalizeChatRole('unknown')).toBe('User')
  })
})

describe('normalizeChatSessionStatus', () => {
  it('resolves numeric index 0 to Active', () => {
    expect(normalizeChatSessionStatus(0)).toBe('Active')
  })

  it('resolves numeric index 1 to Archived', () => {
    expect(normalizeChatSessionStatus(1)).toBe('Archived')
  })

  it('falls back to Active for out-of-range numeric index', () => {
    expect(normalizeChatSessionStatus(99)).toBe('Active')
  })

  it('resolves string value case-insensitively', () => {
    expect(normalizeChatSessionStatus('archived')).toBe('Archived')
  })

  it('falls back to Active for unrecognized string', () => {
    expect(normalizeChatSessionStatus('deleted')).toBe('Active')
  })
})
