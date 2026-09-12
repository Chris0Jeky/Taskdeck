/**
 * Card-modal surface - English source catalog.
 *
 * This is intentionally separate from boardDetail.ts: it covers the shared
 * card editor used by both Paper and Legacy board views.
 */
export default {
  workItemType: {
    label: 'Work item type',
    task: 'Task',
    epic: 'Epic',
    spike: 'Spike',
    permissionChecking: 'Checking whether you can edit this board…',
    permissionUnknown: 'The loaded board does not say whether you can edit it.',
    permissionRefresh: 'Refresh permission',
  },
  commentDelete: {
    title: 'Delete comment?',
    description: 'This comment will be deleted. This action cannot be undone.',
    cancel: 'Keep comment',
    confirm: 'Delete comment',
    deleting: 'Deleting…',
  },
}
