<script setup lang="ts">
import { computed, ref } from 'vue'
import { usePaperThemeStore, type PaperMode } from '../store/paperThemeStore'
import InkBleed from '../components/paper/InkBleed.vue'
import PaperStamp from '../components/paper/PaperStamp.vue'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
import PaperTagstamp from '../components/paper/PaperTagstamp.vue'
import PaperCard from '../components/paper/PaperCard.vue'
import PaperKbd from '../components/paper/PaperKbd.vue'
import PaperIcon from '../components/paper/PaperIcon.vue'
import PaperStatusPill from '../components/paper/PaperStatusPill.vue'
import PaperLedgerRow from '../components/paper/PaperLedgerRow.vue'
import PaperConfidenceDial from '../components/paper/PaperConfidenceDial.vue'
import { PAPER_ICON_SHAPES, type PaperIconName } from '../components/paper/paperIconPaths'

const themeStore = usePaperThemeStore()

const previewMode = ref<'paper' | 'paper-night'>(
  themeStore.activeClass === 'paper-night' ? 'paper-night' : 'paper'
)

const previewClass = computed(() => previewMode.value)

function flip(mode: 'paper' | 'paper-night') {
  previewMode.value = mode
}

function setGlobal(mode: PaperMode) {
  themeStore.setMode(mode)
  if (mode === 'paper' || mode === 'paper-night') {
    previewMode.value = mode
  }
}

// Ink Bleed demo — bumping this key remounts the component so the full 4.6s
// sequence replays from t=0. Avoids exposing imperative replay state on the
// component itself.
const inkBleedKey = ref(0)
function replayInkBleed() {
  inkBleedKey.value += 1
}

const iconNames = Object.keys(PAPER_ICON_SHAPES) as PaperIconName[]

// Stamp showroom — let visitors flip between proposed/applied to see the state
// crossfade.  Default to applied so the embossed style is visible immediately.
const stampKind = ref<'applied' | 'proposed' | 'captured' | 'overdue' | 'draft'>('applied')

</script>

