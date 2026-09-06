<script setup lang="ts">
import BoardSettingsModal from './BoardSettingsModal.vue'
import LabelManagerModal from './LabelManagerModal.vue'
import StarterPackCatalogModal from './StarterPackCatalogModal.vue'
import CaptureModal from '../common/CaptureModal.vue'
import type { BoardDetail, Label } from '../../types/board'

defineProps<{
  board: BoardDetail | null
  boardId: string
  boardLabels: Label[]
  showBoardSettings: boolean
  showLabelManager: boolean
  showStarterPackCatalog: boolean
  showCaptureModal: boolean
}>()

defineEmits<{
  'update:showBoardSettings': [value: boolean]
  'update:showLabelManager': [value: boolean]
  'update:showStarterPackCatalog': [value: boolean]
  'update:showCaptureModal': [value: boolean]
}>()
</script>

<template>
  <!-- Board Settings Modal -->
  <BoardSettingsModal
    v-if="board"
    :board="board"
    :is-open="showBoardSettings"
    @close="$emit('update:showBoardSettings', false)"
    @updated="$emit('update:showBoardSettings', false)"
  />

  <StarterPackCatalogModal
    :board-id="boardId"
    :is-open="showStarterPackCatalog"
    @close="$emit('update:showStarterPackCatalog', false)"
    @applied="$emit('update:showStarterPackCatalog', false)"
  />

  <!-- Label Manager Modal -->
  <LabelManagerModal
    :is-open="showLabelManager"
    :board-id="boardId"
    :labels="boardLabels"
    @close="$emit('update:showLabelManager', false)"
    @updated="() => {}"
  />

  <!--
    No keyboard help dialog lives here. The shell renders exactly one help
    surface per skin from the shared ledger, and the board toolbar opens it
    through the `useShellKeyboardHelp` seam (#2007).
  -->
  <CaptureModal
    v-if="showCaptureModal && board"
    :board-id="boardId"
    :board-name="board.name"
    @close="$emit('update:showCaptureModal', false)"
    @created="$emit('update:showCaptureModal', false)"
  />
</template>
