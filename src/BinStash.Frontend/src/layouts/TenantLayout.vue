<script setup lang="ts">
import {
  Boxes,
  FolderGit2,
  Gauge,
  KeyRound,
  Menu,
  ScrollText,
  Search,
  Settings,
  Users,
} from '@lucide/vue'
import { computed, ref } from 'vue'

import AppSidebar from '@/components/app/AppSidebar.vue'
import CommandPalette from '@/components/app/CommandPalette.vue'
import TenantSwitcher from '@/components/app/TenantSwitcher.vue'
import ThemeToggle from '@/components/app/ThemeToggle.vue'
import UserMenu from '@/components/app/UserMenu.vue'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import { useCommandPalette } from '@/composables/useCommandPalette'
import { useTenantStore } from '@/stores/tenant'

const tenants = useTenantStore()
const mobileNavOpen = ref(false)
const { show: showPalette } = useCommandPalette()

const groups = computed(() => {
  const params = { tenantId: tenants.activeTenantId }

  return [
    {
      label: 'Workspace',
      items: [
        { label: 'Overview', icon: Gauge, to: { name: 'tenant-home', params } },
        { label: 'Repositories', icon: FolderGit2, to: { name: 'repositories', params } },
        { label: 'Usage', icon: Boxes, to: { name: 'usage', params } },
      ],
    },
    {
      label: 'Administration',
      items: [
        { label: 'Members', icon: Users, to: { name: 'members', params } },
        { label: 'Service accounts', icon: KeyRound, to: { name: 'service-accounts', params } },
        { label: 'Audit log', icon: ScrollText, to: { name: 'audit', params } },
        { label: 'Settings', icon: Settings, to: { name: 'tenant-settings', params } },
      ],
    },
  ]
})
</script>

<template>
  <div class="bg-background min-h-dvh">
    <aside
      class="border-hairline fixed inset-y-0 left-0 z-30 hidden w-(--sidebar-width) border-r lg:block"
    >
      <AppSidebar :groups="groups">
        <template #header><TenantSwitcher /></template>
        <template #footer>
          <p class="text-muted-foreground px-2 py-1 font-mono text-[0.6875rem]">BinStash</p>
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
            <AppSidebar :groups="groups" @click="mobileNavOpen = false">
              <template #header><TenantSwitcher /></template>
            </AppSidebar>
          </SheetContent>
        </Sheet>

        <div class="flex-1" />

        <!-- The palette owns ⌘K; this button is the discoverable affordance for it. -->
        <Button
          variant="outline"
          size="sm"
          class="text-muted-foreground hidden gap-2 font-normal sm:flex"
          @click="showPalette()"
        >
          <Search class="size-3.5" />
          <span class="text-xs">Search</span>
          <kbd class="bg-muted rounded px-1.5 py-0.5 font-mono text-[0.625rem]">⌘K</kbd>
        </Button>

        <ThemeToggle />
        <UserMenu />
      </header>

      <main class="mx-auto w-full max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
        <RouterView />
      </main>
    </div>

    <CommandPalette />
  </div>
</template>
