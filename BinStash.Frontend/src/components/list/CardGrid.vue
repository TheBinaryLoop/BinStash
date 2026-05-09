<template>
  <div>
    <!-- Header -->
    <div class="sm:flex sm:justify-between sm:items-center mb-8">
      <div class="mb-4 sm:mb-0">
        <h1 class="text-2xl md:text-3xl text-gray-800 dark:text-gray-100 font-bold">{{ title }}</h1>
        <div v-if="subtitle" class="text-sm text-gray-500 dark:text-gray-400 mt-1">{{ subtitle }}</div>
      </div>

      <div class="grid grid-flow-col sm:auto-cols-max justify-start sm:justify-end gap-2">
        <SearchForm v-model="search" :placeholder="searchPlaceholder" />
        <button
          v-if="primaryAction"
          class="btn bg-gray-900 text-gray-100 hover:bg-gray-800 dark:bg-gray-100 dark:text-gray-800 dark:hover:bg-white"
          type="button"
          @click="primaryAction.onClick"
        >
          <svg class="fill-current shrink-0 xs:hidden" width="16" height="16" viewBox="0 0 16 16">
            <path
              d="M15 7H9V1c0-.6-.4-1-1-1S7 .4 7 1v6H1c-.6 0-1 .4-1 1s.4 1 1 1h6v6c0 .6.4 1 1 1s1-.4 1-1V9h6c.6 0 1-.4 1-1s-.4-1-1-1z"
            />
          </svg>
          <span class="max-xs:sr-only">{{ primaryAction.label }}</span>
        </button>
      </div>
    </div>

    <!-- Banners -->
    <div v-if="error" class="mb-6">
      <div class="bg-rose-500/20 text-rose-700 dark:text-rose-200 px-3 py-2 rounded-lg">
        <span class="text-sm">{{ error }}</span>
      </div>
    </div>

    <div v-if="isLoading" class="mb-6">
      <div class="bg-slate-500/20 text-slate-700 dark:text-slate-200 px-3 py-2 rounded-lg">
        <span class="text-sm">{{ loadingText }}</span>
      </div>
    </div>

    <!-- Empty -->
    <div v-if="!isLoading && filteredItems.length === 0" class="mt-8">
      <div class="bg-white dark:bg-gray-800 shadow-sm rounded-xl p-6 border border-gray-200 dark:border-gray-700/60">
        <div class="text-gray-800 dark:text-gray-100 font-semibold mb-1">{{ emptyTitle }}</div>
        <div class="text-sm text-gray-500 dark:text-gray-400">{{ emptyText }}</div>
        <div v-if="primaryAction" class="mt-4">
          <button
            class="btn bg-gray-900 text-gray-100 hover:bg-gray-800 dark:bg-gray-100 dark:text-gray-800 dark:hover:bg-white"
            type="button"
            @click="primaryAction.onClick"
          >
            {{ emptyActionLabel || primaryAction.label }}
          </button>
        </div>
      </div>
    </div>

    <!-- Grid -->
    <div v-else>
      <div :class="gridClass">
        <slot
          name="card"
          v-for="item in pagedItems"
          :key="getKey(item)"
          :item="item"
        />
      </div>

      <div class="mt-8">
        <PaginationNumeric v-model="page" :total-items="filteredItems.length" :page-size="pageSize" />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, watch, type PropType } from 'vue'
import SearchForm from '../SearchForm.vue'
import PaginationNumeric from '../PaginationNumeric.vue'
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
  gridClass: { type: String, default: 'grid grid-cols-12 gap-6' },

  primaryAction: { type: Object as PropType<PrimaryAction | null>, default: null },
})

const itemsRef = computed(() => props.items)

const { search, page, pageSize, filteredItems, pagedItems } = useListFilteringPaging(
  itemsRef as any,
  props.filterFn as any,
  { initialPageSize: props.pageSize }
)

watch(() => props.pageSize, (v) => { pageSize.value = v })

defineExpose({ search, page, pageSize, filteredItems, pagedItems })
</script>
