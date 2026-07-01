<template>
  <BaseCard :padded="false">
    <template #header>
      <h2 class="font-semibold text-ink-strong">Tenant Profile</h2>
    </template>

    <div class="p-5">
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div>
          <p class="mb-1 text-xs font-medium uppercase text-ink-subtle">Tenant Name</p>
          <p class="text-sm font-medium text-ink-strong">{{ tenant?.name ?? '—' }}</p>
        </div>

        <div>
          <p class="mb-1 text-xs font-medium uppercase text-ink-subtle">Slug</p>
          <p class="font-mono text-sm text-ink-muted">{{ tenant?.slug ?? '—' }}</p>
        </div>

        <div>
          <p class="mb-1 text-xs font-medium uppercase text-ink-subtle">Tenant ID</p>
          <p class="truncate font-mono text-xs text-ink-muted">{{ tenantId }}</p>
        </div>

        <div>
          <p class="mb-1 text-xs font-medium uppercase text-ink-subtle">Your Role</p>
          <div class="flex flex-wrap gap-1">
            <BaseBadge v-for="r in userRoles" :key="r" tone="accent">{{ r }}</BaseBadge>
            <span v-if="userRoles.length === 0" class="text-xs text-ink-subtle">—</span>
          </div>
        </div>
      </div>
    </div>
  </BaseCard>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'
import { BaseCard, BaseBadge } from '@/shared/components/ui'

const route = useRoute()
const auth = useAuthStore()
const tenantStore = useTenantStore()

const tenantId = computed(() => route.params.tenantId as string)
const tenant = computed(() => tenantStore.currentTenant)
const userRoles = computed(() =>
  (auth.user?.roles ?? []).filter(r => r === 'TenantAdmin' || r === 'TenantMember' || r === 'InstanceAdmin'),
)
</script>
