<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspaceAttentionStore } from '../../store/workspaceAttentionStore'
import { isDemoMode } from '../../utils/demoMode'
const session = useSessionStore()
const attention = useWorkspaceAttentionStore()
const restricted = ref(false)
const zone = ref('UTC')
const days = ref<number[]>([1, 2, 3, 4, 5])
const start = ref('09:00'); const end = ref('17:00')
const dayOptions = [{ value: 1, name: 'Monday' }, { value: 2, name: 'Tuesday' }, { value: 3, name: 'Wednesday' },
  { value: 4, name: 'Thursday' }, { value: 5, name: 'Friday' }, { value: 6, name: 'Saturday' }, { value: 0, name: 'Sunday' }]
const formatTime = (minutes: number) => `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`
const minutes = (value: string) => { const [hour, minute] = value.split(':').map(Number); return hour! * 60 + minute! }
watch(() => attention.settings, settings => {
  const window = settings?.window
  restricted.value = !!window
  zone.value = window?.timeZoneId ?? 'UTC'
  days.value = window ? dayOptions.filter(day => (window.daysMask & (1 << day.value)) !== 0).map(day => day.value) : [1, 2, 3, 4, 5]
  start.value = formatTime(window?.startMinute ?? 540); end.value = formatTime(window?.endMinute ?? 1020)
}, { immediate: true })
const valid = computed(() => !restricted.value || (zone.value.trim().length > 0 && days.value.length > 0
  && /^\d{2}:\d{2}$/.test(start.value) && /^\d{2}:\d{2}$/.test(end.value) && start.value !== end.value))
function localZone() { zone.value = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC' }
function saveHours() {
  if (!attention.settings || !valid.value) return
  void attention.save(attention.settings.enabled, restricted.value ? { timeZoneId: zone.value.trim(),
    daysMask: days.value.reduce((mask, day) => mask | (1 << day), 0), startMinute: minutes(start.value), endMinute: minutes(end.value) } : null)
}
onMounted(() => { void attention.load() })
</script>

<template>
  <section v-if="!isDemoMode && !session.isDemo" class="attention-settings" aria-label="Optional reminders">
    <h2>Occasional reminders</h2>
    <p>Show a quiet link to an existing question while you browse a board. At most two per UTC day, at least two hours apart across your devices. No new questions or model calls are made.</p>
    <p>Reminders stay hidden while typing, using a dialog, working in Focus or Zen, or away from this tab.</p>
    <label><input type="checkbox" :checked="attention.settings?.enabled ?? false" :disabled="attention.busy || !attention.settings"
      @change="attention.save(($event.target as HTMLInputElement).checked)"> Enable occasional reminders</label>
    <form v-if="attention.settings" @submit.prevent="saveHours">
      <fieldset :disabled="attention.busy">
        <legend>Reminder hours</legend>
        <label><input v-model="restricted" type="checkbox"> Only offer new reminders during selected hours</label>
        <template v-if="restricted">
          <label>Time zone <input v-model="zone" type="text" maxlength="100" placeholder="Europe/London" required></label>
          <button type="button" @click="localZone">Use this device's time zone</button>
          <p>Use an IANA name such as Europe/London or America/New_York, or UTC. The saved zone applies across devices, including daylight-saving changes.</p>
          <div class="days" role="group" aria-label="Reminder days">
            <label v-for="day in dayOptions" :key="day.value"><input v-model="days" type="checkbox" :value="day.value">{{ day.name }}</label>
          </div>
          <label>Start time <input v-model="start" type="time" required></label>
          <label>End time <input v-model="end" type="time" required></label>
          <p>The start is included and the end is excluded. An overnight window continues into the next day from each selected starting day.</p>
        </template>
        <p v-if="!valid" role="status">Choose a time zone, at least one day and different start/end times.</p>
        <button type="submit" :disabled="!valid">Save reminder hours</button>
      </fieldset>
    </form>
    <p v-if="attention.error" role="status">{{ attention.error }}</p>
    <button v-if="!attention.settings && !attention.busy" type="button" @click="attention.load">Reload reminder preference</button>
  </section>
</template>

<style scoped>
.attention-settings { display: grid; gap: .65rem; padding: 1rem; border: 1px solid var(--td-border-default); border-radius: .6rem; background: var(--td-surface-container); color: var(--td-text-primary); }
h2,p { margin: 0; } label { display: flex; align-items: center; gap: .5rem; flex-wrap: wrap; } input[type=checkbox] { width: 1.2rem; height: 1.2rem; } button { justify-self: start; }
fieldset { display: grid; gap: .7rem; min-width: 0; padding: .8rem; border: 1px solid var(--td-border-default); }
input[type=text], input[type=time] { max-width: 100%; min-width: 0; padding: .4rem; color: var(--td-text-primary); background: var(--td-surface-container); border: 1px solid var(--td-border-default); }
.days { display: flex; flex-wrap: wrap; gap: .6rem; }
</style>
