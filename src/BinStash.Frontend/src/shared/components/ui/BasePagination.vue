<template>
  <div v-if="totalPages > 1" class="flex items-center justify-between gap-3">
    <p class="text-xs text-ink-muted">
      Showing <span class="font-medium text-ink-strong">{{ rangeStart }}</span>–<span class="font-medium text-ink-strong">{{ rangeEnd }}</span>
      of <span class="font-medium text-ink-strong">{{ totalItems }}</span>
    </p>
    <div class="flex items-center gap-1">
      <button
        type="button"
        class="flex h-8 w-8 items-center justify-center rounded-control border border-hairline text-ink-muted transition hover:bg-raised disabled:opacity-40 disabled:pointer-events-none"
        :disabled="page <= 1"
        @click="go(page - 1)"
      >
        <IconChevronLeft class="h-4 w-4" />
      </button>
      <button
        v-for="p in pages"
        :key="p"
        type="button"
        class="flex h-8 min-w-8 items-center justify-center rounded-control px-2 text-sm font-medium transition"
        :class="p === page ? 'bg-accent text-white' : 'border border-hairline text-ink-muted hover:bg-raised'"
        @click="go(p)"
      >{{ p }}</button>
      <button
        type="button"
        class="flex h-8 w-8 items-center justify-center rounded-control border border-hairline text-ink-muted transition hover:bg-raised disabled:opacity-40 disabled:pointer-events-none"
        :disabled="page >= totalPages"
        @click="go(page + 1)"
      >
        <IconChevronRight class="h-4 w-4" />
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { IconChevronLeft, IconChevronRight } from '@tabler/icons-vue'

const page = defineModel<number>({ default: 1 })

const props = withDefaults(defineProps<{
  totalItems: number
  pageSize?: number
  maxButtons?: number
}>(), {
  pageSize: 10,
  maxButtons: 7,
})

const totalPages = computed(() => Math.max(1, Math.ceil(props.totalItems / props.pageSize)))
const rangeStart = computed(() => (props.totalItems === 0 ? 0 : (page.value - 1) * props.pageSize + 1))
const rangeEnd = computed(() => Math.min(props.totalItems, page.value * props.pageSize))

const pages = computed(() => {
  const total = totalPages.value
  const max = props.maxButtons
  if (total <= max) return Array.from({ length: total }, (_, i) => i + 1)
  const half = Math.floor(max / 2)
  let start = Math.max(1, page.value - half)
  const end = Math.min(total, start + max - 1)
  start = Math.max(1, end - max + 1)
  return Array.from({ length: end - start + 1 }, (_, i) => start + i)
})

function go(p: number) {
  if (p < 1 || p > totalPages.value) return
  page.value = p
}
</script>
