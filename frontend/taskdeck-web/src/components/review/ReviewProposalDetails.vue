<script setup lang="ts">
import { computed, ref } from 'vue'
import type { Proposal, ProposalAffectedEntity } from '../../types/automation'

const props = withDefaults(defineProps<{
  proposal: Proposal
  operationHeadlines: string[]
  affectedEntities: ProposalAffectedEntity[]
  hasProvenance: boolean
  captureHref: string
  proposalHref: string
  shortCorrelationId: string
  /**
   * Archived-board decision history (#1973). The capture and review links stay
   * — they are reads that keep the archived scope — but Open Board does not:
   * `BoardView` / `PaperBoardView` do not gate on `isArchived`, and no service
   * rejects a write to an archived board, so that control hands the user a
   * fully editable board from a surface that just told them to restore it first.
   */
  readOnly?: boolean
}>(), { readOnly: false })

defineEmits<{
  (e: 'open-board', boardId: string): void
}>()

// Per-section collapsible state
const expandedSections = ref<Record<string, boolean>>({})

function isSectionExpanded(section: string): boolean {
  return !!expandedSections.value[section]
}

function toggleSection(section: string) {
  expandedSections.value[section] = !expandedSections.value[section]
}

// Link dropdown state
const linkDropdownOpen = ref(false)

function toggleLinkDropdown() {
  linkDropdownOpen.value = !linkDropdownOpen.value
}

function closeLinkDropdown(event: FocusEvent) {
  const nextFocus = event.relatedTarget as HTMLElement
  if (nextFocus?.closest('.td-review-card__links-dropdown-wrapper')) {
    return
  }
  linkDropdownOpen.value = false
}

const fullCorrelationId = computed(() => props.proposal.correlationId?.trim() ?? '')
</script>

<template>
  <div
    v-if="affectedEntities.length > 0 || operationHeadlines.length > 0 || hasProvenance"
    class="td-review-card__details"
  >
    <!-- Collapsible: Affected cards -->
    <div v-if="affectedEntities.length > 0" class="td-review-card__collapsible">
      <button
        class="td-review-card__collapse-toggle"
        :aria-expanded="isSectionExpanded('entities')"
        @click="toggleSection('entities')"
      >
        <span
          class="td-review-card__collapse-icon"
          aria-hidden="true"
          :class="{ 'td-review-card__collapse-icon--open': isSectionExpanded('entities') }"
        >&#9654;</span>
        <span class="td-review-card__section-label">Affected cards</span>
        <span class="td-review-card__count-badge">{{ affectedEntities.length }}</span>
      </button>
      <div v-if="isSectionExpanded('entities')" class="td-review-card__entity-list">
        <span
          v-for="entity in affectedEntities"
          :key="`${proposal.id}-${entity.entityType}-${entity.entityId ?? 'none'}`"
          class="td-review-entity-chip"
        >
          {{ entity.label }} &middot; {{ entity.changeCount }} change{{ entity.changeCount === 1 ? '' : 's' }}
        </span>
      </div>
    </div>

    <!-- Collapsible: Planned changes -->
    <div v-if="operationHeadlines.length > 0" class="td-review-card__collapsible">
      <button
        class="td-review-card__collapse-toggle"
        :aria-expanded="isSectionExpanded('operations')"
        @click="toggleSection('operations')"
      >
        <span
          class="td-review-card__collapse-icon"
          aria-hidden="true"
          :class="{ 'td-review-card__collapse-icon--open': isSectionExpanded('operations') }"
        >&#9654;</span>
        <span class="td-review-card__section-label">Planned changes</span>
        <span class="td-review-card__count-badge">{{ operationHeadlines.length }}</span>
      </button>
      <div v-if="isSectionExpanded('operations')">
        <ul class="td-review-card__operation-list">
          <li
            v-for="(headline, headlineIndex) in operationHeadlines"
            :key="`${proposal.id}-${headlineIndex}-${headline}`"
          >
            {{ headline }}
          </li>
        </ul>
      </div>
    </div>

    <!-- Collapsible: Provenance / Technical details -->
    <div v-if="hasProvenance" class="td-review-card__collapsible">
      <button
        class="td-review-card__collapse-toggle"
        :aria-expanded="isSectionExpanded('provenance')"
        @click="toggleSection('provenance')"
      >
        <span
          class="td-review-card__collapse-icon"
          aria-hidden="true"
          :class="{ 'td-review-card__collapse-icon--open': isSectionExpanded('provenance') }"
        >&#9654;</span>
        <span class="td-review-card__section-label">Technical details</span>
        <span class="td-provenance-chip">Capture-linked</span>
      </button>
      <div v-if="isSectionExpanded('provenance')" class="td-review-card__provenance-content">
        <span
          v-if="fullCorrelationId.length > 0"
          class="td-review-card__provenance-meta"
          :title="fullCorrelationId"
          :aria-label="`Triage run: ${fullCorrelationId}`"
          tabindex="0"
        >
          Triage run: {{ shortCorrelationId }}
        </span>
        <!-- Links dropdown -->
        <div class="td-review-card__links-dropdown-wrapper">
          <button
            class="td-btn td-btn--secondary td-btn--sm"
            :aria-expanded="linkDropdownOpen"
            @click="toggleLinkDropdown"
            @blur="closeLinkDropdown"
          >
            Links &#9662;
          </button>
          <div
            v-if="linkDropdownOpen"
            class="td-review-card__links-dropdown"
            role="menu"
          >
            <router-link
              class="td-review-card__links-dropdown-item"
              role="menuitem"
              :to="captureHref"
              @mousedown.prevent
            >
              Open Capture
            </router-link>
            <router-link
              class="td-review-card__links-dropdown-item"
              role="menuitem"
              :to="proposalHref"
              @mousedown.prevent
            >
              Review Link
            </router-link>
            <button
              v-if="proposal.boardId && !props.readOnly"
              class="td-review-card__links-dropdown-item"
              role="menuitem"
              data-testid="review-open-board"
              @mousedown.prevent
              @click="$emit('open-board', proposal.boardId!)"
            >
              Open Board
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* Collapsible sections */
.td-review-card__collapsible {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-review-card__collapse-toggle {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  background: none;
  border: none;
  padding: var(--td-space-1) 0;
  cursor: pointer;
  color: var(--td-text-primary);
  font-family: inherit;
  text-align: left;
}

.td-review-card__collapse-toggle:hover {
  color: var(--td-color-primary);
}

.td-review-card__collapse-icon {
  font-size: 0.625rem;
  transition: transform 0.15s ease;
  display: inline-block;
  color: var(--td-text-secondary);
}

.td-review-card__collapse-icon--open {
  transform: rotate(90deg);
}

.td-review-card__count-badge {
  border-radius: var(--td-radius-pill, 999px);
  background: var(--td-surface-container-highest);
  color: var(--td-text-secondary);
  font-size: var(--td-font-xs);
  font-weight: 700;
  padding: 0.0625rem 0.4375rem;
  min-width: 1.25rem;
  text-align: center;
}

.td-review-card__section-label {
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--td-text-secondary);
}

