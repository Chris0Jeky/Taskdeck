import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import {
  provideShellKeyboardHelp,
  useShellKeyboardHelp,
  type ShellKeyboardHelpControl,
} from '../../composables/useShellKeyboardHelp'

let injected: ShellKeyboardHelpControl | null | undefined

const Consumer = defineComponent({
  setup() {
    injected = useShellKeyboardHelp()
    return () => h('div')
  },
})

describe('useShellKeyboardHelp', () => {
  it('hands a routed descendant the control the shell provided', () => {
    const open = vi.fn()
    const Provider = defineComponent({
      setup() {
        provideShellKeyboardHelp({ open })
        return () => h(Consumer)
      },
    })

    const wrapper = mount(Provider)

    expect(injected).not.toBeNull()
    injected?.open()
    expect(open).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('returns null outside the shell rather than a silent no-op control', () => {
    const wrapper = mount(Consumer)

    // A caller has to handle this explicitly; nothing pretends the help
    // surface was opened.
    expect(injected).toBeNull()
    wrapper.unmount()
  })
})
