import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'

import { registerGuards } from './guards'

/**
 * Route meta drives the guards; keep the declarations here rather than in the guard so
 * the access rules for a page are readable next to the page.
 */
declare module 'vue-router' {
  interface RouteMeta {
    /** Reachable while signed out (and while setup is incomplete). */
    public?: boolean
    /** Signed-out only; signed-in users get redirected away. */
    guestOnly?: boolean
    requiresAuth?: boolean
    requiresVerifiedEmail?: boolean
    /** Needs a selected tenant; redirects to the tenant picker when there is none. */
    requiresTenant?: boolean
    requiresInstanceAdmin?: boolean
    title?: string
  }
}

const routes: RouteRecordRaw[] = [
  { path: '/', redirect: { name: 'tenant-home' } },

  {
    path: '/setup',
    component: () => import('@/layouts/SetupLayout.vue'),
    children: [
      {
        path: '',
        name: 'setup',
        component: () => import('@/pages/setup/SetupWizardPage.vue'),
        meta: { public: true, title: 'Setup' },
      },
    ],
  },

  {
    path: '/',
    component: () => import('@/layouts/AuthLayout.vue'),
    children: [
      {
        path: 'sign-in',
        name: 'sign-in',
        component: () => import('@/pages/auth/SignInPage.vue'),
        meta: { guestOnly: true, title: 'Sign in' },
      },
      {
        path: 'sign-up',
        name: 'sign-up',
        component: () => import('@/pages/auth/SignUpPage.vue'),
        meta: { guestOnly: true, title: 'Create account' },
      },
      {
        path: 'forgot-password',
        name: 'forgot-password',
        component: () => import('@/pages/auth/ForgotPasswordPage.vue'),
        meta: { guestOnly: true, title: 'Forgot password' },
      },
      {
        path: 'reset-password',
        name: 'reset-password',
        component: () => import('@/pages/auth/ResetPasswordPage.vue'),
        meta: { public: true, title: 'Reset password' },
      },
      {
        path: 'verify-email',
        name: 'verify-email',
        alias: ['/confirm-email', '/confirmEmail'],
        component: () => import('@/pages/auth/VerifyEmailPage.vue'),
        meta: { public: true, title: 'Verify email' },
      },
      {
        path: 'invite/:tenantId/:code',
        name: 'invitation',
        component: () => import('@/pages/onboarding/InvitationPage.vue'),
        meta: { public: true, title: 'Invitation' },
      },
      {
        path: 'workspaces',
        name: 'select-tenant',
        component: () => import('@/pages/onboarding/SelectWorkspacePage.vue'),
        meta: { requiresAuth: true, requiresVerifiedEmail: true, title: 'Workspaces' },
      },
      {
        path: 'workspaces/new',
        name: 'create-tenant',
        component: () => import('@/pages/onboarding/CreateWorkspacePage.vue'),
        meta: { requiresAuth: true, requiresVerifiedEmail: true, title: 'New workspace' },
      },
    ],
  },

  {
    path: '/t/:tenantId',
    component: () => import('@/layouts/TenantLayout.vue'),
    meta: { requiresAuth: true, requiresVerifiedEmail: true, requiresTenant: true },
    children: [
      {
        path: '',
        name: 'tenant-home',
        component: () => import('@/pages/tenant/OverviewPage.vue'),
        meta: { title: 'Overview' },
      },
      {
        path: 'repositories',
        name: 'repositories',
        component: () => import('@/pages/tenant/RepositoriesPage.vue'),
        meta: { title: 'Repositories' },
      },
      {
        path: 'repositories/:repoId',
        name: 'repository',
        component: () => import('@/pages/tenant/RepositoryDetailPage.vue'),
        meta: { title: 'Repository' },
      },
      {
        path: 'repositories/:repoId/releases/:releaseId',
        name: 'release',
        component: () => import('@/pages/tenant/ReleaseDetailPage.vue'),
        meta: { title: 'Release' },
      },
      {
        path: 'members',
        name: 'members',
        component: () => import('@/pages/tenant/MembersPage.vue'),
        meta: { title: 'Members' },
      },
      {
        path: 'service-accounts',
        name: 'service-accounts',
        component: () => import('@/pages/tenant/ServiceAccountsPage.vue'),
        meta: { title: 'Service accounts' },
      },
      {
        path: 'usage',
        name: 'usage',
        component: () => import('@/pages/tenant/UsagePage.vue'),
        meta: { title: 'Usage' },
      },
      {
        path: 'audit',
        name: 'audit',
        component: () => import('@/pages/tenant/AuditLogPage.vue'),
        meta: { title: 'Audit log' },
      },
      {
        path: 'settings',
        name: 'tenant-settings',
        component: () => import('@/pages/tenant/SettingsPage.vue'),
        meta: { title: 'Workspace settings' },
      },
    ],
  },

  {
    path: '/instance',
    component: () => import('@/layouts/InstanceLayout.vue'),
    meta: { requiresAuth: true, requiresVerifiedEmail: true, requiresInstanceAdmin: true },
    children: [
      {
        path: '',
        name: 'instance-home',
        component: () => import('@/pages/instance/InstanceOverviewPage.vue'),
        meta: { title: 'Instance' },
      },
      {
        path: 'tenants',
        name: 'instance-tenants',
        component: () => import('@/pages/instance/InstanceTenantsPage.vue'),
        meta: { title: 'Tenants' },
      },
      {
        path: 'users',
        name: 'instance-users',
        component: () => import('@/pages/instance/InstanceUsersPage.vue'),
        meta: { title: 'Users' },
      },
      {
        path: 'chunk-stores',
        name: 'chunk-stores',
        component: () => import('@/pages/instance/ChunkStoresPage.vue'),
        meta: { title: 'Chunk stores' },
      },
      {
        path: 'chunk-stores/:chunkStoreId',
        name: 'chunk-store',
        component: () => import('@/pages/instance/ChunkStoreDetailPage.vue'),
        meta: { title: 'Chunk store' },
      },
      {
        path: 'audit',
        name: 'instance-audit',
        component: () => import('@/pages/instance/InstanceAuditLogPage.vue'),
        meta: { title: 'Audit log' },
      },
      {
        path: 'settings',
        name: 'instance-settings',
        component: () => import('@/pages/instance/InstanceSettingsPage.vue'),
        meta: { title: 'Instance settings' },
      },
    ],
  },

  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('@/pages/NotFoundPage.vue'),
    meta: { public: true, title: 'Not found' },
  },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: (_to, _from, saved) => saved ?? { top: 0 },
})

registerGuards(router)
