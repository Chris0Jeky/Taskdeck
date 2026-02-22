import { createRouter, createWebHistory } from 'vue-router'
import BoardsListView from '../views/BoardsListView.vue'
import BoardView from '../views/BoardView.vue'
import LoginView from '../views/LoginView.vue'
import RegisterView from '../views/RegisterView.vue'
import ProfileSettingsView from '../views/ProfileSettingsView.vue'
import BoardAccessView from '../views/BoardAccessView.vue'
import ActivityView from '../views/ActivityView.vue'
import AutomationQueueView from '../views/AutomationQueueView.vue'
import AutomationChatView from '../views/AutomationChatView.vue'
import OpsConsoleView from '../views/OpsConsoleView.vue'
import ExportImportView from '../views/ExportImportView.vue'
import ArchiveView from '../views/ArchiveView.vue'
import NotificationInboxView from '../views/NotificationInboxView.vue'
import NotificationPreferencesView from '../views/NotificationPreferencesView.vue'
import { isTokenExpired } from '../utils/jwt'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    // Public routes
    {
      path: '/login',
      name: 'login',
      component: LoginView,
      meta: { public: true },
    },
    {
      path: '/register',
      name: 'register',
      component: RegisterView,
      meta: { public: true },
    },

    // Legacy routes (backward compatible)
    {
      path: '/',
      redirect: '/workspace/boards',
    },
    {
      path: '/boards',
      redirect: '/workspace/boards',
    },
    {
      path: '/boards/:id',
      redirect: (to) => `/workspace/boards/${to.params.id}`,
    },

    // Workspace routes
    {
      path: '/workspace/boards',
      name: 'workspace-boards',
      component: BoardsListView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/boards/:id',
      name: 'workspace-board',
      component: BoardView,
      props: true,
      meta: { requiresShell: true },
    },

    // Activity routes
    {
      path: '/workspace/activity',
      name: 'workspace-activity',
      component: ActivityView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/activity/board/:boardId',
      name: 'workspace-activity-board',
      component: ActivityView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/activity/entity/:entityType/:entityId',
      name: 'workspace-activity-entity',
      component: ActivityView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/activity/user',
      name: 'workspace-activity-user',
      component: ActivityView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/activity/user/:userId',
      redirect: '/workspace/activity/user',
    },

    // Automation routes
    {
      path: '/workspace/automations/queue',
      name: 'workspace-automations-queue',
      component: AutomationQueueView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/automations/proposals',
      name: 'workspace-automations-proposals',
      component: AutomationQueueView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/automations/chat',
      name: 'workspace-automations-chat',
      component: AutomationChatView,
      meta: { requiresShell: true },
    },

    // Ops routes
    {
      path: '/workspace/ops/cli',
      name: 'workspace-ops-cli',
      component: OpsConsoleView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/ops/endpoints',
      name: 'workspace-ops-endpoints',
      component: OpsConsoleView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/ops/logs',
      name: 'workspace-ops-logs',
      component: OpsConsoleView,
      meta: { requiresShell: true },
    },

    // Settings routes
    {
      path: '/workspace/settings/profile',
      name: 'workspace-settings-profile',
      component: ProfileSettingsView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/settings/access/:boardId?',
      name: 'workspace-settings-access',
      component: BoardAccessView,
      props: (route) => ({
        boardId: typeof route.params.boardId === 'string' ? route.params.boardId : null,
      }),
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/settings/export-import',
      name: 'workspace-settings-export-import',
      component: ExportImportView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/settings/preferences',
      name: 'workspace-settings-preferences',
      component: NotificationPreferencesView,
      meta: { requiresShell: true },
    },

    // Archive route
    {
      path: '/workspace/archive',
      name: 'workspace-archive',
      component: ArchiveView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/notifications',
      name: 'workspace-notifications',
      component: NotificationInboxView,
      meta: { requiresShell: true },
    },
  ],
})

// Navigation guard for auth
router.beforeEach((to) => {
  const isPublic = to.meta.public === true
  const token = localStorage.getItem('taskdeck_token')
  const tokenValid = !!token && !isTokenExpired(token)

  if (token && !tokenValid) {
    localStorage.removeItem('taskdeck_token')
    localStorage.removeItem('taskdeck_session')
  }

  if (!isPublic && !tokenValid && to.path.startsWith('/workspace')) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }

  if (isPublic && tokenValid && (to.path === '/login' || to.path === '/register')) {
    return { path: '/workspace/boards' }
  }
})

export default router
