<template>
  <div class="overflow-x-auto">
    <table class="min-w-full">
      <thead>
        <tr class="border-b border-hairline">
          <th
            v-for="col in columns"
            :key="col.key"
            scope="col"
            class="whitespace-nowrap px-4 py-3 text-xs font-semibold uppercase tracking-wide text-ink-subtle"
            :class="alignClass(col.align)"
            :style="col.width ? { width: col.width } : undefined"
          >
            {{ col.label }}
          </th>
        </tr>
      </thead>
      <tbody class="divide-y divide-hairline">
        <template v-if="loading">
          <tr v-for="r in skeletonRows" :key="`sk-${r}`">
            <td v-for="col in columns" :key="col.key" class="px-4 py-3.5">
              <Skeleton height="0.875rem" :width="r % 2 ? '70%' : '50%'" />
            </td>
          </tr>
        </template>
        <template v-else-if="items.length">
          <tr
            v-for="(item, index) in items"
            :key="rowId(item, index)"
            class="transition"
            :class="[hover ? 'hover:bg-raised' : '', onRowClick ? 'cursor-pointer' : '']"
            @click="onRowClick?.(item, index)"
          >
            <td
              v-for="col in columns"
              :key="col.key"
              class="px-4 text-sm text-ink-muted"
              :class="[dense ? 'py-2.5' : 'py-3.5', alignClass(col.align), col.class]"
            >
              <slot :name="`cell-${col.key}`" :item="item" :value="getValue(item, col.key)" :index="index">
                {{ getValue(item, col.key) }}
              </slot>
            </td>
          </tr>
        </template>
      </tbody>
    </table>

    <div v-if="!loading && !items.length">
      <slot name="empty">
        <p class="px-4 py-10 text-center text-sm text-ink-subtle">{{ empty }}</p>
      </slot>
    </div>
  </div>
</template>

<script setup lang="ts">
import Skeleton from './Skeleton.vue'

export interface Column {
  key: string
  label: string
  align?: 'left' | 'center' | 'right'
  width?: string
  class?: string
}

const props = withDefaults(defineProps<{
  columns: Column[]
  items: any[]
  loading?: boolean
  empty?: string
  hover?: boolean
  dense?: boolean
  rowKey?: string | ((item: any, index: number) => string | number)
  onRowClick?: (item: any, index: number) => void
  skeletonRows?: number
}>(), {
  loading: false,
  empty: 'No data.',
  hover: true,
  dense: false,
  skeletonRows: 5,
})

function alignClass(align?: string) {
  return align === 'right' ? 'text-right' : align === 'center' ? 'text-center' : 'text-left'
}

function getValue(item: any, key: string) {
  return key.split('.').reduce((acc, k) => acc?.[k], item)
}

function rowId(item: any, index: number) {
  if (typeof props.rowKey === 'function') return props.rowKey(item, index)
  if (props.rowKey) return getValue(item, props.rowKey) ?? index
  return item.id ?? index
}
</script>
