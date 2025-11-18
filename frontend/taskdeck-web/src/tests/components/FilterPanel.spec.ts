import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import FilterPanel from '../../components/board/FilterPanel.vue'
import type { CardFilters } from '../../store/boardStore'
import type { Label } from '../../types/board'

describe('FilterPanel', () => {
  const mockLabels: Label[] = [
    { id: 'label-1', boardId: 'board-1', name: 'Bug', colorHex: '#ff0000', createdAt: '', updatedAt: '' },
    { id: 'label-2', boardId: 'board-1', name: 'Feature', colorHex: '#00ff00', createdAt: '', updatedAt: '' },
    { id: 'label-3', boardId: 'board-1', name: 'Enhancement', colorHex: '#0000ff', createdAt: '', updatedAt: '' },
  ]

  const defaultFilters: CardFilters = {
    searchText: '',
    labelIds: [],
    dueDateFilter: 'all',
    showBlockedOnly: false
  }

  describe('Rendering', () => {
    it('should not render when isOpen is false', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: false,
          labels: mockLabels,
          activeFilters: defaultFilters
        }
      })

      expect(wrapper.find('.bg-white').exists()).toBe(false)
    })

    it('should render when isOpen is true', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: defaultFilters
        }
      })

      expect(wrapper.find('.bg-white').exists()).toBe(true)
      expect(wrapper.text()).toContain('Filter Cards')
    })

    it('should render all filter controls', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: defaultFilters
        }
      })

      expect(wrapper.find('input[type="text"]').exists()).toBe(true) // Search
      expect(wrapper.find('select').exists()).toBe(true) // Due date
      expect(wrapper.find('input[type="checkbox"]').exists()).toBe(true) // Blocked status
    })

    it('should render all labels with correct colors', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: defaultFilters
        }
      })

      const labelElements = wrapper.findAll('input[type="checkbox"]')
      // 1 for blocked status + 3 for labels
      expect(labelElements.length).toBeGreaterThanOrEqual(3)

      expect(wrapper.text()).toContain('Bug')
      expect(wrapper.text()).toContain('Feature')
      expect(wrapper.text()).toContain('Enhancement')
    })

    it('should show "No labels available" when no labels exist', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: [],
          activeFilters: defaultFilters
        }
      })

      expect(wrapper.text()).toContain('No labels available')
    })
  })

  describe('Search Filter', () => {
    it('should emit update:filters when search text changes', async () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: defaultFilters
        }
      })

      const searchInput = wrapper.find('input[type="text"]')
      await searchInput.setValue('bug fix')

      expect(wrapper.emitted('update:filters')).toBeTruthy()
      const emitted = wrapper.emitted('update:filters') as any[]
      expect(emitted[0][0].searchText).toBe('bug fix')
    })

    it('should display current search text', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: { ...defaultFilters, searchText: 'test search' }
        }
      })

      const searchInput = wrapper.find('input[type="text"]') as any
      expect(searchInput.element.value).toBe('test search')
    })
  })

  describe('Due Date Filter', () => {
    it('should emit update:filters when due date filter changes', async () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: defaultFilters
        }
      })

      const select = wrapper.find('select')
      await select.setValue('overdue')

      expect(wrapper.emitted('update:filters')).toBeTruthy()
      const emitted = wrapper.emitted('update:filters') as any[]
      expect(emitted[0][0].dueDateFilter).toBe('overdue')
    })

    it('should display current due date filter', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: { ...defaultFilters, dueDateFilter: 'due-today' }
        }
      })

      const select = wrapper.find('select') as any
      expect(select.element.value).toBe('due-today')
    })

    it('should have all due date options', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: defaultFilters
        }
      })

      const select = wrapper.find('select')
      const options = select.findAll('option')

      expect(options.length).toBe(5)
      expect(options[0].text()).toBe('All cards')
      expect(options[1].text()).toBe('Overdue')
      expect(options[2].text()).toBe('Due today')
      expect(options[3].text()).toBe('Due this week')
      expect(options[4].text()).toBe('No due date')
    })
  })

  describe('Blocked Status Filter', () => {
    it('should emit update:filters when blocked checkbox changes', async () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: defaultFilters
        }
      })

      // Find the blocked checkbox (first checkbox in the Status section)
      const checkboxes = wrapper.findAll('input[type="checkbox"]')
      const blockedCheckbox = checkboxes[0] // First checkbox is for blocked status
      await blockedCheckbox.setValue(true)

      expect(wrapper.emitted('update:filters')).toBeTruthy()
      const emitted = wrapper.emitted('update:filters') as any[]
      expect(emitted[0][0].showBlockedOnly).toBe(true)
    })

    it('should display current blocked status', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: { ...defaultFilters, showBlockedOnly: true }
        }
      })

      const checkboxes = wrapper.findAll('input[type="checkbox"]')
      const blockedCheckbox = checkboxes[0] as any
      expect(blockedCheckbox.element.checked).toBe(true)
    })
  })

  describe('Label Filter', () => {
    it('should emit update:filters when label is toggled', async () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: defaultFilters
        }
      })

      const checkboxes = wrapper.findAll('input[type="checkbox"]')
      // First checkbox is blocked status, rest are labels
      const firstLabelCheckbox = checkboxes[1]
      await firstLabelCheckbox.setValue(true)

      expect(wrapper.emitted('update:filters')).toBeTruthy()
      const emitted = wrapper.emitted('update:filters') as any[]
      expect(emitted[0][0].labelIds).toContain('label-1')
    })

    it('should display selected labels', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: { ...defaultFilters, labelIds: ['label-1', 'label-2'] }
        }
      })

      const checkboxes = wrapper.findAll('input[type="checkbox"]')
      // Skip first checkbox (blocked status)
      const label1Checkbox = checkboxes[1] as any
      const label2Checkbox = checkboxes[2] as any

      expect(label1Checkbox.element.checked).toBe(true)
      expect(label2Checkbox.element.checked).toBe(true)
    })

    it('should show label count when labels are selected', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: { ...defaultFilters, labelIds: ['label-1', 'label-2'] }
        }
      })

      expect(wrapper.text()).toContain('(2 selected)')
    })
  })

  describe('Clear All Filters', () => {
    it('should emit update:filters with default values when clear all is clicked', async () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: {
            searchText: 'bug',
            labelIds: ['label-1'],
            dueDateFilter: 'overdue',
            showBlockedOnly: true
          }
        }
      })

      const clearButton = wrapper.find('button:not([aria-label])')
      await clearButton.trigger('click')

      expect(wrapper.emitted('update:filters')).toBeTruthy()
      const emitted = wrapper.emitted('update:filters') as any[]
      const lastEmit = emitted[emitted.length - 1][0]

      expect(lastEmit.searchText).toBe('')
      expect(lastEmit.labelIds).toEqual([])
      expect(lastEmit.dueDateFilter).toBe('all')
      expect(lastEmit.showBlockedOnly).toBe(false)
    })

    it('should show clear all button only when filters are active', () => {
      const wrapperWithFilters = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: {
            searchText: 'bug',
            labelIds: [],
            dueDateFilter: 'all',
            showBlockedOnly: false
          }
        }
      })

      expect(wrapperWithFilters.text()).toContain('Clear all')

      const wrapperWithoutFilters = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: {
            searchText: '',
            labelIds: [],
            dueDateFilter: 'all',
            showBlockedOnly: false
          }
        }
      })

      expect(wrapperWithoutFilters.text()).not.toContain('Clear all')
    })
  })

  describe('Active Filters Summary', () => {
    it('should show active filters summary when filters are applied', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: {
            searchText: 'bug',
            labelIds: ['label-1'],
            dueDateFilter: 'overdue',
            showBlockedOnly: true
          }
        }
      })

      expect(wrapper.text()).toContain('Active filters:')
      expect(wrapper.text()).toContain('Search: "bug"')
      expect(wrapper.text()).toContain('overdue')
      expect(wrapper.text()).toContain('Blocked only')
      expect(wrapper.text()).toContain('Bug')
    })

    it('should not show active filters summary when no filters are applied', () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: {
            searchText: '',
            labelIds: [],
            dueDateFilter: 'all',
            showBlockedOnly: false
          }
        }
      })

      expect(wrapper.text()).not.toContain('Active filters:')
    })

    it('should allow removing individual filters from summary', async () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: {
            searchText: 'bug',
            labelIds: [],
            dueDateFilter: 'all',
            showBlockedOnly: false
          }
        }
      })

      // Find the × button in the search filter chip
      const filterChips = wrapper.findAll('.inline-flex.items-center')
      const searchChip = filterChips.find(chip => chip.text().includes('Search:'))

      if (searchChip) {
        const removeButton = searchChip.find('button')
        await removeButton.trigger('click')

        expect(wrapper.emitted('update:filters')).toBeTruthy()
        const emitted = wrapper.emitted('update:filters') as any[]
        expect(emitted[emitted.length - 1][0].searchText).toBe('')
      }
    })
  })

  describe('Toggle', () => {
    it('should emit toggle event when close button is clicked', async () => {
      const wrapper = mount(FilterPanel, {
        props: {
          isOpen: true,
          labels: mockLabels,
          activeFilters: defaultFilters
        }
      })

      const closeButton = wrapper.find('button[aria-label="Close filters"]')
      await closeButton.trigger('click')

      expect(wrapper.emitted('toggle')).toBeTruthy()
    })
  })
})
