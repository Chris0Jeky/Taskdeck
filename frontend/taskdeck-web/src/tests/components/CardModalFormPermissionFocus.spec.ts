import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { createI18n } from 'vue-i18n'
import CardModalForm from '../../components/board/card-modal/CardModalForm.vue'
import en from '../../locales/en'
import type { Card } from '../../types/board'

function mountForm() {
  const i18n = createI18n({
    legacy: false,
    locale: 'en',
    messages: { en },
  })

  return mount(CardModalForm, {
    attachTo: document.body,
    props: {
      card: { dueDate: null } as unknown as Card,
      canEditType: false,
      typePermissionChecking: false,
      typePermissionUnknown: true,
      formattedDueDate: '',
      isOverdue: false,
      workItemType: 'Task',
      title: 'Permission focus card',
      description: '',
      dueDate: '',
      estimateHours: '',
      estimateMinutes: '',
      isBlocked: false,
      blockReason: '',
    },
    global: {
      plugins: [i18n],
      stubs: {
        TdDateField: { template: '<input />' },
        CardEstimateField: { template: '<div />' },
      },
    },
  })
}

describe('CardModalForm permission recovery focus', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
  })

  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('moves focus into the enabled type selector and keeps the live region mounted after a successful retry', async () => {
    const wrapper = mountForm()
    const refresh = wrapper.get('[data-testid="card-type-permission-refresh"]')
    ;(refresh.element as HTMLButtonElement).focus()
    expect(document.activeElement).toBe(refresh.element)

    await wrapper.setProps({ typePermissionChecking: true, typePermissionUnknown: false })
    expect(wrapper.get('[role="status"]').text()).not.toBe('')

    await wrapper.setProps({ typePermissionChecking: false, canEditType: true })
    await nextTick()

    const selector = wrapper.get('#card-work-item-type')
    expect((selector.element as HTMLSelectElement).disabled).toBe(false)
    expect(document.activeElement).toBe(selector.element)
    expect(wrapper.get('[role="status"]').text()).toBe('')
    wrapper.unmount()
  })
})
