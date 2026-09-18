<script setup lang="ts">
import type { CardDetachPreview } from '../../types/board'
defineProps<{ preview: CardDetachPreview }>()
</script>
<template>
  <div class="my-3 text-sm">
    <p v-if="!preview.children.length">No children will be detached.</p>
    <template v-else>
      <p>These direct children will have no parent. Their cards, columns and history are preserved. Restoring this parent will not reattach them.</p>
      <ul class="mt-2 list-disc space-y-2 pl-5">
        <li v-for="child in preview.children" :key="child.id">
          {{ child.title }} <span v-if="child.isArchived">(archived)</span>
          <small class="block break-all">{{ child.id }}</small>
        </li>
      </ul>
    </template>
  </div>
</template>