<template>
  <div class="sg-root">
    <header class="sg-toolbar">
      <h1>Paper &amp; Graphite styleguide</h1>
      <div class="sg-toolbar-actions">
        <span>Preview frame</span>
        <button
          type="button"
          :aria-pressed="previewMode === 'paper'"
          @click="flip('paper')"
        >Light</button>
        <button
          type="button"
          :aria-pressed="previewMode === 'paper-night'"
          @click="flip('paper-night')"
        >Night</button>
        <span class="sg-divider" />
        <span>Apply to app</span>
        <button type="button" @click="setGlobal('off')">Off (Obsidian)</button>
        <button type="button" @click="setGlobal('paper')">Paper</button>
        <button type="button" @click="setGlobal('paper-night')">Night</button>
        <button type="button" @click="setGlobal('auto')">Auto</button>
      </div>
    </header>

    <section :class="['sg-frame', previewClass]">
      <div class="sg-pad">
        <h2 class="tk-display">Paper &amp; <em>Graphite</em>.</h2>
        <p class="tk-lede">
          A logbook kept in a quiet office. Cream paper, hairline rules, italic
          serif headlines, a single seal-red ember accent that earns its place
          on proposals, applied stamps, and decision moments.
        </p>

        <hr class="hr-double sg-rule" />

        <h3 class="tk-eyebrow">Type scale</h3>
        <div class="sg-stack">
          <div><span class="tk-eyebrow">.tk-display</span><span class="tk-display">Today, you moved <em>nine cards.</em></span></div>
          <div><span class="tk-eyebrow">.tk-h1</span><span class="tk-h1">Split <em>"Implement dark mode"</em> into <em>three smaller cards.</em></span></div>
          <div><span class="tk-eyebrow">.tk-h2</span><span class="tk-h2">Provenance &amp; <em>side effects</em></span></div>
          <div><span class="tk-eyebrow">.tk-h3</span><span class="tk-h3">Recently applied</span></div>
          <div><span class="tk-eyebrow">.tk-lede</span><span class="tk-lede">The assistant read the card body, the linked design doc, and 7 prior activity entries on this board.</span></div>
          <div><span class="tk-eyebrow">.tk-body</span><span class="tk-body">Original 4 comments stay on the archived parent.</span></div>
          <div><span class="tk-eyebrow">.tk-meta</span><span class="tk-meta">2026-04-25 · 11:42 PT</span></div>
          <div><span class="tk-eyebrow">.tk-serial</span><span class="tk-serial">REVIEW · #014 · LOCAL-FIRST</span></div>
          <div><span class="tk-eyebrow">.tk-eyebrow</span><span class="tk-eyebrow">QUEUE · 3 AWAITING</span></div>
          <div><span class="tk-eyebrow">.tk-ink-italic</span><span class="tk-ink-italic">a line for tomorrow</span></div>
        </div>

        <hr class="hr-line sg-rule" />

        <h3 class="tk-eyebrow">Rules</h3>
        <div class="sg-stack">
          <hr class="hr-line" />
          <hr class="hr-soft" />
          <hr class="hr-double" />
          <div class="rule-ledger sg-ledger-demo">
            <span class="tk-body">First entry on the ledger book.</span>
            <span class="tk-body">Second entry. Stripes every 28px.</span>
            <span class="tk-body">Third entry. Mind the gap.</span>
          </div>
        </div>

        <hr class="hr-line sg-rule" />

        <h3 class="tk-eyebrow">Surfaces &amp; cards</h3>
        <div class="sg-grid-3">
          <div class="card sg-pad-tight"><span class="tk-eyebrow">.card</span><p class="tk-body">Lifted paper card with hairline border.</p></div>
          <div class="card-lift sg-pad-tight"><span class="tk-eyebrow">.card-lift</span><p class="tk-body">Stronger lift — used for decision rails.</p></div>
          <div class="well sg-pad-tight"><span class="tk-eyebrow">.well</span><p class="tk-body">Recessed surface for column wells.</p></div>
          <div class="card halo-ember sg-pad-tight"><span class="tk-eyebrow sg-token-ember">.halo-ember</span><p class="tk-body">Active proposal halo.</p></div>
        </div>

        <hr class="hr-line sg-rule" />

        <h3 class="tk-eyebrow">Tagstamps &amp; stamps</h3>
        <div class="sg-row">
          <span class="tagstamp sg-token-ember">PROPOSED · DIFF</span>
          <span class="tagstamp sg-token-applied">APPLIED</span>
          <span class="tagstamp sg-token-overdue">OVERDUE</span>
          <span class="tagstamp sg-token-mute">DRAFT</span>
        </div>
        <div class="sg-row sg-stamps">
          <span class="stamp ember">Proposed<b>Apr 25</b><span class="stamp-num">11:42 · #014</span></span>
          <span class="stamp applied">Applied<b>Apr 25</b><span class="stamp-num">11:48 · #014</span></span>
          <span class="stamp">Captured<b>Apr 25</b><span class="stamp-num">10:01 · #021</span></span>
          <span class="stamp overdue">Overdue<b>3d</b><span class="stamp-num">past · #007</span></span>
        </div>

        <hr class="hr-line sg-rule" />

        <h3 class="tk-eyebrow">Buttons &amp; kbd</h3>
        <div class="sg-row">
          <button class="pbtn" type="button">Default <span class="pkbd">⌫</span></button>
          <button class="pbtn pbtn-primary" type="button">Primary <span class="pkbd">P</span></button>
          <button class="pbtn pbtn-ember" type="button">Apply <span class="pkbd">⏎</span></button>
          <button class="pbtn pbtn-ghost" type="button">Ghost</button>
          <span class="pkbd">⌘</span>
          <span class="pkbd">K</span>
          <span class="pkbd-light pkbd">space</span>
        </div>

        <hr class="hr-line sg-rule" />

        <h3 class="tk-eyebrow">Status pills</h3>
        <div class="sg-row">
          <span class="pstatus proposed">PROPOSED</span>
          <span class="pstatus applied">APPLIED</span>
          <span class="pstatus overdue">OVERDUE</span>
          <span class="pstatus draft">DRAFT</span>
          <span class="pstatus live">LIVE</span>
        </div>

        <hr class="hr-line sg-rule" />

        <h3 class="tk-eyebrow">Diff strips</h3>
        <div class="sg-stack">
          <div class="diff-rem">- Implement dark mode</div>
          <div class="diff-add">+ Tokens · darken &amp; QA</div>
          <div class="diff-add">+ Components · mode switch</div>
        </div>

        <hr class="hr-line sg-rule" />

        <h3 class="tk-eyebrow">Ink Bleed (LLM thinking state)</h3>
        <p class="tk-body">
          Replaces every loading / spinner / skeleton in LLM-driven flows.
          Five phases over 4.6s — drop, bloom, compose, settle, stamp. Reduced
          motion users get a 200ms opacity fade with the dried frame.
        </p>
        <div class="sg-row" style="margin-bottom: 12px;">
          <button class="pbtn pbtn-primary" type="button" @click="replayInkBleed">
            Replay sequence
          </button>
        </div>
        <div class="sg-ink-bleed-stage">
          <InkBleed
            :key="inkBleedKey"
            phase="auto"
            headline="Split &quot;Implement dark mode&quot; into three smaller cards."
          />
        </div>

        <hr class="hr-double sg-rule" />

        <h2 class="tk-h2">Component <em>primitives</em></h2>
        <p class="tk-lede">
          Reusable Vue 3 SFCs under <code>src/components/paper/</code>. Each
          primitive composes from the tokens above and works inside both
          <code>.paper</code> and <code>.paper-night</code> without a dark-mode prop.
        </p>

        <h3 class="tk-eyebrow sg-section-eyebrow">PaperStamp</h3>
        <div class="sg-row sg-stamps">
          <PaperStamp kind="applied" date="Apr 25" time="11:42" num="014" />
          <PaperStamp kind="proposed" date="Apr 25" time="11:50" num="015" />
          <PaperStamp kind="captured" date="Apr 25" time="10:01" num="021" />
          <PaperStamp kind="overdue" date="3d" time="past" num="007" />
          <PaperStamp kind="draft" date="Apr 25" time="—" num="—" />
        </div>
        <div class="sg-row" style="margin-top: 14px;">
          <PaperHLBtn
            label="Toggle stamp kind"
            kbd="U"
            @click="stampKind = stampKind === 'applied' ? 'proposed' : 'applied'"
          />
          <PaperStamp :kind="stampKind" date="Apr 25" time="11:42" num="014" />
          <span class="tk-meta">crossfades 240ms (skipped for reduced-motion)</span>
        </div>

        <h3 class="tk-eyebrow sg-section-eyebrow">PaperHLBtn</h3>
        <div class="sg-row">
          <PaperHLBtn label="Default" kbd="⌫" />
          <PaperHLBtn variant="primary" label="Primary" kbd="P" />
          <PaperHLBtn variant="ember" label="Apply" kbd="⏎" />
          <PaperHLBtn variant="ghost" label="Ghost" />
          <PaperHLBtn label="Capture" kbd="space">
            <template #icon><PaperIcon name="plus" /></template>
          </PaperHLBtn>
        </div>

        <h3 class="tk-eyebrow sg-section-eyebrow">PaperTagstamp</h3>
        <div class="sg-row">
          <PaperTagstamp tone="ember">PROPOSED · DIFF</PaperTagstamp>
          <PaperTagstamp tone="applied">APPLIED</PaperTagstamp>
          <PaperTagstamp tone="overdue">OVERDUE</PaperTagstamp>
          <PaperTagstamp tone="mute">DRAFT</PaperTagstamp>
        </div>

        <h3 class="tk-eyebrow sg-section-eyebrow">PaperCard</h3>
        <div class="sg-grid-3">
          <PaperCard variant="flat" class="sg-pad-tight">
            <span class="tk-eyebrow">flat</span>
            <p class="tk-body">Hairline border, single shadow.</p>
          </PaperCard>
          <PaperCard variant="lift" class="sg-pad-tight">
            <span class="tk-eyebrow">lift</span>
            <p class="tk-body">Lifted shadow for decision rails.</p>
          </PaperCard>
          <PaperCard variant="well" class="sg-pad-tight">
            <span class="tk-eyebrow">well</span>
            <p class="tk-body">Recessed surface for column wells.</p>
          </PaperCard>
          <PaperCard variant="flat" :halo="true" class="sg-pad-tight">
            <span class="tk-eyebrow" style="color: var(--ember)">halo</span>
            <p class="tk-body">Active proposal halo.</p>
          </PaperCard>
        </div>

        <h3 class="tk-eyebrow sg-section-eyebrow">PaperKbd</h3>
        <div class="sg-row">
          <PaperKbd>⌘</PaperKbd>
          <PaperKbd>K</PaperKbd>
          <PaperKbd>⌫</PaperKbd>
          <PaperKbd :light="true">space</PaperKbd>
          <PaperKbd :light="true">tab</PaperKbd>
        </div>

        <h3 class="tk-eyebrow sg-section-eyebrow">PaperIcon · hairline set</h3>
        <div class="sg-icon-grid">
          <span v-for="name in iconNames" :key="name" class="sg-icon-cell">
            <PaperIcon :name="name" :size="16" />
            <span class="tk-meta">{{ name }}</span>
          </span>
        </div>

        <h3 class="tk-eyebrow sg-section-eyebrow">PaperStatusPill</h3>
        <div class="sg-row">
          <PaperStatusPill kind="proposed">PROPOSED</PaperStatusPill>
          <PaperStatusPill kind="applied">APPLIED</PaperStatusPill>
          <PaperStatusPill kind="overdue">OVERDUE</PaperStatusPill>
          <PaperStatusPill kind="draft">DRAFT</PaperStatusPill>
          <PaperStatusPill kind="live">LIVE</PaperStatusPill>
        </div>

        <h3 class="tk-eyebrow sg-section-eyebrow">PaperLedgerRow</h3>
        <PaperCard variant="flat">
          <PaperLedgerRow
            idx="014"
            title="Split &quot;Implement dark mode&quot; into three smaller cards."
            meta="Apr 25 · 11:42"
            :status="{ kind: 'applied', label: 'APPLIED' }"
          />
          <PaperLedgerRow
            idx="015"
            title="Add provenance trail to applied proposals."
            meta="Apr 25 · 11:50"
            :status="{ kind: 'proposed', label: 'PROPOSED' }"
          />
          <PaperLedgerRow
            idx="016"
            title="Audit overdue items in Inbox."
            meta="2d"
            :status="{ kind: 'overdue', label: 'OVERDUE' }"
          />
        </PaperCard>

        <h3 class="tk-eyebrow sg-section-eyebrow">PaperConfidenceDial</h3>
        <div class="sg-row" style="gap: 28px;">
          <PaperConfidenceDial :value="0.18" subline="router · v3" />
          <PaperConfidenceDial :value="0.5" subline="assistant" />
          <PaperConfidenceDial :value="0.84" subline="opus" />
          <PaperConfidenceDial :value="1" caption="LIVE" subline="local" />
        </div>

        <hr class="hr-line sg-rule" />

        <footer class="tk-serial sg-footer">
          STYLEGUIDE · PAPER &amp; GRAPHITE · {{ previewMode.toUpperCase() }}
        </footer>
      </div>
    </section>

    <!-- Side-by-side opposite-theme preview frame so primitives can be checked
         in both substrates without flipping the toggle. -->
    <section :class="['sg-frame', 'sg-frame-mini', previewMode === 'paper' ? 'paper-night' : 'paper']">
      <div class="sg-pad">
        <h3 class="tk-eyebrow">Primitives in {{ previewMode === 'paper' ? 'NIGHT' : 'LIGHT' }}</h3>
        <div class="sg-row sg-stamps">
          <PaperStamp kind="applied" date="Apr 25" time="11:42" num="014" />
          <PaperStamp kind="proposed" date="Apr 25" time="11:50" num="015" />
          <PaperStamp kind="overdue" date="3d" time="past" num="007" />
        </div>
        <div class="sg-row">
          <PaperHLBtn label="Default" kbd="⌫" />
          <PaperHLBtn variant="ember" label="Apply" kbd="⏎" />
          <PaperTagstamp tone="ember">PROPOSED</PaperTagstamp>
          <PaperStatusPill kind="live">LIVE</PaperStatusPill>
        </div>
        <div class="sg-row" style="gap: 28px;">
          <PaperConfidenceDial :value="0.62" subline="assistant" />
          <PaperCard variant="lift" class="sg-pad-tight" style="min-width: 200px;">
            <span class="tk-eyebrow">card-lift</span>
            <p class="tk-body">Both themes share one stylesheet.</p>
          </PaperCard>
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.sg-root {
  min-height: 100vh;
  background: var(--td-color-surface, #131313);
  color: var(--td-text-primary, #fff);
  padding: 16px;
  font-family: system-ui, -apple-system, sans-serif;
}

.sg-toolbar {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 12px 16px;
  margin-bottom: 16px;
  background: var(--td-surface-container, #201f1f);
  border: 1px solid var(--td-border-default, #2a2a2a);
  border-radius: 6px;
}
.sg-toolbar h1 {
  margin: 0;
  font-size: 14px;
  font-weight: 500;
}
.sg-toolbar-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  font-size: 12px;
  color: var(--td-text-secondary, #c0bdb5);
}
.sg-toolbar button {
  padding: 4px 10px;
  font-size: 11px;
  border-radius: 4px;
  border: 1px solid var(--td-border-default, #2a2a2a);
  background: var(--td-surface-container-low, #1c1b1b);
  color: inherit;
  cursor: pointer;
}
.sg-toolbar button[aria-pressed='true'] {
  border-color: #a8421f;
  color: #fff;
  background: #a8421f33;
}
.sg-divider {
  width: 1px;
  height: 16px;
  background: var(--td-border-default, #2a2a2a);
  margin: 0 4px;
}

/* Paper preview frame: scope variables/typography by adding the class below.
   The frame is its own .paper / .paper-night so it works regardless of the
   currently-applied global mode. */
.sg-frame {
  border-radius: 6px;
  overflow: hidden;
  border: 1px solid var(--td-border-default, #2a2a2a);
}
.sg-pad {
  padding: 36px 48px;
  min-height: 600px;
}

.sg-rule {
  margin: 28px 0;
}
.sg-stack {
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.sg-stack > div {
  display: grid;
  grid-template-columns: 160px 1fr;
  align-items: baseline;
  gap: 16px;
}
.sg-grid-3 {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px;
}
.sg-pad-tight {
  padding: 12px 14px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.sg-row {
  display: flex;
  flex-wrap: wrap;
  gap: 14px;
  align-items: center;
}
.sg-stamps {
  margin-top: 18px;
  gap: 28px;
}
.sg-ledger-demo {
  display: flex;
  flex-direction: column;
  gap: 0;
  padding: 4px 12px;
  border: 1px solid var(--line);
  border-radius: 2px;
}
.sg-ledger-demo > * {
  height: 28px;
  display: flex;
  align-items: center;
}
.sg-footer {
  display: block;
  margin-top: 36px;
  padding-top: 14px;
  border-top: 1px solid var(--line);
}


.sg-ink-bleed-stage {
  position: relative;
  height: 320px;
  border: 1px solid var(--line);
  background: var(--paper-card);
  overflow: hidden;
  border-radius: 2px;
}
.sg-section-eyebrow {
  display: block;
  margin-top: 28px;
  margin-bottom: 10px;
}
.sg-icon-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(96px, 1fr));
  gap: 12px;
}
.sg-icon-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 10px 6px;
  border: 1px solid var(--line-soft);
  border-radius: 2px;
  background: var(--paper-card);
}
.sg-frame-mini {
  margin-top: 16px;
}
.sg-token-ember {
  color: var(--ember);
}
.sg-token-applied {
  color: var(--applied);
}
.sg-token-overdue {
  color: var(--overdue);
}
.sg-token-mute {
  color: var(--mute);
}
</style>
