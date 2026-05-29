# ADR-0033: Ambient Channel Hardening — VS Code Extension over Desktop Voice

- **Status**: Proposed
- **Date**: 2026-05-16
- **Deciders**: Repository maintainers

## Context

RFAI-11 requires hardening one ambient capture channel for production use while leaving
the other as a developer-dogfood prototype. The two candidates are:

1. **Desktop voice capture** — local-first transcription into RawCapture with consent
2. **VS Code extension** — selected-text, file-path, and git-remote capture from IDE context

The decision follows RFAI-10 (PWA share-target), which demonstrated that structured,
text-based capture channels produce higher-quality captures than unstructured input.

## Decision

Harden the **VS Code extension** as the production ambient channel. Desktop voice
remains a working prototype only (browser-local, no server-side audio).

Rationale:

- **Testability**: Text-based capture from IDE context is fully deterministic and testable
  in CI. Voice capture requires audio fixtures, transcription mocks, and tolerance for
  non-deterministic output.
- **Privacy posture**: VS Code captures only user-selected text and workspace metadata
  (file path, git remote hash). Voice capture requires microphone access, explicit consent
  UI, and careful handling of ambient audio. The privacy surface of text capture is
  fundamentally smaller.
- **Capture quality**: IDE context (selected code, file path, git remote) produces
  structured, high-signal captures. Voice transcription produces unstructured text that
  requires additional NLP to extract intent.
- **Developer workflow fit**: Taskdeck's primary audience is developers. Capturing from
  within the IDE where developers already work reduces friction more than a voice
  interface. VS Code is the dominant editor (73%+ market share among web developers).
- **webkitSpeechRecognition rejection**: The only browser-native speech API
  (`webkitSpeechRecognition`) streams audio to Google servers, violating Taskdeck's
  local-first and privacy-first principles. Local alternatives (whisper.cpp via WASM)
  are experimental and resource-heavy.

## Alternatives Considered

### Desktop voice as hardened channel
Rejected because of the privacy surface (microphone access, ambient audio), testing
complexity (non-deterministic transcription), and the explicit rejection of
`webkitSpeechRecognition` for streaming audio to third parties.

### Both channels hardened equally
Rejected because it doubles the maintenance surface without proportional value. The
issue scope explicitly requires choosing one.

### Browser extension (Chrome/Firefox) instead of VS Code
Already partially covered by RFAI-10 (`CaptureSource.BrowserExtension = 10`). The VS
Code extension is complementary, not a replacement — it captures IDE-specific context
that browser extensions cannot.

## Consequences

**Positive:**
- Smaller privacy surface — no microphone access or audio handling in the hardened path
- Deterministic, CI-testable capture flow
- Direct integration with developer's primary workspace
- VS Code extension marketplace provides distribution without custom packaging

**Negative:**
- Voice capture remains prototype-only — users who prefer voice input must wait
- VS Code lock-in for IDE capture (JetBrains, Neovim users not served yet)
- Extension requires separate build/publish pipeline (not part of main frontend bundle)

**Neutral:**
- Browser-local PWA voice prototype shipped as a small additional feature — provides
  dogfood signal for future voice hardening without production commitment
- `CaptureSource.VsCodeExtension` (= 11) added to the domain enum alongside existing
  `Voice` (= 4) — voice capture path remains available for future hardening

## References

- Issue: #983 (RFAI-11)
- Parent: #972 (Roadmap v4 tracker)
- Depends on: #982 (RFAI-10, PWA share-target)
- ADR-0003: Proposal-First Automation (ambient writes must be proposal-first)
- ADR-0005: Capture Model — Queue-Wrapper MVP
- ADR-0020: Plugin Extension Architecture
- #219: Voice capture privacy posture (referenced but not reused)