.td-review-card__entity-list {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  max-height: 12rem;
  overflow-y: auto;
}

.td-review-entity-chip {
  border-radius: var(--td-radius-pill, 999px);
  background: var(--td-surface-container-highest);
  border: 1px solid var(--td-border-default);
  color: var(--td-text-primary);
  font-size: var(--td-font-xs);
  font-weight: 600;
  padding: 0.25rem 0.625rem;
}

.td-review-card__operation-list {
  margin: 0;
  padding-left: 1.25rem;
  color: var(--td-text-secondary);
  line-height: 1.6;
  max-height: 12rem;
  overflow-y: auto;
}

/* Provenance content inside collapsible */
.td-review-card__provenance-content {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  align-items: center;
  padding-left: calc(0.625rem + var(--td-space-2));
}

.td-review-card__provenance-meta {
  font-size: var(--td-font-xs);
  color: var(--td-text-secondary);
}

.td-provenance-chip {
  border-radius: var(--td-radius-pill, 999px);
  background: var(--td-surface-container-high);
  border: 1px solid var(--td-border-default);
  color: var(--td-text-secondary);
  font-size: var(--td-font-xs);
  font-weight: 600;
  padding: 0.25rem 0.625rem;
}

/* Links dropdown */
.td-review-card__links-dropdown-wrapper {
  position: relative;
  display: inline-block;
}

.td-review-card__links-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  z-index: 10;
  min-width: 160px;
  margin-top: var(--td-space-1);
  padding: var(--td-space-1) 0;
  background: var(--td-surface-container);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
}

.td-review-card__links-dropdown-item {
  display: block;
  padding: var(--td-space-2) var(--td-space-3);
  font-size: var(--td-font-sm);
  color: var(--td-text-primary);
  text-decoration: none;
  background: none;
  border: none;
  text-align: left;
  cursor: pointer;
  font-family: inherit;
}

.td-review-card__links-dropdown-item:hover {
  background: var(--td-surface-container-high);
}

/* Details section wrapper */
.td-review-card__details {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  border-top: 1px solid var(--td-border-ghost);
  padding-top: var(--td-space-2);
}

@media (max-width: 640px) {
  .td-review-card__provenance-content {
    flex-direction: column;
    align-items: stretch;
    padding-left: 0;
  }

  .td-review-card__links-dropdown {
    position: static;
    box-shadow: none;
    border: 1px solid var(--td-border-default);
    margin-top: var(--td-space-1);
  }
}
</style>
