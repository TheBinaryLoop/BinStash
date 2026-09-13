<script setup lang="ts">
import { PanelLeftClose, PanelLeftOpen } from '@lucide/vue'
import type { Component } from 'vue'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { RouteLocationRaw } from 'vue-router'

import { Button } from '@/components/ui/button'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useSidebar } from '@/composables/useSidebar'

export interface NavItem {
  label: string
  icon: Component
  to: RouteLocationRaw
  /**
   * Index route whose path prefixes its siblings — without this the workspace root
   * would light up on every page inside the workspace.
   */
  exact?: boolean
}

const props = withDefaults(
  defineProps<{
    groups: Array<{ label: string; items: NavItem[] }>
    /** The mobile sheet is always full width; only the fixed rail collapses. */
    collapsible?: boolean
  }>(),
  { collapsible: true },
)

const route = useRoute()
const router = useRouter()
const { collapsed: rawCollapsed, toggle } = useSidebar()

const collapsed = computed(() => props.collapsible && rawCollapsed.value)

/**
 * Active state is derived from the resolved path rather than RouterLink's own matching.
 * Detail pages are declared as siblings of their list (`repositories` and
 * `repositories/:repoId`), not as nested children, so RouterLink sees unrelated route
 * records and reports the list link as inactive while you are looking at a repository.
 */
const decorated = computed(() =>
  props.groups.map((group) => ({
    ...group,
    items: group.items.map((item) => {
      const href = router.resolve(item.to).path
      const isActive = item.exact
        ? route.path === href
        : route.path === href || route.path.startsWith(`${href}/`)
      return { ...item, href, isActive }
    }),
  })),
)
</script>

<template>
  <nav class="flex h-full flex-col">
    <div
      class="border-hairline flex h-(--header-height) items-center border-b"
      :class="collapsed ? 'justify-center px-1' : 'px-2'"
    >
      <slot name="header" :collapsed="collapsed" />
    </div>

    <ScrollArea class="flex-1">
      <div class="space-y-5 p-2">
        <div v-for="group in decorated" :key="group.label" class="space-y-1">
          <p
            v-if="!collapsed"
            class="text-muted-foreground px-2 py-1 text-[0.6875rem] font-medium tracking-wider uppercase"
          >
            {{ group.label }}
          </p>
          <!-- Collapsed: a rule stands in for the group heading so the grouping survives. -->
          <div v-else class="bg-hairline mx-auto my-2 h-px w-5" aria-hidden="true" />

          <Tooltip v-for="item in group.items" :key="item.label" :disabled="!collapsed">
            <TooltipTrigger as-child>
              <RouterLink
                :to="item.to"
                :aria-current="item.isActive ? 'page' : undefined"
                class="group relative flex items-center rounded-md text-sm transition-colors"
                :class="[
                  collapsed ? 'justify-center px-0 py-2' : 'gap-2.5 px-2 py-1.5',
                  item.isActive
                    ? 'bg-accent text-accent-foreground font-medium'
                    : 'text-muted-foreground hover:bg-accent/60 hover:text-foreground',
                ]"
              >
                <!-- Active rail: reads at a glance without spending colour on the label. -->
                <span
                  v-if="item.isActive"
                  class="bg-primary absolute inset-y-1.5 -left-2 w-0.5 rounded-full"
                  aria-hidden="true"
                />
                <component :is="item.icon" class="size-4 shrink-0" />
                <span v-if="!collapsed" class="truncate">{{ item.label }}</span>
                <span v-else class="sr-only">{{ item.label }}</span>
              </RouterLink>
            </TooltipTrigger>
            <TooltipContent side="right">{{ item.label }}</TooltipContent>
          </Tooltip>
        </div>
      </div>
    </ScrollArea>

    <div class="border-hairline flex items-center gap-2 border-t p-2">
      <template v-if="!collapsed">
        <div class="min-w-0 flex-1"><slot name="footer" /></div>
      </template>

      <Button
        v-if="collapsible"
        variant="ghost"
        size="icon"
        class="shrink-0"
        :class="collapsed ? 'mx-auto' : ''"
        :aria-label="collapsed ? 'Expand sidebar' : 'Collapse sidebar'"
        @click="toggle"
      >
        <PanelLeftOpen v-if="collapsed" class="size-4" />
        <PanelLeftClose v-else class="size-4" />
      </Button>
    </div>
  </nav>
</template>
