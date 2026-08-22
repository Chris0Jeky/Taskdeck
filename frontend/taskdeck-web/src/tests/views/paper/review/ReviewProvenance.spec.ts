import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewProvenance from '../../../../views/paper/review/ReviewProvenance.vue'
import {
  classifyProvenanceActor,
  formatProvenanceActorLabel,
} from '../../../../views/paper/review/provenanceActor'
import type { ProvenanceRow } from '../../../../composables/usePaperReviewSelectors'
import type { ProvenanceMetadata } from '../../../../components/review/ProvenanceDrawer.vue'
import { i18n, SUPPORTED_LOCALES } from '../../../../i18n'

const rows: ProvenanceRow[] = [
  {
    icon: 'doc',
    key: 'source note',
    value: 'Captured inbox note',
    weight: 'primary',
  },
]

/**
 * Provenance triples exactly as the backend records them. These are wire values, not
 * fixtures of convenience:
 *   deterministic — `CaptureTriageService.TriageProviderName` / `TriageModelName` /
 *                   `CaptureTriageOutputContract.PromptVersionV1`
 *   live provider — what the provider that answered reported, plus `PromptVersionLlmV2`
 *   mock          — `MockLlmProvider`'s health identity
 *   degraded      — a live run whose LLM leg failed: the backend KEEPS the deterministic
 *                   defaults, so the record is deterministic and the copy must say so.
 *                   The honesty follows the data, never the workspace configuration.
 */
function metadata(over: Partial<ProvenanceMetadata>): ProvenanceMetadata {
  return { model: 'unknown', provider: 'unknown', confidence: 0.9, latencyMs: 0, promptVersion: null, ...over }
}

const DETERMINISTIC = metadata({
  provider: 'deterministic-extractor',
  model: 'capture-triage-v1',
  promptVersion: 'triage.v1',
})
const LIVE_PROVIDER = metadata({
  provider: 'OpenAI',
  model: 'gpt-4o-mini',
  promptVersion: 'llm-triage.v2',
})
const MOCK = metadata({ provider: 'Mock', model: 'mock-default', promptVersion: 'llm-triage.v2' })
/** Degraded live run — identical record to a plain deterministic one, by design. */
const DEGRADED = metadata({
  provider: 'deterministic-extractor',
  model: 'capture-triage-v1',
  promptVersion: 'triage.v1',
})

function render(over?: ProvenanceMetadata | null) {
  return mount(ReviewProvenance, {
    props: { rows, proposalId: 'proposal-001', metadata: over ?? null },
  })
}

function footnoteText(over?: ProvenanceMetadata | null): string {
  return render(over).get('[data-testid="paper-review-provenance-footnote"]').text()
}

/**
 * Every locale's spelling of the offline/no-network claim. A live-provider proposal must
 * not contain ANY of them — that claim is the defect this file exists to keep out
 * (GH-1963), and pinning only the English wording would let a translated catalog
 * reintroduce it unnoticed.
 */
const OFFLINE_CLAIMS = ['offline', 'sin conexión', 'no ai provider', 'nessun provider ai', 'ningún proveedor de ia']

