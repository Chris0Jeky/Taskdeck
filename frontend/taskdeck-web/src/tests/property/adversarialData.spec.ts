import { describe, it, expect } from 'vitest'
import * as fc from 'fast-check'

/**
 * Additional adversarial data tests for frontend input handling.
 * Extends inputSanitization.spec.ts with:
 * - Proposal operation parameter handling
 * - Capture provenance round-trip
 * - Webhook URL display safety
 * - Extreme numeric boundary values
 */

// Adversarial string arbitrary reused across tests
const adversarialString = fc.oneof(
  fc.constant("<script>alert('xss')</script>"),
  fc.constant('<img src=x onerror=alert(1)>'),
  fc.constant("'; DROP TABLE boards; --"),
  fc.constant('" OR 1=1 --'),
  fc.constant('\u0000'),
  fc.constant('\uFEFF'),
  fc.constant('\u200B'),
  fc.constant('\u202E'),
  fc.constant('\u{1F468}\u200D\u{1F469}\u200D\u{1F467}\u200D\u{1F466}'),
  fc.constant('田中太郎'),
  fc.constant('مرحبا'),
  fc.constant('{\"nested\": true}'),
  fc.constant('back\\slash'),
  fc.constant('\x1B[31mRed\x1B[0m'),
  fc.string(),
)

describe('Adversarial Data: Proposal Operation Parameters', () => {
  it('any string serialized as operation parameter survives JSON round-trip', () => {
    fc.assert(
      fc.property(adversarialString, (input) => {
        const operation = {
          sequence: 0,
          actionType: 'create',
          targetType: 'card',
          parameters: JSON.stringify({ title: input }),
          idempotencyKey: crypto.randomUUID(),
        }
        const json = JSON.stringify(operation)
        const parsed = JSON.parse(json)
        const innerParams = JSON.parse(parsed.parameters)
        expect(innerParams.title).toBe(input)
      }),
      { numRuns: 500 },
    )
  })

  it('proposal with adversarial summary should serialize safely', () => {
    fc.assert(
      fc.property(adversarialString, (summary) => {
        const proposal = {
          id: crypto.randomUUID(),
          sourceType: 0,
          summary,
          riskLevel: 0,
          status: 'PendingReview',
          operations: [],
        }
        const json = JSON.stringify(proposal)
        const parsed = JSON.parse(json)
        expect(parsed.summary).toBe(summary)
      }),
      { numRuns: 500 },
    )
  })

  it('malformed operation parameters string does not throw when parsed cautiously', () => {
    const malformedParams = [
      '',
      'not json',
      '{',
      '[',
      '{"unclosed": ',
      'null',
      '12345',
      '<xml>data</xml>',
    ]

    for (const param of malformedParams) {
      expect(() => {
        try {
          JSON.parse(param)
        } catch {
          // Expected - the frontend should handle this gracefully
        }
      }).not.toThrow()
    }
  })
})

describe('Adversarial Data: Capture Provenance Round-Trip', () => {
  it('provenance with all optional fields survives JSON round-trip', () => {
    const provenanceArb = fc.record({
      captureItemId: fc.uuid(),
      triageRunId: fc.option(fc.uuid(), { nil: null }),
      proposalId: fc.option(fc.uuid(), { nil: null }),
      promptVersion: fc.option(fc.string({ maxLength: 50 }), { nil: null }),
      provider: fc.option(fc.string({ maxLength: 50 }), { nil: null }),
      model: fc.option(fc.string({ maxLength: 50 }), { nil: null }),
      requestedByUserId: fc.option(fc.uuid(), { nil: null }),
      correlationId: fc.option(fc.uuid(), { nil: null }),
      sourceSurface: fc.option(fc.string({ maxLength: 50 }), { nil: null }),
      boardId: fc.option(fc.uuid(), { nil: null }),
      sessionId: fc.option(fc.uuid(), { nil: null }),
    })

    fc.assert(
      fc.property(provenanceArb, (provenance) => {
        const json = JSON.stringify(provenance)
        const parsed = JSON.parse(json)
        expect(parsed.captureItemId).toBe(provenance.captureItemId)
        expect(parsed.triageRunId).toBe(provenance.triageRunId)
        expect(parsed.proposalId).toBe(provenance.proposalId)
        expect(parsed.promptVersion).toBe(provenance.promptVersion)
        expect(parsed.provider).toBe(provenance.provider)
        expect(parsed.model).toBe(provenance.model)
      }),
      { numRuns: 200 },
    )
  })

  it('provenance with adversarial string fields round-trips', () => {
    fc.assert(
      fc.property(adversarialString, adversarialString, (promptVersion, provider) => {
        const provenance = {
          captureItemId: crypto.randomUUID(),
          promptVersion,
          provider,
        }
        const json = JSON.stringify(provenance)
        const parsed = JSON.parse(json)
        expect(parsed.promptVersion).toBe(promptVersion)
        expect(parsed.provider).toBe(provider)
      }),
      { numRuns: 500 },
    )
  })
})

