import type { RouteLocationNormalized, Router } from 'vue-router'

import { apiJson } from '@/lib/http'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'

interface SetupStatus {
  /** The server reports setup completion as `isInitialized` on /api/setup/status. */
  isInitialized: boolean
}

/**
 * Setup state is instance-wide and only ever transitions once (incomplete -> complete),
 * so it is fetched at most once per page load rather than on every navigation.
 */
let setupComplete: boolean | null = null

async function isSetupComplete(): Promise<boolean> {
  if (setupComplete !== null) return setupComplete
  try {
    const status = await apiJson<SetupStatus>('/api/setup/status')
    setupComplete = status.isInitialized
  } catch {
    // If the check itself fails, do not strand the user in the wizard.
    setupComplete = true
  }
  return setupComplete
}

export function markSetupComplete() {
  setupComplete = true
}

export function registerGuards(router: Router) {
  router.beforeEach(async (to) => {
    // An unconfigured instance has nothing to show; funnel everything to the wizard.
    if (to.name !== 'setup' && !(await isSetupComplete())) {
      return { name: 'setup' }
    }
    if (to.name === 'setup' && (await isSetupComplete())) {
      return { name: 'tenant-home' }
    }

    const auth = useAuthStore()
    await auth.ready()

    if (to.meta.guestOnly && auth.isAuthenticated) {
      return { name: 'tenant-home' }
    }

    if (to.meta.public) return true

    if (to.meta.requiresAuth && !auth.isAuthenticated) {
      return { name: 'sign-in', query: { redirect: to.fullPath } }
    }

    // An unverified account can still reach the verify screen, but nothing else.
    if (to.meta.requiresVerifiedEmail && auth.isAuthenticated && !auth.isEmailVerified) {
      return { name: 'verify-email', query: { email: auth.user?.email ?? '' } }
    }

    if (to.meta.requiresInstanceAdmin && !auth.isInstanceAdmin) {
      return { name: 'not-found' }
    }

    if (to.meta.requiresTenant) {
      const redirect = await resolveTenant(to)
      if (redirect) return redirect
    }

    return true
  })

  router.afterEach((to) => {
    const title = to.meta.title
    document.title = title ? `${title} · BinStash` : 'BinStash'
  })
}

/**
 * Keeps the URL and the active tenant in agreement. The tenant in the path wins, so
 * links and bookmarks to another workspace switch context instead of silently
 * rendering the current one's data.
 */
async function resolveTenant(to: RouteLocationNormalized) {
  const tenants = useTenantStore()
  await tenants.ready()

  if (!tenants.hasTenants) {
    return { name: 'create-tenant' as const }
  }

  const routeTenantId = typeof to.params.tenantId === 'string' ? to.params.tenantId : null

  if (routeTenantId) {
    const known = tenants.tenants.some((tenant) => tenant.id === routeTenantId)
    if (!known) return { name: 'select-tenant' as const }

    if (routeTenantId !== tenants.activeTenantId) {
      await tenants.switchTenant(routeTenantId)
    }
    return null
  }

  if (!tenants.activeTenantId) {
    return { name: 'select-tenant' as const }
  }

  return {
    name: to.name as string,
    params: { ...to.params, tenantId: tenants.activeTenantId },
    query: to.query,
  }
}
