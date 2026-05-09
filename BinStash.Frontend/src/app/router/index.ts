import { createRouter, createWebHistory } from 'vue-router'
import { setupRouterGuards } from './guards'

import ChunkStores from '@/pages/ChunkStores.vue'
import ChunkStoreDetail from '@/pages/ChunkStoreDetail.vue'

import SelectTenant from '@/pages/SelectTenant.vue'
import TenantDashboard from '@/features/tenants/pages/TenantDashboardPage.vue'
import TenantRepositories from '@/features/tenants/pages/TenantRepositoriesPage.vue'
import RepositoryDetail from '@/features/tenants/pages/RepositoryDetailPage.vue'
import TenantMembers from '@/features/tenants/pages/TenantMembersPage.vue'
import TenantServiceAccounts from '@/features/tenants/pages/TenantServiceAccountsPage.vue'
import TenantSettings from '@/features/tenants/pages/TenantSettingsPage.vue'
import ReleaseDetail from '@/features/tenants/pages/ReleaseDetailPage.vue'

import PageNotFound from '@/pages/utility/PageNotFound.vue'
import Signin from '@/features/auth/pages/SigninPage.vue'
import Signup from '@/features/auth/pages/SignupPage.vue'
import ResetPassword from '@/features/auth/pages/ResetPasswordPage.vue'
import VerifyEmail from '@/features/auth/pages/VerifyEmailPage.vue'
import Onboarding01 from '@/pages/Onboarding01.vue'
import Onboarding02 from '@/pages/Onboarding02.vue'
import Onboarding03 from '@/pages/Onboarding03.vue'
import Onboarding04 from '@/pages/Onboarding04.vue'
import InvitationOnboarding from '@/pages/InvitationOnboarding.vue'
import SetupWizard from '@/features/setup/pages/SetupWizardPage.vue'
import InstanceDashboard from '@/features/instance/pages/InstanceDashboardPage.vue'
import InstanceSettings from '@/features/instance/pages/InstanceSettingsPage.vue'
import InstanceTenants from '@/features/instance/pages/InstanceTenantsPage.vue'
import InstanceUsers from '@/features/instance/pages/InstanceUsersPage.vue'

import InstanceAdminLayout from '@/app/layouts/InstanceAdminLayout.vue'
import TenantLayout from '@/app/layouts/TenantLayout.vue'
import AuthLayout from '@/app/layouts/AuthLayout.vue'
import SetupLayout from '@/app/layouts/SetupLayout.vue'

const routerHistory = createWebHistory()

const requiresAuth = { requiresAuth: true }
const requiresVerified = { requiresVerified: true }
const requiresOnboarding = { requiresOnboarding: true }
const requiresInstanceAdmin = { requiresAuth: true, requiresInstanceAdmin: true }
const guestOnly = { guestOnly: true }

export const router = createRouter({
  history: routerHistory,
  routes: [
    { path: '/', redirect: () => '/select-tenant', meta: { ...requiresAuth, ...requiresVerified } },

    { path: '/chunk-stores', component: ChunkStores, meta: { ...requiresAuth, ...requiresVerified } },
    { path: '/chunk-stores/:id', component: ChunkStoreDetail, meta: { ...requiresAuth, ...requiresVerified } },

    // Public / guest-only
    {
      path: '/signin',
      component: AuthLayout,
      children: [
        { path: '', component: Signin, meta: guestOnly },
      ],
    },
    {
      path: '/signup',
      component: AuthLayout,
      children: [
        { path: '', component: Signup, meta: guestOnly },
      ],
    },
    {
      path: '/reset-password',
      component: AuthLayout,
      children: [
        { path: '', component: ResetPassword, meta: guestOnly },
      ],
    },
    {
      path: '/verify-email',
      component: AuthLayout,
      children: [
        {
          path: '',
          component: VerifyEmail,
          alias: ['/confirmEmail', '/confirm-email'],
        },
      ],
    },

    // Error pages
    { path: '/error/404', component: PageNotFound },

    // Onboarding after auth
    { path: '/onboarding-01', component: Onboarding01, meta: { ...requiresAuth, ...requiresVerified, ...requiresOnboarding } },
    { path: '/onboarding-02', component: Onboarding02, meta: { ...requiresAuth, ...requiresVerified, ...requiresOnboarding } },
    { path: '/onboarding-03', component: Onboarding03, meta: { ...requiresAuth, ...requiresVerified, ...requiresOnboarding } },
    { path: '/onboarding-04', component: Onboarding04, meta: { ...requiresAuth, ...requiresVerified, ...requiresOnboarding } },

    // Tenant selection
    { path: '/select-tenant', component: SelectTenant, meta: { requiresAuth: true } },

    // Tenant workspace
    {
      path: '/t/:tenantId',
      component: TenantLayout,
      meta: { requiresAuth: true },
      children: [
        { path: '', component: TenantDashboard },
        { path: 'repositories', component: TenantRepositories },
        { path: 'repositories/:repoId', component: RepositoryDetail },
        { path: 'repositories/:repoId/releases/:releaseId', component: ReleaseDetail },
        { path: 'members', component: TenantMembers },
        { path: 'service-accounts', component: TenantServiceAccounts },
        { path: 'settings', component: TenantSettings },
      ],
    },

    // Instance Admin
    {
      path: '/instance',
      component: InstanceAdminLayout,
      meta: requiresInstanceAdmin,
      children: [
        { path: '', component: InstanceDashboard },
        { path: 'tenants', component: InstanceTenants },
        { path: 'users', component: InstanceUsers },
        { path: 'settings', component: InstanceSettings },
      ]
    },

    // Invitation onboarding (public — handles own auth state)
    { path: '/invite/:tenantId/:invitationCode', component: InvitationOnboarding, meta: { public: true } },

    // BinStash Setup Wizard
    {
      path: '/setup',
      component: SetupLayout,
      children: [
        { path: '', component: SetupWizard, meta: { public: true } },
      ],
    },

    { path: '/:pathMatch(.*)*', component: PageNotFound },
  ],
})

setupRouterGuards(router)
