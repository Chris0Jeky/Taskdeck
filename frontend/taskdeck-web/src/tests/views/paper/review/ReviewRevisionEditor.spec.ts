import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewRevisionEditor from '../../../../views/paper/review/ReviewRevisionEditor.vue'

function mountEditor(operationsPayload: string) {
  return mount(ReviewRevisionEditor, {
    props: {
      operationsPayload,
      saving: false,
    },
  })
}

describe('ReviewRevisionEditor', () => {
  it('reparses the editable fields when the proposal payload changes', async () => {
    const wrapper = mountEditor('{"title":"First"}')

    expect(wrapper.get('[data-testid="revision-field-title"]').element).toHaveProperty('value', 'First')

    await wrapper.setProps({ operationsPayload: '{"title":"Second"}' })

    expect(wrapper.get('[data-testid="revision-field-title"]').element).toHaveProperty('value', 'Second')
  })

  it('preserves original string fields as strings when saving', async () => {
    const wrapper = mountEditor('{"title":"Original","labels":["bug"]}')

    await wrapper.get('[data-testid="revision-field-title"]').setValue('{"not":"json"}')
    await wrapper.get('[data-testid="revision-field-labels"]').setValue('["bug","urgent"]')
    await wrapper.get('[data-testid="revision-reason"]').setValue('Clarify title')
    await wrapper.get('[data-testid="revision-save"]').trigger('click')

    const saveEvents = wrapper.emitted('save')
    expect(saveEvents).toHaveLength(1)
    const payload = saveEvents![0][0] as { revisedPayload: string; reason: string }

    expect(JSON.parse(payload.revisedPayload)).toEqual({
      title: '{"not":"json"}',
      labels: ['bug', 'urgent'],
    })
    expect(payload.reason).toBe('Clarify title')
  })

  it('blocks save when an editable JSON field is invalid', async () => {
    const wrapper = mountEditor('{"operations":[{"sequence":1,"actionType":"CreateCard"}]}')

    await wrapper.get('[data-testid="revision-field-operations"]').setValue('not valid json')
    await wrapper.get('[data-testid="revision-reason"]').setValue('Break operations')

    expect(wrapper.get('[data-testid="revision-field-operations-error"]').text()).toContain('valid JSON')
    expect(wrapper.get('[data-testid="revision-save"]').attributes('disabled')).toBeDefined()

    await wrapper.get('[data-testid="revision-save"]').trigger('click')

    expect(wrapper.emitted('save')).toBeUndefined()
  })
})
