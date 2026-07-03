import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import ProvenanceDrawer from '../../../components/review/ProvenanceDrawer.vue'
import type { ProvenanceRow } from '../../../composables/usePaperReviewSelectors'
import type { ProvenanceMetadata, EvidenceLink } from '../../../components/review/ProvenanceDrawer.vue'

vi.mock('../../../composables/useEscapeStack', () => ({
  registerEscapeHandler: vi.fn((_handler: () => void) => {
    return () => {}
  }),
}))

const primaryRow: ProvenanceRow = {
  icon: '📄',
  key: 'title',
  value: 'Fix the login bug',
  weight: 'primary',
}

const contextualRow: ProvenanceRow = {
  icon: '🔗',
  key: 'board',
  value: 'Sprint 12',
  weight: 'contextual',
}

const inferredRow: ProvenanceRow = {
  icon: '🤔',
  key: 'priority',
  value: 'high',
  weight: 'inferred',
}

const excludedRow: ProvenanceRow = {
  icon: '🚫',
  key: 'stale note',
  value: 'old context',
  weight: 'excluded',
}

const sampleMetadata: ProvenanceMetadata = {
  model: 'gpt-4o',
  provider: 'openai',
  confidence: 0.87,
  latencyMs: 342,
  promptVersion: 'v1.2.0',
}

const sampleEvidenceLinks: EvidenceLink[] = [
  {
    sourceKey: 'inbox note',
    span: [0, 42],
    reason: 'Direct quote matched task title',
    weight: 'primary',
  },
  {
    sourceKey: 'board context',
    span: null,
    reason: 'Board name inferred from sprint tag',
    weight: 'contextual',
  },
]

