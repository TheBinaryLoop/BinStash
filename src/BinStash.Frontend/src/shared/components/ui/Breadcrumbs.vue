<template>
  <nav class="flex items-center gap-1.5 text-sm text-ink-muted" aria-label="Breadcrumb">
    <template v-for="(item, i) in items" :key="i">
      <RouterLink
        v-if="item.to && i < items.length - 1"
        :to="item.to"
        class="truncate transition hover:text-ink-strong"
      >
        {{ item.label }}
      </RouterLink>
      <span v-else class="truncate font-medium text-ink-strong">{{ item.label }}</span>
      <IconChevronRight v-if="i < items.length - 1" class="h-4 w-4 shrink-0 text-ink-subtle" />
    </template>
  </nav>
</template>

<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { IconChevronRight } from '@tabler/icons-vue'

export interface Crumb {
  label: string
  to?: string | object
}

defineProps<{ items: Crumb[] }>()
</script>
