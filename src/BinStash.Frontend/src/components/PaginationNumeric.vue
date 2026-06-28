<template>
  <div class="flex justify-center" v-if="pageCount > 1">
    <nav class="flex" role="navigation" aria-label="Pagination">
      <!-- Previous -->
      <div class="mr-2">
        <button
          type="button"
          :disabled="currentPage <= 1"
          @click="goTo(currentPage - 1)"
          :class="prevNextClass(currentPage <= 1, true)"
        >
          <span class="sr-only">Previous</span><wbr />
          <svg class="fill-current" width="16" height="16" viewBox="0 0 16 16">
            <path d="M9.4 13.4l1.4-1.4-4-4 4-4-1.4-1.4L4 8z" />
          </svg>
        </button>
      </div>

      <!-- Pages -->
      <ul class="inline-flex text-sm font-medium -space-x-px rounded-lg shadow-xs">
        <li v-for="p in displayPages" :key="String(p) + '-' + currentPage">
          <span
            v-if="p === '…'"
            class="inline-flex items-center justify-center leading-5 px-3.5 py-2 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700/60 text-gray-400 dark:text-gray-500"
          >…</span>

          <button
            v-else
            type="button"
            @click="goTo(p)"
            :class="pageClass(p === currentPage, p === 1, p === pageCount)"
          >
            {{ p }}
          </button>
        </li>
      </ul>

      <!-- Next -->
      <div class="ml-2">
        <button
          type="button"
          :disabled="currentPage >= pageCount"
          @click="goTo(currentPage + 1)"
          :class="prevNextClass(currentPage >= pageCount, false)"
        >
          <span class="sr-only">Next</span><wbr />
          <svg class="fill-current" width="16" height="16" viewBox="0 0 16 16">
            <path d="M6.6 13.4L5.2 12l4-4-4-4 1.4-1.4L12 8z" />
          </svg>
        </button>
      </div>
    </nav>
  </div>
</template>

<script>
export default {
  name: 'PaginationNumeric',
  props: {
    modelValue: { type: Number, default: 1 },      // current page (1-based)
    totalItems: { type: Number, default: 0 },
    pageSize: { type: Number, default: 12 },
    maxButtons: { type: Number, default: 7 },      // total numeric slots excluding prev/next
  },
  emits: ['update:modelValue', 'change'],
  computed: {
    pageCount() {
      const size = Math.max(1, this.pageSize)
      return Math.max(1, Math.ceil(this.totalItems / size))
    },
    currentPage() {
      // clamp
      const p = Number.isFinite(this.modelValue) ? this.modelValue : 1
      return Math.min(Math.max(1, p), this.pageCount)
    },
    displayPages() {
      // Returns an array like [1, 2, 3, '…', 9]
      const total = this.pageCount
      const max = Math.max(5, this.maxButtons) // ensure it’s not too tiny
      const current = this.currentPage

      if (total <= max) {
        return Array.from({ length: total }, (_, i) => i + 1)
      }

      // Always show first and last; show a window around current
      const windowSize = max - 2 // excluding first/last
      const half = Math.floor(windowSize / 2)

      let start = current - half
      let end = current + half

      if (start < 2) {
        start = 2
        end = start + windowSize - 1
      }
      if (end > total - 1) {
        end = total - 1
        start = end - windowSize + 1
      }

      const pages = [1]
      if (start > 2) pages.push('…')

      for (let p = start; p <= end; p++) pages.push(p)

      if (end < total - 1) pages.push('…')
      pages.push(total)

      return pages
    },
  },
  methods: {
    goTo(page) {
      const p = Math.min(Math.max(1, page), this.pageCount)
      if (p === this.currentPage) return
      this.$emit('update:modelValue', p)
      this.$emit('change', p)
    },
    pageClass(isActive, isFirst, isLast) {
      const base =
        'inline-flex items-center justify-center leading-5 px-3.5 py-2 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700/60'
      const hover =
        'hover:bg-gray-50 dark:hover:bg-gray-900'
      const inactiveText =
        'text-gray-600 dark:text-gray-300'
      const activeText =
        'text-violet-500'

      const rounding = isFirst ? ' rounded-l-lg' : isLast ? ' rounded-r-lg' : ''
      if (isActive) return `${base}${rounding} ${activeText}`
      return `${base}${rounding} ${hover} ${inactiveText}`
    },
    prevNextClass(disabled, isPrev) {
      const base =
        'inline-flex items-center justify-center rounded-lg leading-5 px-2.5 py-2 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700/60 shadow-xs'
      const enabled =
        'hover:bg-gray-50 dark:hover:bg-gray-900 text-violet-500'
      const disabledCls =
        'text-gray-300 dark:text-gray-600 cursor-not-allowed'
      return disabled ? `${base} ${disabledCls}` : `${base} ${enabled}`
    },
  },
}
</script>
