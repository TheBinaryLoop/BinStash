<script setup lang="ts">
import type { Component } from 'vue'

import { ScrollArea } from '@/components/ui/scroll-area'

defineProps<{
  groups: Array<{
    label: string
    /**
     * `exact` marks an index route whose path is a prefix of its siblings — without it
     * RouterLink reports the parent as active on every child page too.
     */
    items: Array<{ label: string; icon: Component; to: Record<string, unknown>; exact?: boolean }>
  }>
}>()
</script>

<template>
  <nav class="flex h-full flex-col">
    <div class="border-hairline border-b p-2">
      <slot name="header" />
    </div>

    <ScrollArea class="flex-1">
      <div class="space-y-5 p-2">
        <div v-for="group in groups" :key="group.label" class="space-y-1">
          <p class="text-muted-foreground px-2 py-1 text-[0.6875rem] font-medium tracking-wider uppercase">
            {{ group.label }}
          </p>
          <RouterLink
            v-for="item in group.items"
            :key="item.label"
            v-slot="{ isActive, isExactActive, href, navigate }"
            :to="item.to"
            custom
          >
            <a
              :href="href"
              :aria-current="(item.exact ? isExactActive : isActive) ? 'page' : undefined"
              class="group relative flex items-center gap-2.5 rounded-md px-2 py-1.5 text-sm transition-colors"
              :class="
                (item.exact ? isExactActive : isActive)
                  ? 'bg-accent text-accent-foreground font-medium'
                  : 'text-muted-foreground hover:bg-accent/60 hover:text-foreground'
              "
              @click="navigate"
            >
              <!-- Active rail: reads at a glance without spending colour on the label. -->
              <span
                v-if="item.exact ? isExactActive : isActive"
                class="bg-primary absolute inset-y-1.5 -left-2 w-0.5 rounded-full"
                aria-hidden="true"
              />
              <component :is="item.icon" class="size-4 shrink-0" />
              <span class="truncate">{{ item.label }}</span>
            </a>
          </RouterLink>
        </div>
      </div>
    </ScrollArea>

    <div class="border-hairline border-t p-2">
      <slot name="footer" />
    </div>
  </nav>
</template>
