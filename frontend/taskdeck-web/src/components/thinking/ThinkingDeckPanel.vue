<script setup lang="ts">
import { computed, ref, toRef, watch } from 'vue'
import ThinkingQuestionAnswer from './ThinkingQuestionAnswer.vue'
import { useThinkingDeck } from '../../composables/useThinkingDeck'
import type { ThinkingKind, ThinkingLayer } from '../../types/thinking'

const props = defineProps<{ boardId: string; cardId: string }>()
const emit = defineEmits<{ 'dirty-change': [dirty: boolean] }>()
const { layers, revision, loading, saving, ready, canWrite, error, conflict, dirty, load, save, add, move } =
  useThinkingDeck(toRef(props, 'boardId'), toRef(props, 'cardId'))
const view = ref<'stack' | 'path'>('stack')
const confirmReload = ref(false)
const pendingRemoval = ref<string | null>(null)
const kinds: ThinkingKind[] = ['note', 'question', 'options', 'steps', 'thread']
const privateDrafts = ref<Record<string, boolean>>({})
const anyDirty = computed(() => dirty.value || layers.value.some(layer => privateDrafts.value[layer.id]))
watch(anyDirty, value => emit('dirty-change', value), { immediate: true })
function addItem(layer: ThinkingLayer) {
  if (layer.items.length < 50) layer.items.push({ id: crypto.randomUUID(), text: 'New item', completed: false })
}
function removeItem(layer: ThinkingLayer, id: string) {
  layer.items = layer.items.filter(item => item.id !== id)
  if (layer.selectedOptionId === id) layer.selectedOptionId = null
}
function reload() { confirmReload.value = false; void load() }
</script>

<template>
  <section class="thinking-deck" aria-label="Thinking deck">
    <header class="deck-heading">
      <div><p class="eyebrow">A little room to think</p><h2>Thinking deck</h2></div>
      <div class="view-switch" aria-label="Thinking deck presentation">
        <button type="button" :aria-pressed="view === 'stack'" @click="view = 'stack'">Stack</button>
        <button type="button" :aria-pressed="view === 'path'" @click="view = 'path'">Path</button>
      </div>
    </header>
    <p class="intro">Notes, open questions and small steps, attached to this task. Add only what helps.</p>
    <p v-if="loading" role="status">Loading your thinking…</p>
    <div v-if="error" class="deck-error" role="alert">
      <p>{{ error }}</p>
      <button v-if="!ready" type="button" @click="load">Retry loading</button>
      <button v-if="conflict" type="button" @click="confirmReload = true">Load saved version…</button>
    </div>
    <div v-if="confirmReload" class="deck-confirm" role="group" aria-label="Discard draft confirmation">
      <p>Loading the saved version will discard your unsaved draft.</p>
      <button type="button" @click="confirmReload = false">Keep draft</button>
      <button type="button" @click="reload">Discard draft and load</button>
    </div>
    <template v-if="ready">
      <p v-if="!canWrite" role="status" class="intro">Read-only · You can explore these thoughts. Editing needs board write access.</p>
      <p v-if="!layers.length" class="empty">This task can stay simple. Start a layer when you need space to work something out.</p>
      <ol class="layers" :class="`layers--${view}`">
        <li v-for="(layer, index) in layers" :key="layer.id" class="layer">
          <fieldset :disabled="!canWrite" :aria-label="`Shared thinking layer ${index + 1}`">
          <div class="layer-top"><span class="layer-kind">{{ index + 1 }} · {{ layer.kind }}</span>
            <div class="layer-tools">
              <button type="button" :disabled="index === 0" :aria-label="`Move layer ${index + 1} up`" @click="move(index, -1)">↑</button>
              <button type="button" :disabled="index === layers.length - 1" :aria-label="`Move layer ${index + 1} down`" @click="move(index, 1)">↓</button>
              <button type="button" :aria-label="`Remove layer ${index + 1}`" @click="pendingRemoval = layer.id">Remove</button>
            </div>
          </div>
          <div v-if="pendingRemoval === layer.id" class="deck-confirm">
            <span>Remove this layer from the draft?</span>
            <button type="button" @click="pendingRemoval = null">Keep</button>
            <button type="button" @click="layers.splice(index, 1); pendingRemoval = null">Remove layer</button>
          </div>
          <input v-model="layer.title" :aria-label="`Layer ${index + 1} title`" class="layer-title" maxlength="200" :placeholder="layer.kind === 'question' ? 'What is still unknown?' : 'Give this thought a title'">
          <textarea v-model="layer.body" :aria-label="`Layer ${index + 1} details`" maxlength="8000" rows="3" :placeholder="layer.kind === 'question' ? 'Add context or your working answer…' : 'Write a little, or leave this open…'" />
          <ul v-if="['options', 'steps', 'thread'].includes(layer.kind)" class="items">
            <li v-for="(item, itemIndex) in layer.items" :key="item.id">
              <input v-if="layer.kind === 'steps'" v-model="item.completed" type="checkbox" :aria-label="`Complete step ${itemIndex + 1}`">
              <input v-if="layer.kind === 'options'" v-model="layer.selectedOptionId" type="radio" :name="`option-${layer.id}`" :value="item.id" :aria-label="`Choose option ${itemIndex + 1}`">
              <input v-model="item.text" :aria-label="`${layer.kind} item ${itemIndex + 1}`" maxlength="2000">
              <button type="button" :aria-label="`Remove item ${itemIndex + 1}`" @click="removeItem(layer, item.id)">×</button>
            </li>
          </ul>
          <div v-if="['options', 'steps', 'thread'].includes(layer.kind)" class="item-actions">
            <button type="button" :disabled="layer.items.length >= 50" @click="addItem(layer)">+ Add {{ layer.kind === 'steps' ? 'step' : layer.kind === 'options' ? 'option' : 'thought' }}</button>
            <button v-if="layer.kind === 'options' && layer.selectedOptionId" type="button" @click="layer.selectedOptionId = null">Clear choice</button>
          </div>
          <p v-if="layer.kind === 'options'" class="hint">Choosing keeps every alternative.</p>
          <p v-if="layer.kind === 'steps'" class="hint">These are thinking steps. Checking one does not change the task’s board status.</p>
          </fieldset>
          <ThinkingQuestionAnswer v-if="layer.kind === 'question'" :board-id="boardId" :card-id="cardId" :layer-id="layer.id" :revision="revision" :source-ready="!dirty && !saving" @dirty-change="privateDrafts[layer.id] = $event" />
        </li>
      </ol>
      <fieldset :disabled="!canWrite" aria-label="Add shared thinking layers">
      <div class="add-layers" role="group" aria-label="Add thinking layer"><button v-for="kind in kinds" :key="kind" type="button" :disabled="layers.length >= 40" @click="add(kind)">+ {{ kind }}</button></div>
      </fieldset>
      <footer class="deck-footer">
        <span role="status">{{ dirty ? 'Unsaved thinking' : revision ? 'Thinking saved' : 'No layers yet' }}</span>
        <button v-if="canWrite" type="button" class="save-button" :disabled="!dirty || saving || conflict" @click="save">{{ saving ? 'Saving…' : 'Save thinking' }}</button>
      </footer>
    </template>
  </section>