describe('ReviewProvenance footnote', () => {
  it.each([
    ['deterministic capture triage', DETERMINISTIC, 'deterministic-extractor/capture-triage-v1'],
    ['a live LLM provider', LIVE_PROVIDER, 'OpenAI/gpt-4o-mini'],
    ['the built-in mock provider', MOCK, 'Mock/mock-default'],
    ['a degraded live run that fell back', DEGRADED, 'deterministic-extractor/capture-triage-v1'],
  ])('names the engine the backend recorded for %s', (_case, recorded, label) => {
    expect(footnoteText(recorded)).toContain(label)
  })

  it('describes a deterministic capture by its producer, without an egress guarantee', () => {
    // The recorded triple names WHO PRODUCED the output; it structurally cannot
    // record whether a provider was called (GH-1273: provider/model are stamped
    // only on Succeeded/EmptyExtraction). A degraded live run — transcript sent,
    // output discarded, deterministic fallback produced the proposal — carries
    // this same triple, so the copy must never promise the text stayed local.
    const text = footnoteText(DETERMINISTIC)
    expect(text).toContain('deterministic offline extractor')
    expect(text).not.toContain('No AI provider was called')
  })

  it('describes a degraded live run by what actually ran, with no egress claim either way', () => {
    // The live leg was attempted and failed; the backend recorded the deterministic
    // extractor. Naming the configured provider here would be the inverse of GH-1963 —
    // and claiming no provider was called would be GH-1963 itself, one branch over:
    // the transcript WAS dispatched (and quota-billed) before the fallback ran.
    const text = footnoteText(DEGRADED)
    expect(text).toContain('deterministic offline extractor')
    expect(text).not.toContain('OpenAI')
    expect(text).not.toContain('No AI provider was called')
  })

  it('tells a live-provider proposal that its source text was sent to that provider', () => {
    const text = footnoteText(LIVE_PROVIDER)
    expect(text).toContain('your configured AI provider')
    expect(text).toContain('sent to that provider')
  })

  it('names the mock provider as a mock rather than a live model', () => {
    const text = footnoteText(MOCK)
    expect(text).toContain('mock provider')
    expect(text).toContain('not a live model')
  })

  // AC5 — the regression guard. This is the assertion the previous version of this file
  // had inverted: it pinned the unconditional "deterministic offline extractor for
  // captures" sentence, so the false claim shipped protected by a passing test.
  it.each(SUPPORTED_LOCALES.map((locale) => [locale]))(
    '%s: never claims an offline or provider-free run for a live-provider proposal',
    (locale) => {
      i18n.global.locale.value = locale
      const text = footnoteText(LIVE_PROVIDER).toLowerCase()
      for (const claim of OFFLINE_CLAIMS) {
        expect(text, `"${claim}" asserted for a live-provider proposal in "${locale}"`).not.toContain(
          claim,
        )
      }
      expect(text).toContain('openai/gpt-4o-mini')
    },
  )

  // The inverse guard: the deterministic triple is also what a DEGRADED live run
  // carries (the transcript was dispatched and quota-billed before the fallback),
  // so no locale may attach a provider-free/egress guarantee to it. 'offline' is
  // deliberately absent from this list — it describes the extractor, which is a
  // supported producer claim; the egress claims are what the record cannot back.
  it.each(SUPPORTED_LOCALES.map((locale) => [locale]))(
    '%s: never claims a provider-free run for the deterministic triple',
    (locale) => {
      i18n.global.locale.value = locale
      for (const fixture of [DETERMINISTIC, DEGRADED]) {
        const text = footnoteText(fixture).toLowerCase()
        for (const claim of ['no ai provider', 'nessun provider ai', 'ningún proveedor de ia']) {
          expect(
            text,
            `"${claim}" asserted for the deterministic triple in "${locale}"`,
          ).not.toContain(claim)
        }
      }
    },
  )

  it.each([
    ['no provenance at all', null],
    ['a record whose engine is undetermined', metadata({ provider: 'unknown', model: 'unknown' })],
    [
      'an incoherent record (deterministic provider, LLM prompt version)',
      metadata({
        provider: 'deterministic-extractor',
        model: 'capture-triage-v1',
        promptVersion: 'llm-triage.v2',
      }),
    ],
  ])('says nothing rather than guessing for %s', (_case, recorded) => {
    const wrapper = render(recorded as ProvenanceMetadata | null)
    expect(wrapper.find('[data-testid="paper-review-provenance-footnote"]').exists()).toBe(false)
    // The read-set affordance is not part of the claim and must survive the silence.
    expect(wrapper.text()).toContain('View full read-set')
  })

  it('does not hardcode a model name or over-claim a device boundary', () => {
    // Kept from the pre-GH-1963 spec: "What haiku read" and "No data left this device"
    // were earlier over-claims on this same footnote.
    const text = render(DETERMINISTIC).text()
    expect(text).not.toContain('haiku')
    expect(text).not.toContain('No data left this device')
    expect(text).not.toContain('ran locally')
  })
})

describe('classifyProvenanceActor', () => {
  it.each([
    ['deterministic', DETERMINISTIC, 'deterministic'],
    ['live provider', LIVE_PROVIDER, 'provider'],
    ['mock', MOCK, 'mock'],
    ['degraded fallback', DEGRADED, 'deterministic'],
  ])('classifies %s provenance by what the record names', (_case, recorded, kind) => {
    expect(classifyProvenanceActor(recorded).kind).toBe(kind)
  })

  it.each([
    ['null', null],
    ['undefined', undefined],
    ['an empty provider', { provider: '   ', model: 'x', promptVersion: null }],
    ['the "unknown" sentinel', { provider: 'unknown', model: 'unknown', promptVersion: null }],
  ])('refuses to classify %s', (_case, recorded) => {
    expect(classifyProvenanceActor(recorded).kind).toBe('unknown')
  })

  it('matches the wire values case-insensitively', () => {
    expect(
      classifyProvenanceActor({ provider: 'Deterministic-Extractor', model: 'capture-triage-v1' })
        .kind,
    ).toBe('deterministic')
    expect(classifyProvenanceActor({ provider: 'MOCK', model: 'mock-default' }).kind).toBe('mock')
  })

  it('falls back to the provider alone when no usable model was recorded', () => {
    const actor = classifyProvenanceActor({ provider: 'OpenAI', model: 'unknown' })
    expect(actor.kind).toBe('provider')
    if (actor.kind === 'unknown') throw new Error('expected a classified actor')
    expect(formatProvenanceActorLabel(actor)).toBe('OpenAI')
  })
})
