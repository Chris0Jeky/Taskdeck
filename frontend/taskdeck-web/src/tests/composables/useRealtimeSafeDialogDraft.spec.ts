import { defineComponent, nextTick, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { useRealtimeSafeDialogDraft } from '../../composables/useRealtimeSafeDialogDraft'

type Source = { id: string; name: string; description: string }

function mountHarness() {
  const source = ref<Source>({ id: 'board-1', name: 'Board', description: 'Initial' })
  const isOpen = ref(false)
  const isBusy = ref(false)
  const name = ref('')
  const description = ref('')

  mount(defineComponent({
    setup() {
      useRealtimeSafeDialogDraft({
        isOpen: () => isOpen.value,
        source: () => source.value,
        sourceKey: (value) => value.id,
        seed: (value) => {
          name.value = value.name
          description.value = value.description
        },
        fields: [
          {
            sourceValue: (value) => value.name,
            draftValue: () => name.value,
            apply: (value) => { name.value = value },
          },
          {
            sourceValue: (value) => value.description,
            draftValue: () => description.value,
            apply: (value) => { description.value = value },
          },
        ],
        isBusy: () => isBusy.value,
      })
      return {}
    },
    template: '<span />',
  }))

  return { source, isOpen, isBusy, name, description }
}

describe('useRealtimeSafeDialogDraft', () => {
  it('resumes untouched-field reconciliation after a busy refresh is followed by a failed action', async () => {
    const state = mountHarness()
    state.isOpen.value = true
    await nextTick()

    state.name.value = 'Local name'
    state.source.value = { id: 'board-1', name: 'Remote name', description: 'Remote description' }
    await nextTick()
    expect(state.name.value).toBe('Local name')
    expect(state.description.value).toBe('Remote description')

    state.isBusy.value = true
    state.source.value = { id: 'board-1', name: 'Busy remote name', description: 'Busy remote description' }
    await nextTick()
    expect(state.description.value).toBe('Remote description')

    state.isBusy.value = false
    state.source.value = { id: 'board-1', name: 'After failure name', description: 'After failure description' }
    await nextTick()
    expect(state.name.value).toBe('Local name')
    expect(state.description.value).toBe('After failure description')
  })
})
