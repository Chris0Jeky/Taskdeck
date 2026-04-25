<script setup lang="ts">
import { computed } from 'vue'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'

/**
 * TodayCover — dossier cover panel.  Paper-2 → paper gradient backdrop,
 * 56px serif italic headline, lede, "Seal day" ember CTA, and a serial
 * `D-YYYY-MM-DD-NNN` aligned top-right.
 *
 * The component is dumb: parent owns the seal toggle and toast state.
 * That keeps idempotency testable in isolation.
 */
const props = defineProps<{
  serial: string
  cardsMoved: number
  lede: string
  autoSealsIn: string
  sealed: boolean
}>()

const emit = defineEmits<{
  (event: 'seal'): void
  (event: 'note'): void
}>()

const headlineParts = computed(() => {
  const moved = props.cardsMoved
  const word = moved === 1 ? 'card' : 'cards'
  // Headline rendered as: "Today, you moved <em>N cards</em>."
  return { count: moved, word }
})

function onSealClick() {
  emit('seal')
}
</script>

<template>
  <section class="today-cover" data-section="cover">
    <div class="today-cover__inner">
      <div class="today-cover__copy">
        <div class="tk-eyebrow">Dossier · day's ledger · sealed at end of session</div>
        <h1 class="tk-h1 today-cover__headline">
          Today, you moved <em>{{ headlineParts.count }} {{ headlineParts.word }}</em>.
        </h1>
        <p class="tk-lede today-cover__lede">{{ lede }}</p>
        <div class="today-cover__actions">
          <PaperHLBtn
            variant="ember"
            :label="sealed ? 'Day sealed' : 'Seal day & archive'"
            data-action="seal"
            :aria-pressed="sealed"
            @click="onSealClick"
          />
          <PaperHLBtn label="Write a note" data-action="note" @click="emit('note')" />
          <span class="tk-meta today-cover__auto" data-testid="auto-seals-in">
            <template v-if="sealed">Sealed for the day</template>
            <template v-else>Auto-seals in {{ autoSealsIn }}</template>
          </span>
        </div>
      </div>
      <div class="today-cover__stamp">
        <span class="tk-serial today-cover__serial" data-testid="dossier-serial">{{ serial }}</span>
      </div>
    </div>
  </section>
</template>

<style scoped>
.today-cover {
  padding: 44px 56px 28px;
  background: linear-gradient(180deg, var(--paper-2) 0%, var(--paper) 100%);
  border-bottom: 1px solid var(--line);
  position: relative;
}
.today-cover__inner {
  display: grid;
  grid-template-columns: 1.4fr 1fr;
  gap: 32px;
  align-items: flex-end;
}
.today-cover__copy {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.today-cover__headline {
  font-size: 56px;
  line-height: 1.02;
  margin: 10px 0 6px;
}
.today-cover__lede {
  margin-top: 8px;
  max-width: 620px;
}
.today-cover__actions {
  display: flex;
  gap: 14px;
  margin-top: 18px;
  align-items: center;
  flex-wrap: wrap;
}
.today-cover__auto {
  margin-left: 6px;
}
.today-cover__stamp {
  position: relative;
  text-align: right;
}
.today-cover__serial {
  display: inline-block;
  margin-top: 10px;
  color: var(--faint);
  text-align: right;
}

@media (max-width: 900px) {
  .today-cover {
    padding: 32px 24px 20px;
  }
  .today-cover__inner {
    grid-template-columns: 1fr;
  }
  .today-cover__headline {
    font-size: 40px;
  }
}
</style>
