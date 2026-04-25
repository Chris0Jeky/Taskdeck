<script setup lang="ts">
import { computed, ref } from 'vue'
import { usePaperThemeStore, type PaperMode } from '../store/paperThemeStore'

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
          <div><span class="tk-eyebrow">.tk-h3</span><span class="tk-h3">Recently applied · undoable</span></div>
          <div><span class="tk-eyebrow">.tk-lede</span><span class="tk-lede">Haiku read the card body, the linked design doc, and 7 prior activity entries on this board.</span></div>
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

        <h3 class="tk-eyebrow">Erasing reversibility line</h3>
        <p class="tk-body">
          <span class="erase-line">undo within 6 hours · single keystroke</span>
        </p>

        <footer class="tk-serial sg-footer">
          STYLEGUIDE · PAPER &amp; GRAPHITE · {{ previewMode.toUpperCase() }}
        </footer>
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
