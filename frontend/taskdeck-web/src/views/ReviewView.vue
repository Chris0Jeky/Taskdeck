<script setup lang="ts">
import PaperReviewView from './paper/PaperReviewView.vue'
import LegacyReviewView from './LegacyReviewView.vue'
import { usePaperThemeStore } from '../store/paperThemeStore'

const paperTheme = usePaperThemeStore()
</script>

<template>
  <PaperReviewView v-if="paperTheme.isOn" />
  <LegacyReviewView v-else />
</template>
