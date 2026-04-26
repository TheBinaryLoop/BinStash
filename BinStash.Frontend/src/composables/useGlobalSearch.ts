import { ref, watch } from 'vue'
import { searchLocal } from '@/search/local-search'
import type { SearchResult } from '@/search/types'

export function useGlobalSearch() {
  const query = ref('')
  const loading = ref(false)
  const localResults = ref<SearchResult[]>([])
  const remoteResults = ref<SearchResult[]>([])

  let timer: number | undefined

  watch(query, (value) => {
    localResults.value = searchLocal(value)

    window.clearTimeout(timer)

    if (!value.trim()) {
      remoteResults.value = []
      loading.value = false
      return
    }

    timer = window.setTimeout(async () => {
      loading.value = true
      try {
        const response = await fetch(`/api/search?q=${encodeURIComponent(value)}`, {
          credentials: 'include'
        })

        if (!response.ok) {
          remoteResults.value = []
          return
        }

        remoteResults.value = await response.json()
      } finally {
        loading.value = false
      }
    }, 250)
  })

  return {
    query,
    loading,
    localResults,
    remoteResults
  }
}