describe('ProvenanceDrawer', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
  })

  it('does not render when open is false', () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: false,
        rows: [],
        metadata: null,
        evidenceLinks: [],
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    expect(document.querySelector('.prov-drawer-backdrop')).toBeNull()
    wrapper.unmount()
  })

  it('renders the drawer when open is true', () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [],
        metadata: null,
        evidenceLinks: [],
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    const drawer = document.querySelector('.prov-drawer')
    expect(drawer).not.toBeNull()
    expect(drawer?.getAttribute('role')).toBe('dialog')
    expect(drawer?.getAttribute('aria-modal')).toBe('true')
    wrapper.unmount()
  })

  it('shows metadata model, confidence percentage, and latency when metadata is provided', () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [],
        metadata: sampleMetadata,
        evidenceLinks: [],
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    const metaSection = document.querySelector('.prov-drawer__meta')
    expect(metaSection).not.toBeNull()
    const text = metaSection?.textContent ?? ''
    expect(text).toContain('openai/gpt-4o')
    expect(text).toContain('87%')
    expect(text).toContain('342ms')
    wrapper.unmount()
  })

  it('renders deterministic capture-triage provenance verbatim (#1273)', () => {
    // Capture triage is a deterministic offline extractor, not an LLM — the drawer must
    // display the extractor identity as-is, without mapping it to an AI provider label.
    const deterministicMetadata: ProvenanceMetadata = {
      model: 'capture-triage-v1',
      provider: 'deterministic-extractor',
      confidence: 1,
      latencyMs: 0,
      promptVersion: 'triage.v1',
    }
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [],
        metadata: deterministicMetadata,
        evidenceLinks: [],
        proposalId: 'test-proposal-triage',
      },
      attachTo: document.body,
    })
    const text = document.querySelector('.prov-drawer__meta')?.textContent ?? ''
    expect(text).toContain('deterministic-extractor/capture-triage-v1')
    expect(text).toContain('triage.v1')
    wrapper.unmount()
  })

  it('does not render metadata section when metadata is null', () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [],
        metadata: null,
        evidenceLinks: [],
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    expect(document.querySelector('.prov-drawer__meta')).toBeNull()
    wrapper.unmount()
  })

  it('groups rows by weight into separate sections', () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [primaryRow, contextualRow, inferredRow, excludedRow],
        metadata: null,
        evidenceLinks: [],
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    const groupTitles = Array.from(
      document.querySelectorAll('.prov-drawer__group-title'),
    ).map((el) => el.textContent?.trim())
    expect(groupTitles).toContain('Primary Sources')
    expect(groupTitles).toContain('Contextual')
    expect(groupTitles).toContain('Inferred')
    expect(groupTitles).toContain('Excluded')
    wrapper.unmount()
  })

  it('only renders sections that have rows', () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [primaryRow],
        metadata: null,
        evidenceLinks: [],
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    const groupTitles = Array.from(
      document.querySelectorAll('.prov-drawer__group-title'),
    ).map((el) => el.textContent?.trim())
    expect(groupTitles).toContain('Primary Sources')
    expect(groupTitles).not.toContain('Contextual')
    expect(groupTitles).not.toContain('Inferred')
    expect(groupTitles).not.toContain('Excluded')
    wrapper.unmount()
  })

  it('renders source row key and value within the correct group', () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [primaryRow, inferredRow],
        metadata: null,
        evidenceLinks: [],
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    const sourceKeys = Array.from(
      document.querySelectorAll('.prov-drawer__source-key'),
    ).map((el) => el.textContent?.trim())
    const sourceValues = Array.from(
      document.querySelectorAll('.prov-drawer__source-value'),
    ).map((el) => el.textContent?.trim())
    expect(sourceKeys).toContain('title')
    expect(sourceKeys).toContain('priority')
    expect(sourceValues).toContain('Fix the login bug')
    expect(sourceValues).toContain('high')
    wrapper.unmount()
  })

  it('shows evidence links section when evidenceLinks are provided', () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [],
        metadata: null,
        evidenceLinks: sampleEvidenceLinks,
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    const evidenceSection = document.querySelector('.prov-drawer__evidence')
    expect(evidenceSection).not.toBeNull()
    const evidenceRows = document.querySelectorAll('.prov-drawer__evidence-row')
    expect(evidenceRows.length).toBe(2)
    const text = evidenceSection?.textContent ?? ''
    expect(text).toContain('inbox note')
    expect(text).toContain('Direct quote matched task title')
    expect(text).toContain('board context')
    expect(text).toContain('Board name inferred from sprint tag')
    wrapper.unmount()
  })

  it('does not render evidence section when evidenceLinks is empty', () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [],
        metadata: null,
        evidenceLinks: [],
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    expect(document.querySelector('.prov-drawer__evidence')).toBeNull()
    wrapper.unmount()
  })

  it('shows span information for evidence links that have a span', () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [],
        metadata: null,
        evidenceLinks: sampleEvidenceLinks,
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    const spans = document.querySelectorAll('.prov-drawer__evidence-span')
    // first link has span [0, 42], second has null span
    expect(spans.length).toBe(1)
    expect(spans[0]?.textContent).toContain('0')
    expect(spans[0]?.textContent).toContain('42')
    wrapper.unmount()
  })

  it('emits close when the close button is clicked', async () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [],
        metadata: null,
        evidenceLinks: [],
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    const closeBtn = document.querySelector('.prov-drawer__close') as HTMLElement
    closeBtn?.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })

  it('emits close when the backdrop is clicked', async () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [],
        metadata: null,
        evidenceLinks: [],
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    const backdrop = document.querySelector('.prov-drawer-backdrop') as HTMLElement
    backdrop?.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })

  it('copy JSON button calls navigator.clipboard.writeText with correct JSON structure', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText },
      writable: true,
      configurable: true,
    })

    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [primaryRow],
        metadata: sampleMetadata,
        evidenceLinks: sampleEvidenceLinks,
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    const copyBtn = document.querySelector('.prov-drawer__action--copy') as HTMLElement
    copyBtn?.click()
    await wrapper.vm.$nextTick()

    expect(writeText).toHaveBeenCalledOnce()
    const written = writeText.mock.calls[0]![0] as string
    const parsed = JSON.parse(written)
    expect(parsed).toHaveProperty('sources')
    expect(parsed).toHaveProperty('metadata')
    expect(parsed).toHaveProperty('evidenceLinks')
    expect(parsed.sources).toHaveLength(1)
    expect(parsed.sources[0].key).toBe('title')
    expect(parsed.metadata.model).toBe('gpt-4o')
    expect(parsed.evidenceLinks).toHaveLength(2)
    wrapper.unmount()
  })

  it('report button emits report event with an empty proposalId', async () => {
    const wrapper = mount(ProvenanceDrawer, {
      props: {
        open: true,
        rows: [],
        metadata: null,
        evidenceLinks: [],
        proposalId: 'test-proposal-1',
      },
      attachTo: document.body,
    })
    const reportBtn = document.querySelector('.prov-drawer__action--report') as HTMLElement
    reportBtn?.click()
    await wrapper.vm.$nextTick()
    const emitted = wrapper.emitted('report')
    expect(emitted).toHaveLength(1)
    expect(emitted![0]).toEqual(['test-proposal-1'])
    wrapper.unmount()
  })
})
