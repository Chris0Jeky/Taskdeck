<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'

/**
 * TodayCover — dossier cover panel.  Paper-2 → paper gradient backdrop,
 * 56px serif italic headline, lede, seal CTA, and a serial
 * `D-YYYY-MM-DD-NNN` aligned top-right.
 *
 * The component is dumb: the parent owns the seal state machine and toast
 * state.  That keeps idempotency testable in isolation.
 *
 * Seal control states (issue 1939) — the seal is irreversible, so the control
 * must never sit enabled after the fact only to answer "already sealed":
 *
 *   idle       `seal-request`  → parent opens the confirm
 *   confirming `seal-confirm` / `seal-cancel`
 *   sealing    both buttons disabled while the request is in flight
 *   sealed     the CTA is DISABLED and a visible reason says why
 *
 * There is no auto-seal status line: nothing auto-seals a day (GH-1939) — see
 * the seal honesty contract in `locales/en/today.ts` for the measurement and
 * for why `docs/STATUS.md`'s contrary line is corrected in the docs sweep, not
 * by re-adding the copy here.
 */
const props = withDefaults(
  defineProps<{
    serial: string
    cardsMoved: number | null
    lede: string
    sealed: boolean
    confirmingSeal?: boolean
    sealing?: boolean
  }>(),
  {
    confirmingSeal: false,
    sealing: false,
  },
)

const emit = defineEmits<{
  (event: 'seal-request'): void
  (event: 'seal-confirm'): void
  (event: 'seal-cancel'): void
  (event: 'note'): void
}>()

const { t } = useI18n()

const headlineParts = computed(() => {
  const moved = props.cardsMoved ?? 0
  const word = moved === 1 ? 'card' : 'cards'
  // Headline rendered as: "Today, you moved <em>N cards</em>."
  return { count: moved, word }
})

const confirmOpen = computed(() => props.confirmingSeal && !props.sealed)
const sealButton = ref<InstanceType<typeof PaperHLBtn> | null>(null)
const confirmButton = ref<InstanceType<typeof PaperHLBtn> | null>(null)
const sealedReason = ref<HTMLElement | null>(null)

function focusEl(candidate: unknown) {
  if (candidate instanceof HTMLElement) candidate.focus()
}

// Focus has to be handed off explicitly at every edge of the confirm prompt.
// Each exit destroys or disables the element holding it — cancel and success
// unmount the `v-else-if` branch, and `:disabled` during the request blurs the
// confirm CTA — so without this the caret falls to <body> and a keyboard user
// is parked at the top of the document, worst of all after a FAILED seal where
// the prompt is still open and still waiting on them.
watch(confirmOpen, async (open, wasOpen) => {
  await nextTick()
  if (open) {
    // The irreversible step must be reachable from the keyboard and announced,
    // not just painted.
    focusEl(confirmButton.value?.$el)
    return
  }
  if (!wasOpen) return
  // Sealed is terminal: send focus to the `role="status"` reason that explains
  // why the CTA is now dead. Otherwise the prompt was cancelled, and focus
  // belongs back on the control that opened it (re-enabled by the same tick).
  focusEl(props.sealed ? sealedReason.value : sealButton.value?.$el)
})

// A failed seal leaves the prompt open and re-enables both buttons, but the
// browser already dropped focus when they went disabled. Put it back on the
// action the user is being asked to retry.
watch(
  () => props.sealing,
  async (sealing, wasSealing) => {
    if (sealing || !wasSealing || !confirmOpen.value) return
    await nextTick()
    focusEl(confirmButton.value?.$el)
  },
)
</script>

<template>
  <section class="today-cover" data-section="cover">
    <div class="today-cover__inner">
      <div class="today-cover__copy">
        <div class="tk-eyebrow">Dossier · day's ledger · you seal it when you're done</div>
        <h1 class="tk-h1 today-cover__headline">
          <template v-if="cardsMoved !== null">
            Today, you moved <em>{{ headlineParts.count }} {{ headlineParts.word }}</em>.
          </template>
          <template v-else>Today, at a glance.</template>
        </h1>
        <p class="tk-lede today-cover__lede">{{ lede }}</p>
        <div class="today-cover__actions">
          <!-- Sealed is a terminal state: the button is disabled, and the
               reason sits next to it rather than arriving as a toast only
               after a click that could never do anything. -->
          <PaperHLBtn
            ref="sealButton"
            variant="ember"
            :label="sealed ? t('today.seal.sealedAction') : t('today.seal.action')"
            data-action="seal"
            :disabled="sealed || confirmingSeal || sealing"
            :aria-describedby="sealed ? 'today-seal-sealed-reason' : undefined"
            @click="emit('seal-request')"
          />
          <PaperHLBtn
            :label="t('today.note.action')"
            data-action="note"
            :title="t('today.note.hint')"
            @click="emit('note')"
          />
          <span class="tk-meta today-cover__seal-status" data-testid="seal-status">
            {{ sealed ? t('today.seal.sealedStatus') : t('today.seal.idleStatus') }}
          </span>
        </div>

        <p
          v-if="sealed"
          id="today-seal-sealed-reason"
          ref="sealedReason"
          class="today-cover__seal-reason"
          data-testid="seal-sealed-reason"
          role="status"
          tabindex="-1"
        >
          {{ t('today.seal.sealedReason') }}
        </p>

        <div
          v-else-if="confirmOpen"
          class="today-cover__confirm"
          data-testid="seal-confirm"
          role="group"
          aria-labelledby="today-seal-confirm-title"
        >
          <strong id="today-seal-confirm-title" class="today-cover__confirm-title">
            {{ t('today.seal.confirmTitle') }}
          </strong>
          <p class="today-cover__confirm-body">{{ t('today.seal.confirmEffect') }}</p>
          <p class="today-cover__confirm-body today-cover__confirm-body--warn">
            {{ t('today.seal.confirmIrreversible') }}
          </p>
          <div class="today-cover__confirm-actions">
            <PaperHLBtn
              ref="confirmButton"
              variant="ember"
              :label="sealing ? t('today.seal.sealingAction') : t('today.seal.confirmAction')"
              data-action="seal-confirm"
              :disabled="sealing"
              @click="emit('seal-confirm')"
            />
            <PaperHLBtn
              :label="t('today.seal.confirmCancel')"
              data-action="seal-cancel"
              :disabled="sealing"
              @click="emit('seal-cancel')"
            />
          </div>
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
.today-cover__seal-status {
  margin-left: 6px;
}
.today-cover__seal-reason {
  margin: 12px 0 0;
  max-width: 620px;
  color: var(--ink-2);
  font-size: 13px;
  line-height: 1.5;
}
.today-cover__confirm {
  margin: 14px 0 0;
  max-width: 620px;
  padding: 16px 18px;
  border: 1px solid var(--line);
  border-left: 3px solid var(--ember);
  background: var(--paper-card);
}
.today-cover__confirm-title {
  display: block;
  color: var(--ink);
  font-size: 15px;
  line-height: 1.4;
}
.today-cover__confirm-body {
  margin: 8px 0 0;
  color: var(--ink-2);
  font-size: 13px;
  line-height: 1.5;
}
.today-cover__confirm-body--warn {
  color: var(--ink);
  font-weight: 700;
}
.today-cover__confirm-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  margin-top: 14px;
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
