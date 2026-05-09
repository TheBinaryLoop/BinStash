import type { Router } from 'vue-router'

import { listTenantsForMember } from '../../api/tenants'
import { useAuthStore } from '../../stores/auth'
import { useTenantStore } from '../../stores/tenant'
import { useSetupStore } from '../../features/setup/store/setup.store'

export function setupRouterGuards(router: Router) {
  router.beforeEach(async (to) => {
    const setupStore = useSetupStore()
    if (!setupStore.status && !setupStore.loading && !setupStore.error && !setupStore.isInitialized) {
      await setupStore.fetchStatus()
    }
    // Block everything except /setup if setup is required
    if (
      setupStore.error === 'setup_required' ||
      setupStore.status === null ||
      setupStore.isInitialized === false
    ) {
      if (to.path !== '/setup') {
        return '/setup'
      }
      return true
    }
    // Allow public access to /setup (setup wizard)
    if (to.path === '/setup' || to.meta.public) {
      return true
    }

    const auth = useAuthStore()
    const tenant = useTenantStore()

    // 1) Restore auth once per reload
    if (!auth.user && !auth.isRestoring) {
      await auth.restore()
    }

    const isAuthed = !!auth.user

    // 2) Guest-only pages
    if (to.meta.guestOnly && isAuthed) return '/select-tenant'

    // 3) Protected pages
    if (to.meta.requiresAuth && !isAuthed) {
      return withRedirect('/signin', to)
    }

    // 3b) Instance admin guard
    if (to.meta.requiresInstanceAdmin) {
      if (!auth.user?.roles?.includes('InstanceAdmin')) {
        // Non-admins go to their tenant home if they have one, otherwise select-tenant
        return tenant.currentTenantId ? `/t/${tenant.currentTenantId}` : '/select-tenant'
      }
      return true
    }

    // from here: only authed users matter
    if (!isAuthed) return true

    // 4) Email verification gating (preserve redirect)
    if (to.meta.requiresVerified && !auth.user!.isEmailConfirmed) {
      return withRedirect('/verify-email', to)
    }

    // 5) Onboarding gating (preserve redirect)
    const isVerifyEmail =
      to.path.startsWith('/verify-email') ||
      to.path.startsWith('/confirmEmail') ||
      to.path.startsWith('/confirm-email')
    if (
      auth.user!.isEmailConfirmed &&
      !auth.user!.onboardingCompleted &&
      !to.meta.requiresOnboarding &&
      !isVerifyEmail
    ) {
      return withRedirect('/onboarding-01', to)
    }

    if (to.meta.requiresOnboarding && auth.user!.onboardingCompleted) {
      return '/select-tenant'
    }

    // ---- TENANCY ----

    // 6) Ensure tenant list is loaded (only once, only when authed)
    if (!tenant.isLoaded && !tenant.isLoading) {
      tenant.isLoading = true
      try {
        const tenants = await listTenantsForMember()
        tenant.setTenants(tenants)
      } finally {
        tenant.isLoading = false
      }
    }

    // 7) Determine if this is a tenant route
    const tenantIdParam =
      typeof to.params?.tenantId === 'string' && to.params.tenantId.length > 0
        ? (to.params.tenantId as string)
        : null

    const isTenantRoute = !!tenantIdParam

    // 8) If user is on a tenant route: validate membership + set currentTenant
    if (isTenantRoute) {
      const exists = tenant.tenants.some(t => t.tenantId === tenantIdParam)

      // Instance admins can open any tenant directly, even if local tenant cache is stale.
      if (!exists && !auth.user?.roles?.includes('InstanceAdmin')) {
        // Unknown tenant => force tenant selection (keep redirect)
        return withRedirect('/select-tenant', to)
      }

      // keep store in sync with route
      if (tenant.currentTenantId !== tenantIdParam) {
        tenant.setCurrentTenant(tenantIdParam)
      }

      return true
    }

    // 9) For pages that should require a tenant but are not tenant-scoped,
    // use meta.requiresTenant (handy for transitional refactors)
    if (to.meta.requiresTenant) {
      // if we have a saved tenant, send user to tenant home (or to redirect if tenant-scoped)
      if (tenant.currentTenantId) {
        return `/t/${tenant.currentTenantId}`
      }
      return withRedirect('/select-tenant', to)
    }

    // 10) Prevent staying on select-tenant when a destination is already clear
    if (to.path === '/select-tenant') {
      // If user came here explicitly with a redirect, honour it
      const hasRedirect = typeof to.query?.redirect === 'string'
      if (!hasRedirect) {
        // Instance admins always default to the instance dashboard
        if (auth.user?.roles?.includes('InstanceAdmin')) {
          return '/instance'
        }
        // Regular users with a saved tenant go straight there
        if (tenant.currentTenantId) {
          return `/t/${tenant.currentTenantId}`
        }
      }
    }

    return true
  })
}

function withRedirect(targetPath: string, to: any) {
  // If there is already a redirect in the query, keep it. Otherwise use current fullPath.
  const existing = typeof to.query?.redirect === 'string' ? to.query.redirect : null
  const redirect = existing ?? to.fullPath
  return { path: targetPath, query: { redirect } }
}