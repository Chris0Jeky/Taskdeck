<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { CardWorkItemType, Card } from '../../../types/board'
import { TdDateField } from '../../ui'

defineProps<{
  card: Card
  canEditType: boolean
  /** A server read of the caller's board write permission is in flight (#2952). */
  typePermissionChecking?: boolean
  /** The caller's board write permission could not be established; offer recovery (#2952). */
  typePermissionUnknown?: boolean
  formattedDueDate: string
  isOverdue: boolean
}>()

const { t } = useI18n()
const workItemType = defineModel<CardWorkItemType>('workItemType', { required: true })
const title = defineModel<string>('title', { required: true })
const description = defineModel<string>('description', { required: true })
const dueDate = defineModel<string>('dueDate', { required: true })
const isBlocked = defineModel<boolean>('isBlocked', { required: true })
const blockReason = defineModel<string>('blockReason', { required: true })

defineEmits<{
  (e: 'clear-due-date'): void
  (e: 'refresh-type-permission'): void
}>()
</script>

<template>
  <!-- Title -->
  <div>
    <label for="card-title" class="block text-sm font-medium text-on-surface-variant mb-1">
      Title *
    </label>
    <input
      id="card-title"
      v-model="title"
      type="text"
      required
      class="w-full px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-md text-on-surface placeholder-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary"
      placeholder="Card title"
    />
  </div>

  <div>
    <label for="card-work-item-type" class="block text-sm font-medium text-on-surface-variant mb-1">
      {{ t('cardModal.workItemType.label') }}
    </label>
    <select id="card-work-item-type" v-model="workItemType" :disabled="!canEditType"
      class="w-full rounded-md border border-outline-variant/40 bg-surface-container-high px-3 py-2 text-on-surface focus:outline-none focus:ring-2 focus:ring-primary disabled:opacity-70">
      <option value="Task">{{ t('cardModal.workItemType.task') }}</option>
      <option value="Epic">{{ t('cardModal.workItemType.epic') }}</option>
      <option value="Spike">{{ t('cardModal.workItemType.spike') }}</option>
    </select>
    <!--
      An unknown write permission is a state the user can act on, so it says so and offers
      the read again, instead of leaving a disabled control with no explanation. The message
      changes around a retry control that STAYS mounted: unmounting the button a keyboard
      user just activated would drop focus out of the editor's tab cycle.
    -->
    <div
      v-if="typePermissionChecking || typePermissionUnknown"
      class="mt-1 flex flex-wrap items-center gap-2 text-xs text-on-surface-variant"
    >
      <span role="status">
        <span v-if="typePermissionChecking" data-testid="card-type-permission-checking">
          {{ t('cardModal.workItemType.permissionChecking') }}
        </span>
        <span v-else data-testid="card-type-permission-unknown">
          {{ t('cardModal.workItemType.permissionUnknown') }}
        </span>
      </span>
      <button
        type="button"
        data-testid="card-type-permission-refresh"
        :disabled="typePermissionChecking"
        class="rounded-md border border-outline-variant/40 px-2 py-1 text-xs text-on-surface hover:bg-surface-container-high disabled:opacity-70"
        @click="$emit('refresh-type-permission')"
      >
        {{ t('cardModal.workItemType.permissionRefresh') }}
      </button>
    </div>
  </div>

  <!-- Description -->
  <div>
    <label for="card-description" class="block text-sm font-medium text-on-surface-variant mb-1">
      Description
    </label>
    <textarea
      id="card-description"
      v-model="description"
      rows="4"
      class="w-full px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-md text-on-surface placeholder-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary"
      placeholder="Add a more detailed description..."
    ></textarea>
  </div>

  <!-- Due Date -->
  <div>
    <label for="card-due-date" class="block text-sm font-medium text-on-surface-variant mb-1">
      Due Date
    </label>
    <div class="flex gap-2">
      <TdDateField
        id="card-due-date"
        v-model="dueDate"
        class="flex-1 px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-md text-on-surface focus:outline-none focus:ring-2 focus:ring-primary"
      />
      <button
        v-if="dueDate"
        @click="$emit('clear-due-date')"
        type="button"
        class="px-3 py-2 text-sm text-on-surface-variant hover:text-on-surface border border-outline-variant/40 rounded-md hover:bg-surface-container-high transition-colors"
      >
        Clear
      </button>
    </div>
    <p v-if="card.dueDate" class="mt-1 text-xs" :class="isOverdue ? 'text-error' : 'text-on-surface-variant'">
      Current: {{ formattedDueDate }}
      <span v-if="isOverdue" class="font-medium">(Overdue)</span>
    </p>
  </div>

  <!-- Blocked Status -->
  <div class="border border-outline-variant/30 rounded-md p-4">
    <div class="flex items-center mb-2">
      <input
        id="card-is-blocked"
        v-model="isBlocked"
        type="checkbox"
        class="w-4 h-4 text-primary border-outline-variant rounded"
      />
      <label for="card-is-blocked" class="ml-2 text-sm font-medium text-on-surface-variant">
        Mark as blocked
      </label>
    </div>
    <div v-if="isBlocked">
      <label for="card-block-reason" class="block text-sm font-medium text-on-surface-variant mb-1">
        Block Reason *
      </label>
      <textarea
        id="card-block-reason"
        v-model="blockReason"
        rows="2"
        required
        class="w-full px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-md text-on-surface placeholder-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary"
        placeholder="Why is this card blocked?"
      ></textarea>
    </div>
  </div>
</template>
