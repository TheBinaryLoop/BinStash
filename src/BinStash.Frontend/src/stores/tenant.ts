import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { apolloClient, resetApolloStore, setApolloTenant } from '@/lib/apollo'
import { MyTenantsDocument } from '@/graphql/generated'
import type { MyTenantsQuery } from '@/graphql/generated'

export type TenantSummary = NonNullable<NonNullable<MyTenantsQuery['tenants']>['nodes']>[number]

const STORAGE_KEY = 'binstash.tenant'

export const useTenantStore = defineStore('tenant', () => {
  const tenants = ref<TenantSummary[]>([])
  const activeTenantId = ref<string | null>(readStoredTenantId())
  const loaded = ref(false)
  let inflight: Promise<void> | null = null

  const activeTenant = computed(
    () => tenants.value.find((tenant) => tenant.id === activeTenantId.value) ?? null,
  )
  const hasTenants = computed(() => tenants.value.length > 0)
  const isTenantAdmin = computed(
    () => activeTenant.value?.myRoles.includes('TenantAdmin') === true,
  )

  // Publish to the Apollo link immediately: a restored id must be on the very first request.
  setApolloTenant(activeTenantId.value)

  async function ready(): Promise<void> {
    if (loaded.value) return
    inflight ??= load().finally(() => {
      inflight = null
      loaded.value = true
    })
    return inflight
  }

  async function load(): Promise<void> {
    const { data } = await apolloClient.query({
      query: MyTenantsDocument,
      variables: { first: 100 },
      fetchPolicy: 'network-only',
    })

    tenants.value = (data?.tenants?.nodes ?? []).filter((node): node is TenantSummary => !!node)

    // A stored tenant the user has since been removed from must not stay selected.
    if (activeTenantId.value && !tenants.value.some((t) => t.id === activeTenantId.value)) {
      setActiveTenant(null)
    }

    // With exactly one tenant there is no meaningful choice to present.
    if (!activeTenantId.value && tenants.value.length === 1) {
      setActiveTenant(tenants.value[0]!.id)
    }
  }

  function setActiveTenant(tenantId: string | null) {
    activeTenantId.value = tenantId
    setApolloTenant(tenantId)

    try {
      if (tenantId) localStorage.setItem(STORAGE_KEY, tenantId)
      else localStorage.removeItem(STORAGE_KEY)
    } catch {
      /* private mode — selection just won't survive a reload */
    }
  }

  /**
   * Switches tenant and drops the cache. Cached entities are tenant-scoped, so
   * carrying them across a switch would show one tenant's data under another.
   */
  async function switchTenant(tenantId: string) {
    if (tenantId === activeTenantId.value) return
    setActiveTenant(tenantId)
    await resetApolloStore()
  }

  function reset() {
    tenants.value = []
    loaded.value = false
    setActiveTenant(null)
  }

  return {
    tenants,
    activeTenantId,
    activeTenant,
    hasTenants,
    isTenantAdmin,
    loaded,
    ready,
    load,
    setActiveTenant,
    switchTenant,
    reset,
  }
})

function readStoredTenantId(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY)
  } catch {
    return null
  }
}
