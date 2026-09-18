<script setup lang="ts">
import type { CardAssignment } from '../../types/board'
defineProps<{ assignments?: CardAssignment[] }>()
</script>
<template>
  <p v-if="assignments?.length" class="text-xs text-on-surface-variant" aria-label="Assignees">
    Assigned to {{ assignments.map(a => a.displayName).join(', ') }}
  </p>
</template>
