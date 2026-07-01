<template>
  <div>
    <!-- Header -->
    <div class="mb-6 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div class="min-w-0">
        <h1 class="text-xl font-semibold tracking-tight text-ink-strong sm:text-2xl">{{ title }}</h1>
        <div v-if="subtitle" class="mt-1 text-sm text-ink-muted">{{ subtitle }}</div>
      </div>

      <div class="flex shrink-0 items-center gap-2">
        <div class="w-full sm:w-64">
          <BaseInput v-model="search" :placeholder="searchPlaceholder" :prefix-icon="IconSearch" />
        </div>
        <BaseButton v-if="primaryAction" :icon="IconPlus" @click="primaryAction.onClick">
          {{ primaryAction.label }}
        </BaseButton>
      </div>
    </div>

    <!-- Error -->
    <div v-if="error" class="mb-6 rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
      {{ error }}
    </div>

    <!-- Loading -->
    <div v-if="isLoading" class="flex items-center justify-center py-16">
      <Spinner :size="28" color="var(--color-accent)" :label="loadingText" />
    </div>

    <!-- Empty -->
    <BaseCard v-else-if="filteredItems.length === 0">
      <EmptyState :title="emptyTitle" :description="emptyText">
        <BaseButton v-if="primaryAction" :icon="IconPlus" @click="primaryAction.onClick">
          {{ emptyActionLabel || primaryAction.label }}
        </BaseButton>
      </EmptyState>
    </BaseCard>

    <!-- Grid -->
    <div v-else>
      <div :class="gridClass">
        <slot name="card" v-for="item in pagedItems" :key="getKey(item)" :item="item" />
      </div>

      <div class="mt-6">
        <BasePagination v-model="page" :total-items="filteredItems.length" :page-size="pageSize" />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, watch, type PropType } from 'vue'
import { IconSearch, IconPlus } from '@tabler/icons-vue'
import { BaseInput, BaseButton, BaseCard, EmptyState, BasePagination } from '@/shared/components/ui'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { useListFilteringPaging, type ListFilterFn } from '../../composables/useListFilteringPaging'

type PrimaryAction = { label: string; onClick: () => void }

const props = defineProps({
  title: { type: String, required: true },
  subtitle: { type: String, default: '' },

  items: { type: Array as PropType<any[]>, required: true },
  filterFn: { type: Function as PropType<ListFilterFn<any>>, required: true },
  getKey: { type: Function as PropType<(item: any) => string>, required: true },

  isLoading: { type: Boolean, default: false },
  error: { type: String, default: '' },

  searchPlaceholder: { type: String, default: 'Search…' },
  loadingText: { type: String, default: 'Loading…' },

  emptyTitle: { type: String, default: 'No results' },
  emptyText: { type: String, default: 'Try adjusting your search or create a new item.' },
  emptyActionLabel: { type: String, default: '' },

  pageSize: { type: Number, default: 12 },
  gridClass: { type: String, default: 'grid grid-cols-12 gap-5' },

  primaryAction: { type: Object as PropType<PrimaryAction | null>, default: null },
})

const itemsRef = computed(() => props.items)

const { search, page, pageSize, filteredItems, pagedItems } = useListFilteringPaging(
  itemsRef as any,
  props.filterFn as any,
  { initialPageSize: props.pageSize },
)

watch(() => props.pageSize, (v) => { pageSize.value = v })

defineExpose({ search, page, pageSize, filteredItems, pagedItems })
</script>
