<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'

import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb'

/**
 * Detail pages are siblings of their list route rather than nested children, so there is
 * no route hierarchy to derive a trail from — each page states its own.
 */
defineProps<{
  items: Array<{ label: string; to?: RouteLocationRaw }>
}>()
</script>

<template>
  <Breadcrumb>
    <BreadcrumbList>
      <template v-for="(item, index) in items" :key="index">
        <BreadcrumbItem>
          <BreadcrumbLink v-if="item.to" as-child>
            <RouterLink :to="item.to" class="hover:text-foreground transition-colors">
              {{ item.label }}
            </RouterLink>
          </BreadcrumbLink>
          <BreadcrumbPage v-else class="max-w-[16rem] truncate">{{ item.label }}</BreadcrumbPage>
        </BreadcrumbItem>
        <BreadcrumbSeparator v-if="index < items.length - 1" />
      </template>
    </BreadcrumbList>
  </Breadcrumb>
</template>
