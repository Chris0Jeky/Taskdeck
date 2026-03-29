import { createRouter, createWebHistory } from 'vue-router'
import LoginView from '../views/LoginView.vue'
import RegisterView from '../views/RegisterView.vue'
import { isTokenExpired } from '../utils/jwt'
import { isDemoMode, isDemoSessionActive } from '../utils/demoMode'
import { usePerformanceMark } from '../composables/usePerformanceMark'

// Lazy-loaded route components — keeps initial bundle small and speeds up
// first-paint for login/register (the only eagerly-loaded views).
const BoardsListView = () => import('../views/BoardsListView.vue')
const BoardView = () => import('../views/BoardView.vue')
const ProfileSettingsView = () => import('../views/ProfileSettingsView.vue')
const BoardAccessView = () => import('../views/BoardAccessView.vue')
const ActivityView = () => import('../views/ActivityView.vue')
const AutomationQueueView = () => import('../views/AutomationQueueView.vue')
const AutomationChatView = () => import('../views/AutomationChatView.vue')
const OpsConsoleView = () => import('../views/OpsConsoleView.vue')
const ExportImportView = () => import('../views/ExportImportView.vue')
const ArchiveView = () => import('../views/ArchiveView.vue')
const NotificationInboxView = () => import('../views/NotificationInboxView.vue')
const NotificationPreferencesView = () => import('../views/NotificationPreferencesView.vue')
const InboxView = () => import('../views/InboxView.vue')
const HomeView = () => import('../views/HomeView.vue')
const TodayView = () => import('../views/TodayView.vue')
const ReviewView = () => import('../views/ReviewView.vue')

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

// Route-transition performance instrumentation
const routePerf = usePerformanceMark('route-transition')

// Navigation guard for auth
router.beforeEach((to) => {
  routePerf.start()

  const isPublic = to.meta.public === true
  const demoActive = isDemoMode && isDemoSessionActive()
  const token = localStorage.getItem('taskdeck_token')
  const tokenValid = !!token && !isTokenExpired(token)
  const hasValidSession = tokenValid || demoActive

  if (token && !tokenValid) {
    localStorage.removeItem('taskdeck_token')
    localStorage.removeItem('taskdeck_session')
  }

  if (!isPublic && !hasValidSession && to.path.startsWith('/workspace')) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }

  if (isPublic && hasValidSession && (to.path === '/login' || to.path === '/register')) {
    return { path: '/workspace/home' }
  }
})

router.afterEach(() => {
  routePerf.end()
})

export default router
