<script setup lang="ts">
import { Check, ChevronsUpDown, Plus } from '@lucide/vue'
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useTenantStore } from '@/stores/tenant'

const tenants = useTenantStore()
const router = useRouter()

const active = computed(() => tenants.activeTenant)

async function select(tenantId: string) {
  if (tenantId === tenants.activeTenantId) return
  await tenants.switchTenant(tenantId)
  // Land on the workspace root: the current route's ids belong to the previous tenant.
  await router.push({ name: 'tenant-home', params: { tenantId } })
}
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button
        variant="ghost"
        class="hover:bg-accent h-auto w-full justify-between gap-2 px-2 py-2 text-left"
      >
        <span class="flex min-w-0 items-center gap-2.5">
          <span
            class="bg-primary/15 text-primary flex size-7 shrink-0 items-center justify-center rounded-md text-xs font-semibold"
          >
            {{ (active?.name ?? '?').slice(0, 2).toUpperCase() }}
          </span>
          <span class="min-w-0">
            <span class="block truncate text-sm font-medium">{{ active?.name ?? 'Select workspace' }}</span>
            <span v-if="active" class="text-muted-foreground block truncate font-mono text-xs">
              {{ active.slug }}
            </span>
          </span>
        </span>
        <ChevronsUpDown class="text-muted-foreground size-4 shrink-0" />
      </Button>
    </DropdownMenuTrigger>

    <DropdownMenuContent align="start" class="w-[15rem]">
      <DropdownMenuLabel class="text-muted-foreground text-xs">Workspaces</DropdownMenuLabel>
      <DropdownMenuItem
        v-for="tenant in tenants.tenants"
        :key="tenant.id"
        class="gap-2"
        @select="select(tenant.id)"
      >
        <span class="min-w-0 flex-1 truncate">{{ tenant.name }}</span>
        <Check v-if="tenant.id === tenants.activeTenantId" class="text-primary size-4 shrink-0" />
      </DropdownMenuItem>

      <DropdownMenuSeparator />
      <DropdownMenuItem class="gap-2" @select="router.push({ name: 'create-tenant' })">
        <Plus class="size-4" />
        <span>New workspace</span>
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
