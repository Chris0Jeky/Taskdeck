/**
 * Classifies the actor named by a proposal's RECORDED provenance, so the Review
 * provenance footnote can describe what actually ran instead of asserting a
 * constant (GH-1963).
 *
 * Why this exists: the footnote used to be a static sentence claiming captures are
 * handled by "a deterministic offline extractor". That is only one of the engines the
 * backend can record — a transcript capture triaged by a live provider is stamped with
 * that provider's own identity — so the sentence could tell a user their meeting
 * transcript never left the machine when it had. The backend provenance record is
 * honest; only the copy was not (GH-1273 fixed the inverse bug at the data layer).
 *
 * ── The wire values ──────────────────────────────────────────────────────────
 * `provider` / `model` / `promptVersion` are BACKEND WIRE VALUES: compared as literals
 * here, never translated, and rendered verbatim when they reach the user.
 *
 *   deterministic leg  provider `deterministic-extractor`, model `capture-triage-v1`,
 *                      promptVersion `triage.v1`
 *                      (`CaptureTriageService.TriageProviderName` / `TriageModelName`,
 *                       `CaptureTriageOutputContract.PromptVersionV1`)
 *   live LLM leg       provider/model as reported by the provider that answered
 *                      (e.g. `OpenAI` / `gpt-4o-mini`), promptVersion `llm-triage.v2`
 *                      (`CaptureTriageOutputContract.PromptVersionLlmV2`)
 *   mock provider      provider `Mock`, model `mock-default` (`MockLlmProvider`)
 *   undetermined       the literal `unknown`
 *                      (`CaptureTriageService.UnknownProvenanceValue`, also the fallback
 *                       of `CaptureRequestContract.SanitizeProvenanceMetadata`)
 *
 * A degraded live run is NOT a special case here and must not become one: when the LLM
 * leg fails the backend keeps the deterministic defaults, so a degraded proposal arrives
 * carrying `deterministic-extractor` and is classified `deterministic` — the honesty
 * follows the recorded data, never the workspace configuration.
 *
 * ── Fail-closed rules ────────────────────────────────────────────────────────
 * Absent, blank, or `unknown` provenance yields `unknown`, which the footnote renders as
 * NOTHING. On a trust surface, saying nothing is correct and guessing is not.
 * Contradictory provenance (the deterministic provider stamped alongside the LLM prompt
 * version) also yields `unknown`: the one claim this module must never make on bad data
 * is the offline one.
 */

/**
 * The provenance triple as the wire carries it. Structurally a subset of
 * `ProvenanceMetadata` (`components/review/ProvenanceDrawer.vue`), declared
 * independently so this module stays a plain, directly testable function.
 */
export interface RecordedProvenance {
  provider: string
  model: string
  promptVersion: string | null
}

export type ProvenanceActor =
  /** Taskdeck's own deterministic extractor ran; no model was called. */
  | { kind: 'deterministic'; provider: string; model: string | null }
  /** The built-in mock provider answered; canned output, not a live model. */
  | { kind: 'mock'; provider: string; model: string | null }
  /** A configured AI provider answered and was sent the source text. */
  | { kind: 'provider'; provider: string; model: string | null }
  /** Nothing recorded, or something incoherent — make no claim. */
  | { kind: 'unknown' }

/** `CaptureTriageService.TriageProviderName`. */
const DETERMINISTIC_EXTRACTOR_PROVIDER = 'deterministic-extractor'
/** `LlmHealthStatus` provider name of `MockLlmProvider`. */
const MOCK_PROVIDER = 'mock'
/** `CaptureTriageService.UnknownProvenanceValue`. */
const UNKNOWN_PROVENANCE = 'unknown'
/** `CaptureTriageOutputContract.PromptVersionLlmV2` — stamped only by the LLM leg. */
const LLM_TRIAGE_PROMPT_VERSION = 'llm-triage.v2'

/** Trimmed value, or null when the field is absent, blank, or the `unknown` sentinel. */
function meaningful(value: string | null | undefined): string | null {
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  if (trimmed === '') return null
  return trimmed.toLowerCase() === UNKNOWN_PROVENANCE ? null : trimmed
}

/**
 * Maps recorded provenance onto the actor the footnote may describe.
 *
 * `provider` is the discriminator because it is the field the backend stamps with the
 * engine that actually produced the output. `promptVersion` is used only to detect the
 * incoherent combination above; `model` is carried through for display and never
 * classifies on its own.
 */
export function classifyProvenanceActor(
  provenance: Partial<RecordedProvenance> | null | undefined,
): ProvenanceActor {
  if (!provenance) return { kind: 'unknown' }

  const provider = meaningful(provenance.provider)
  if (provider === null) return { kind: 'unknown' }

  const model = meaningful(provenance.model)
  const promptVersion = meaningful(provenance.promptVersion)
  const normalizedProvider = provider.toLowerCase()

  if (normalizedProvider === DETERMINISTIC_EXTRACTOR_PROVIDER) {
    // Deterministic provider + LLM prompt version cannot both be true of one run.
    // Refuse to assert "offline" rather than pick a side.
    if (promptVersion !== null && promptVersion.toLowerCase() === LLM_TRIAGE_PROMPT_VERSION) {
      return { kind: 'unknown' }
    }
    return { kind: 'deterministic', provider, model }
  }

  if (normalizedProvider === MOCK_PROVIDER) {
    return { kind: 'mock', provider, model }
  }

  return { kind: 'provider', provider, model }
}

/**
 * The engine identity as it is shown to the user: `provider/model`, or the provider alone
 * when no usable model was recorded. Wire text, rendered verbatim and never translated —
 * the same `provider/model` spelling the provenance drawer already uses, so the footnote
 * and the drawer name one run the same way.
 *
 * `unknown` has no label because it has no sentence.
 */
export function formatProvenanceActorLabel(
  actor: Exclude<ProvenanceActor, { kind: 'unknown' }>,
): string {
  return actor.model === null ? actor.provider : `${actor.provider}/${actor.model}`
}
