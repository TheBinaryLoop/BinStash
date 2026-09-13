<script setup lang="ts">
import { ArrowRight, Plus } from '@lucide/vue'
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'

import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { formatDateOnly } from '@/lib/format'
import { useTenantStore } from '@/stores/tenant'

const tenants = useTenantStore()
const router = useRouter()

onMounted(async () => {
  await tenants.ready()
  // Nothing to choose between — go straight where the user was heading.
  if (tenants.tenants.length === 1) void open(tenants.tenants[0]!.id)
})

async function open(tenantId: string) {
  await tenants.switchTenant(tenantId)
  await router.push({ name: 'tenant-home', params: { tenantId } })
}
</script>

<template>
  <div class="space-y-6">
    <div class="space-y-1.5">
      <h1 class="text-xl font-semibold tracking-tight">Choose a workspace</h1>
      <p class="text-muted-foreground text-sm">
        You have access to {{ tenants.tenants.length }} workspace{{
          tenants.tenants.length === 1 ? '' : 's'
        }}.
      </p>
    </div>

    <div v-if="!tenants.loaded" class="space-y-2">
      <Skeleton v-for="row in 3" :key="row" class="h-16 w-full" />
    </div>

    <ul v-else class="space-y-2">
      <li v-for="tenant in tenants.tenants" :key="tenant.id">
        <button
          type="button"
          class="bg-card hairline hover:border-primary/40 group flex w-full items-center gap-3 rounded-lg px-3 py-3 text-left transition-colors"
          @click="open(tenant.id)"
        >
          <span
            class="bg-primary/15 text-primary flex size-9 shrink-0 items-center justify-center rounded-md text-sm font-semibold"
          >
            {{ tenant.name.slice(0, 2).toUpperCase() }}
          </span>
          <span class="min-w-0 flex-1">
            <span class="block truncate text-sm font-medium">{{ tenant.name }}</span>
            <span class="text-muted-foreground block truncate font-mono text-xs">
              {{ tenant.slug }}
              <template v-if="tenant.joinedAt">· joined {{ formatDateOnly(tenant.joinedAt) }}</template>
            </span>
          </span>
          <ArrowRight
            class="text-muted-foreground group-hover:text-foreground size-4 shrink-0 transition-colors"
          />
        </button>
      </li>
    </ul>

    <Button variant="outline" class="w-full gap-2" @click="router.push({ name: 'create-tenant' })">
      <Plus class="size-4" />
      Create a workspace
    </Button>
  </div>
</template>
