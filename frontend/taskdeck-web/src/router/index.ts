import { createRouter, createWebHistory } from 'vue-router'
import LoginView from '../views/LoginView.vue'
import RegisterView from '../views/RegisterView.vue'
import { isTokenExpired } from '../utils/jwt'
import { isDemoMode, isDemoSessionActive } from '../utils/demoMode'
import * as tokenStorage from '../utils/tokenStorage'
import { usePerformanceMark } from '../composables/usePerformanceMark'
import { useFeatureFlagStore } from '../store/featureFlagStore'
import type { FeatureFlags } from '../types/feature-flags'

// Augment vue-router's RouteMeta so that `requiresFlag` is type-safe throughout.
declare module 'vue-router' {
  interface RouteMeta {
    public?: boolean
    requiresShell?: boolean
    automationSurface?: string
    requiresFlag?: keyof FeatureFlags
  }
}

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
const DevToolsView = () => import('../views/DevToolsView.vue')

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
      meta: { requiresShell: true, requiresFlag: 'newActivity' },
    },
    {
      path: '/workspace/activity/board/:boardId',
      name: 'workspace-activity-board',
      component: ActivityView,
      meta: { requiresShell: true, requiresFlag: 'newActivity' },
    },
    {
      path: '/workspace/activity/entity/:entityType/:entityId',
      name: 'workspace-activity-entity',
      component: ActivityView,
      meta: { requiresShell: true, requiresFlag: 'newActivity' },
    },
    {
      path: '/workspace/activity/user',
      name: 'workspace-activity-user',
      component: ActivityView,
      meta: { requiresShell: true, requiresFlag: 'newActivity' },
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
      meta: { requiresShell: true, automationSurface: 'queue', requiresFlag: 'newAutomation' },
    },
    {
      path: '/workspace/review',
      name: 'workspace-review',
      component: ReviewView,
      meta: { requiresShell: true, automationSurface: 'review', requiresFlag: 'newAutomation' },
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
      meta: { requiresShell: true, requiresFlag: 'newAutomation' },
    },

    // Ops routes
    {
      path: '/workspace/ops/cli',
      name: 'workspace-ops-cli',
      component: OpsConsoleView,
      meta: { requiresShell: true, requiresFlag: 'newOps' },
    },
    {
      path: '/workspace/ops/endpoints',
      name: 'workspace-ops-endpoints',
      component: OpsConsoleView,
      meta: { requiresShell: true, requiresFlag: 'newOps' },
    },
    {
      path: '/workspace/ops/logs',
      name: 'workspace-ops-logs',
      component: OpsConsoleView,
      meta: { requiresShell: true, requiresFlag: 'newOps' },
    },

    // Settings routes
    {
      path: '/workspace/settings/profile',
      name: 'workspace-settings-profile',
      component: ProfileSettingsView,
      meta: { requiresShell: true, requiresFlag: 'newAuth' },
    },
    {
      path: '/workspace/settings/access/:boardId?',
      name: 'workspace-settings-access',
      component: BoardAccessView,
      props: (route) => ({
        boardId: typeof route.params.boardId === 'string' ? route.params.boardId : null,
      }),
      meta: { requiresShell: true, requiresFlag: 'newAccess' },
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
      meta: { requiresShell: true, requiresFlag: 'newArchive' },
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

    // Internal dev tooling (trace replay + scenario editor)
    {
      path: '/workspace/dev-tools',
      name: 'workspace-dev-tools',
      component: DevToolsView,
      meta: { requiresShell: true, requiresFlag: 'devTools' },
    },
  ],
})

// Route-transition performance instrumentation
const routePerf = usePerformanceMark('route-transition')

// Navigation guard for auth and feature flags
router.beforeEach((to) => {
  routePerf.start()

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

  // Feature-flag gate: block direct URL access to flagged routes when the flag
  // is disabled. The store is read synchronously from localStorage on first
  // access so the guard works correctly on hard refresh before App.vue mounts.
  const requiredFlag = to.meta.requiresFlag
  if (requiredFlag !== undefined) {
    const featureFlags = useFeatureFlagStore()
    // Restore from localStorage in case App.vue hasn't mounted yet (direct nav).
    featureFlags.restore()
    if (!featureFlags.isEnabled(requiredFlag)) {
      return { path: '/workspace/home' }
    }
  }
})

router.afterEach(() => {
  routePerf.end()
})

export default router