</template>

<style scoped>
.thinking-deck { color: var(--td-text-primary, #332f29); max-width: 850px; margin: 0 auto; padding: 1.25rem; }
fieldset { border: 0; padding: 0; margin: 0; min-width: 0; }
.deck-heading,.layer-top,.deck-footer,.items li,.item-actions,.add-layers { display: flex; align-items: center; gap: .65rem; }
.deck-heading,.layer-top,.deck-footer { justify-content: space-between; }
.eyebrow,.layer-kind { font-size: .72rem; text-transform: uppercase; letter-spacing: .12em; }
h2 { font-size: 1.6rem; font-weight: 600; margin: .15rem 0; }
.intro,.hint { color: var(--td-text-secondary, #686156); }
.intro { margin: .65rem 0 1.3rem; }
button { border: 1px solid var(--td-border-default, #d7d1c6); border-radius: .45rem; padding: .35rem .65rem; background: var(--td-surface-container, #fffdf7); font-size: .85rem; }
button:disabled { opacity: .45; cursor: default; }
button:not(:disabled):hover { border-color: #777b59; }
button:focus-visible,input:focus-visible,textarea:focus-visible { outline: 2px solid #677249; outline-offset: 3px; }
button[aria-pressed=true],.save-button { background: #45533e; color: #fff; }
.view-switch,.layer-tools { display: flex; gap: .3rem; }
.layers { list-style: none; padding: 0; margin: 0 0 1rem; }
.layer { border: 1px solid var(--td-border-default, #d7d1c6); background: var(--td-surface-container, #fffdf7); padding: 1rem; border-radius: .8rem; margin: 0 0 .8rem; box-shadow: 0 3px 0 #d8d1c530; }
.layers--path { border-left: 2px solid #b9bea6; margin-left: .5rem; padding-left: 1.25rem; }
.layer-title,textarea { display: block; width: 100%; background: transparent; border: 0; padding: .5rem 0; }
.layer-title { font-size: 1.15rem; font-weight: 600; margin-top: .35rem; }
textarea { resize: vertical; min-height: 5rem; }
.items { list-style: none; padding: 0; }
.items li { margin: .45rem 0; }
.items input:not([type=checkbox]):not([type=radio]) { flex: 1; min-width: 0; border: 1px solid var(--td-border-default, #d7d1c6); border-radius: .35rem; padding: .4rem; background: transparent; }
.hint { font-size: .78rem; margin-top: .65rem; }
.add-layers { flex-wrap: wrap; }
.deck-footer { margin-top: 1.5rem; font-size: .85rem; }
.empty { padding: 2rem 1rem; border: 1px dashed #b7b2a5; border-radius: .8rem; margin-bottom: 1rem; }
.deck-error,.deck-confirm { padding: .8rem; margin-bottom: .8rem; background: #fff1d6; color: #5f4315; border-radius: .5rem; }
@media (max-width: 520px) { .thinking-deck { padding: .5rem; } .deck-heading { align-items: flex-start; } .layer { padding: .75rem; } .layer-tools button { padding: .25rem .4rem; } }
</style>
