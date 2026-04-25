<script setup lang="ts">
import PaperInboxView from './paper/PaperInboxView.vue'
import LegacyInboxView from './LegacyInboxView.vue'
import { usePaperThemeStore } from '../store/paperThemeStore'

const paperTheme = usePaperThemeStore()
</script>

<template>
  <PaperInboxView v-if="paperTheme.isOn" />
  <LegacyInboxView v-else />
</template>