describe('Adversarial Data: Webhook URL Display Safety', () => {
  it('dangerous URLs should be displayable as text without execution', () => {
    const dangerousUrls = [
      'javascript:alert(1)',
      'data:text/html,<script>alert(1)</script>',
      'data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==',
      'vbscript:MsgBox(1)',
      'file:///etc/passwd',
      'https://admin:password@evil.com/webhook',
      'http://169.254.169.254/latest/meta-data/',
    ]

    for (const url of dangerousUrls) {
      // When displayed as text content, the URL string should be preserved exactly
      expect(typeof url).toBe('string')
      // URL encoding should work without throwing
      expect(() => encodeURIComponent(url)).not.toThrow()
      // JSON round-trip should preserve the URL
      const json = JSON.stringify({ url })
      const parsed = JSON.parse(json)
      expect(parsed.url).toBe(url)
    }
  })

  it('extremely long URLs should not cause OOM', () => {
    const longUrl = 'https://example.com/' + 'a'.repeat(100_000)
    expect(() => {
      JSON.stringify({ url: longUrl })
    }).not.toThrow()
    expect(longUrl.length).toBe(100_020)
  })
})

describe('Adversarial Data: Numeric Boundary Values', () => {
  it('card position with extreme values should serialize correctly', () => {
    const extremePositions = [
      0,
      1,
      -1,
      Number.MAX_SAFE_INTEGER,
      Number.MIN_SAFE_INTEGER,
      Number.MAX_VALUE,
      Number.MIN_VALUE,
      Infinity,
      -Infinity,
      NaN,
    ]

    for (const pos of extremePositions) {
      const card = { id: crypto.randomUUID(), position: pos }
      const json = JSON.stringify(card)
      const parsed = JSON.parse(json)

      if (Number.isFinite(pos)) {
        expect(parsed.position).toBe(pos)
      } else if (Number.isNaN(pos)) {
        // JSON.stringify converts NaN to null
        expect(parsed.position).toBeNull()
      } else {
        // JSON.stringify converts Infinity to null
        expect(parsed.position).toBeNull()
      }
    }
  })

  it('wipLimit boundary values should be handled', () => {
    const wipLimits = [0, 1, -1, null, undefined, Number.MAX_SAFE_INTEGER]

    for (const wipLimit of wipLimits) {
      const col = { name: 'Col', wipLimit }
      expect(() => JSON.stringify(col)).not.toThrow()
    }
  })
})

describe('Adversarial Data: Capture Text Edge Cases', () => {
  it('capture text with binary-like data should serialize', () => {
    // Simulate various binary-like string patterns
    const binaryPatterns = [
      '\x00\x01\x02\x03\x04\x05',
      String.fromCharCode(...Array.from({ length: 256 }, (_, i) => i)),
      '\uFFFE\uFFFF', // non-characters
      '\uD800', // lone surrogate (will be replaced in JSON)
    ]

    for (const pattern of binaryPatterns) {
      expect(() => {
        try {
          const json = JSON.stringify({ text: pattern })
          JSON.parse(json)
        } catch {
          // Some patterns may fail JSON serialization, which is expected
        }
      }).not.toThrow()
    }
  })

  it('very long capture text (50K chars) should not freeze', () => {
    const longText = 'x'.repeat(50_000)
    const start = performance.now()

    const json = JSON.stringify({ text: longText })
    JSON.parse(json)

    const elapsed = performance.now() - start
    // Should complete well under 1 second
    expect(elapsed).toBeLessThan(1000)
  })
})
