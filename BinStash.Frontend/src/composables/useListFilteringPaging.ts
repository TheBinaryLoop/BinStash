import { computed, ref, watch, type Ref } from 'vue'

export type ListFilterFn<T> = (item: T, query: string) => boolean

export type UseListFilteringPagingOptions = {
  initialSearch?: string
  initialPage?: number
  initialPageSize?: number
  resetPageOnSearchChange?: boolean
}

export function useListFilteringPaging<T>(
  allItems: Ref<T[]>,
  filterFn: ListFilterFn<T>,
  options?: UseListFilteringPagingOptions
) {
  const search = ref(options?.initialSearch ?? '')
  const page = ref(options?.initialPage ?? 1)
  const pageSize = ref(options?.initialPageSize ?? 12)

  const normalizedQuery = computed(() => search.value.trim().toLowerCase())

  const filteredItems = computed(() => {
    const q = normalizedQuery.value
    if (!q) return allItems.value
    return allItems.value.filter((x) => filterFn(x, q))
  })

  const pageCount = computed(() => {
    const size = Math.max(1, pageSize.value)
    return Math.max(1, Math.ceil(filteredItems.value.length / size))
  })

  const pagedItems = computed(() => {
    const size = Math.max(1, pageSize.value)
    const p = Math.min(Math.max(1, page.value), pageCount.value)
    const start = (p - 1) * size
    return filteredItems.value.slice(start, start + size)
  })

  function setPage(p: number) {
    page.value = Math.min(Math.max(1, p), pageCount.value)
  }

  // reset to page 1 when search changes (optional)
  watch(
    () => normalizedQuery.value,
    () => {
      if (options?.resetPageOnSearchChange ?? true) page.value = 1
    }
  )

  // clamp page when list size changes (e.g. after load/filter/create)
  watch(
    () => filteredItems.value.length,
    () => {
      if (page.value > pageCount.value) page.value = pageCount.value
      if (page.value < 1) page.value = 1
    }
  )

  return {
    search,
    page,
    pageSize,
    filteredItems,
    pagedItems,
    pageCount,
    setPage,
  }
}
