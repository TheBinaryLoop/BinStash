<script setup lang="ts">
import { ArrowLeft, Building2, Database, Gauge, ScrollText, Settings, Users } from '@lucide/vue'
import { computed, ref } from 'vue'

import AppLogo from '@/components/app/AppLogo.vue'
import AppSidebar from '@/components/app/AppSidebar.vue'
import ThemeToggle from '@/components/app/ThemeToggle.vue'
import UserMenu from '@/components/app/UserMenu.vue'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import { Menu } from '@lucide/vue'
import { useSidebar } from '@/composables/useSidebar'
import { useTenantStore } from '@/stores/tenant'

const tenants = useTenantStore()
const mobileNavOpen = ref(false)
const { widthStyle } = useSidebar()

const groups = computed(() => [
  {
    label: 'Instance',
    items: [
      { label: 'Overview', icon: Gauge, to: { name: 'instance-home' }, exact: true },
      { label: 'Tenants', icon: Building2, to: { name: 'instance-tenants' } },
      { label: 'Users', icon: Users, to: { name: 'instance-users' } },
    ],
  },
  {
    label: 'Storage',
    items: [{ label: 'Chunk stores', icon: Database, to: { name: 'chunk-stores' } }],
  },
  {
    label: 'Administration',
    items: [
      { label: 'Audit log', icon: ScrollText, to: { name: 'instance-audit' } },
      { label: 'Settings', icon: Settings, to: { name: 'instance-settings' } },
    ],
  },
])

const backToWorkspace = computed(() => ({
  name: tenants.activeTenantId ? 'tenant-home' : 'select-tenant',
  params: tenants.activeTenantId ? { tenantId: tenants.activeTenantId } : {},
}))
</script>

<template>
  <div class="bg-background min-h-dvh" :style="widthStyle">
    <aside
      class="border-hairline fixed inset-y-0 left-0 z-30 hidden w-(--sidebar-width) border-r lg:block"
    >
      <AppSidebar :groups="groups">
        <template #header="{ collapsed }">
          <div class="flex w-full items-center gap-2.5" :class="collapsed ? 'justify-center' : 'px-2'">
            <AppLogo :size="24" />
            <div v-if="!collapsed" class="min-w-0">
              <p class="truncate text-sm font-semibold">Instance</p>
              <p class="text-muted-foreground truncate text-xs">Administration</p>
            </div>
          </div>
        </template>
        <template #footer>
          <RouterLink
            :to="backToWorkspace"
            class="text-muted-foreground hover:text-foreground flex items-center gap-2 rounded-md px-2 py-1.5 text-sm"
          >
            <ArrowLeft class="size-4 shrink-0" />
            <span class="truncate">Back to workspace</span>
          </RouterLink>
        </template>
      </AppSidebar>
    </aside>

    <div class="lg:pl-(--sidebar-width)">
      <header
        class="border-hairline bg-background/85 sticky top-0 z-20 flex h-(--header-height) items-center gap-2 border-b px-4 backdrop-blur-md"
      >
        <Sheet v-model:open="mobileNavOpen">
          <SheetTrigger as-child>
            <Button variant="ghost" size="icon" class="lg:hidden" aria-label="Open navigation">
              <Menu class="size-4" />
            </Button>
          </SheetTrigger>
          <SheetContent side="left" class="w-(--sidebar-width) p-0">
            <SheetTitle class="sr-only">Navigation</SheetTitle>
            <AppSidebar :groups="groups" :collapsible="false" @click="mobileNavOpen = false" />
          </SheetContent>
        </Sheet>

        <div class="flex-1" />
        <ThemeToggle />
        <UserMenu />
      </header>

      <main class="mx-auto w-full max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
        <RouterView />
      </main>
    </div>
  </div>
</template>
