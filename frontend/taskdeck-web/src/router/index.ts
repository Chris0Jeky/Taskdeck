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
import InboxView from '../views/InboxView.vue'
import HomeView from '../views/HomeView.vue'
import TodayView from '../views/TodayView.vue'
import ReviewView from '../views/ReviewView.vue'
import { isTokenExpired } from '../utils/jwt'
import { isDemoMode, isDemoSessionActive } from '../utils/demoMode'
import * as tokenStorage from '../utils/tokenStorage'

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
      redirect: '/workspace/home',
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
      path: '/workspace',
      redirect: '/workspace/home',
    },
    {
      path: '/workspace/home',
      name: 'workspace-home',
      component: HomeView,
      meta: { requiresShell: true },
    },
    {
      path: '/workspace/today',
      name: 'workspace-today',
      component: TodayView,
      meta: { requiresShell: true },
    },
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
      path: '/workspace/automations',
      redirect: (to) => ({
        name: 'workspace-review',
        hash: to.hash,
        query: to.query,
      }),
    },
    {
      path: '/workspace/automations/queue',
      name: 'workspace-automations-queue',
      component: AutomationQueueView,
      meta: { requiresShell: true, automationSurface: 'queue' },
    },
    {
      path: '/workspace/review',
      name: 'workspace-review',
      component: ReviewView,
      meta: { requiresShell: true, automationSurface: 'review' },
    },
    {
      path: '/workspace/automations/proposals',
      redirect: (to) => ({
        name: 'workspace-review',
        hash: to.hash,
        query: to.query,
      }),
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
      path: '/workspace/inbox',
      name: 'workspace-inbox',
      component: InboxView,
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
  const demoActive = isDemoMode && isDemoSessionActive()
  const token = tokenStorage.getToken()
  const tokenValid = !!token && !isTokenExpired(token)
  const hasValidSession = tokenValid || demoActive

  if (token && !tokenValid) {
    tokenStorage.clearAll()
  }

  if (!isPublic && !hasValidSession && to.path.startsWith('/workspace')) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }

  if (isPublic && hasValidSession && (to.path === '/login' || to.path === '/register')) {
    return { path: '/workspace/home' }
  }
})

export default router
