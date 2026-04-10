import type { Preview } from '@storybook/vue3-vite'
import '../src/style.css'

const preview: Preview = {
  parameters: {
    controls: { expanded: true },
    backgrounds: {
      default: 'obsidian',
      values: [
        { name: 'obsidian', value: '#131313' },
        { name: 'light', value: '#f5f3f1' },
      ],
    },
  },
  decorators: [
    (story) => ({
      components: { story },
      template: `
        <div style="padding: 1rem;">
          <story />
        </div>
      `,
    }),
  ],
}

export default preview
