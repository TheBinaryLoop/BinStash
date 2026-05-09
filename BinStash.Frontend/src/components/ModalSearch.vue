<template>
  <transition
    enter-active-class="transition ease-out duration-200"
    enter-from-class="opacity-0"
    enter-to-class="opacity-100"
    leave-active-class="transition ease-out duration-100"
    leave-from-class="opacity-100"
    leave-to-class="opacity-0"
  >
    <div
      v-show="modalOpen"
      class="fixed inset-0 bg-gray-900/30 z-50 transition-opacity"
      aria-hidden="true"
    />
  </transition>

  <transition
    enter-active-class="transition ease-in-out duration-200"
    enter-from-class="opacity-0 translate-y-4"
    enter-to-class="opacity-100 translate-y-0"
    leave-active-class="transition ease-in-out duration-200"
    leave-from-class="opacity-100 translate-y-0"
    leave-to-class="opacity-0 translate-y-4"
  >
    <div
      v-show="modalOpen"
      :id="id"
      class="fixed inset-0 z-50 overflow-hidden flex items-start top-20 mb-4 justify-center px-4 sm:px-6"
      role="dialog"
      aria-modal="true"
    >
      <div
        ref="modalContent"
        class="bg-white dark:bg-gray-800 border border-transparent dark:border-gray-700/60 overflow-auto max-w-2xl w-full max-h-full rounded-lg shadow-lg"
      >
        <form
          class="border-b border-gray-200 dark:border-gray-700/60"
          @submit.prevent="openSelected"
        >
          <div class="relative">
            <label :for="searchId" class="sr-only">Search</label>
            <input
              :id="searchId"
              ref="searchInput"
              v-model="query"
              class="w-full dark:text-gray-300 bg-white dark:bg-gray-800 border-0 focus:ring-transparent placeholder-gray-400 dark:placeholder-gray-500 appearance-none py-3 pl-10 pr-4"
              type="search"
              placeholder="Search Anything…"
              autocomplete="off"
              @keydown.down.prevent="moveDown"
              @keydown.up.prevent="moveUp"
              @keydown.enter.prevent="openSelected"
            />
            <button class="absolute inset-0 right-auto group" type="submit" aria-label="Search">
              <svg
                class="shrink-0 fill-current text-gray-400 dark:text-gray-500 group-hover:text-gray-500 dark:group-hover:text-gray-400 ml-4 mr-2"
                width="16"
                height="16"
                viewBox="0 0 16 16"
                xmlns="http://www.w3.org/2000/svg"
              >
                <path d="M7 14c-3.86 0-7-3.14-7-7s3.14-7 7-7 7 3.14 7 7-3.14 7-7 7zM7 2C4.243 2 2 4.243 2 7s2.243 5 5 5 5-2.243 5-5-2.243-5-5-5z" />
                <path d="M15.707 14.293L13.314 11.9a8.019 8.019 0 01-1.414 1.414l2.393 2.393a.997.997 0 001.414 0 .999.999 0 000-1.414z" />
              </svg>
            </button>
          </div>
        </form>

        <div class="py-4 px-2">
          <template v-if="!hasQuery">
            <div v-if="recentSearches.length" class="mb-3 last:mb-0">
              <div class="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase px-2 mb-2">
                Recent searches
              </div>
              <ul class="text-sm">
                <li v-for="item in recentSearches" :key="`recent-search-${item.id}`">
                  <button
                    type="button"
                    class="w-full flex items-center p-2 text-left text-gray-800 dark:text-gray-100 hover:bg-gray-100 dark:hover:bg-gray-700/20 rounded-lg"
                    @click="openResult(item)"
                  >
                    <svg
                      class="fill-current text-gray-400 dark:text-gray-500 shrink-0 mr-3"
                      width="16"
                      height="16"
                      viewBox="0 0 16 16"
                    >
                      <path d="M15.707 14.293v.001a1 1 0 01-1.414 1.414L11.185 12.6A6.935 6.935 0 017 14a7.016 7.016 0 01-5.173-2.308l-1.537 1.3L0 8l4.873 1.12-1.521 1.285a4.971 4.971 0 008.59-2.835l1.979.454a6.971 6.971 0 01-1.321 3.157l3.107 3.112zM14 6L9.127 4.88l1.521-1.28a4.971 4.971 0 00-8.59 2.83L.084 5.976a6.977 6.977 0 0112.089-3.668l1.537-1.3L14 6z" />
                    </svg>
                    <span>{{ item.title }}</span>
                  </button>
                </li>
              </ul>
            </div>

            <div v-if="recentPages.length" class="mb-3 last:mb-0">
              <div class="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase px-2 mb-2">
                Recent pages
              </div>
              <ul class="text-sm">
                <li v-for="item in recentPages" :key="`recent-page-${item.id}`">
                  <button
                    type="button"
                    class="w-full flex items-center p-2 text-left text-gray-800 dark:text-gray-100 hover:bg-gray-100 dark:hover:bg-gray-700/20 rounded-lg"
                    @click="openResult(item)"
                  >
                    <svg
                      class="fill-current text-gray-400 dark:text-gray-500 shrink-0 mr-3"
                      width="16"
                      height="16"
                      viewBox="0 0 16 16"
                    >
                      <path d="M14 0H2c-.6 0-1 .4-1 1v14c0 .6.4 1 1 1h8l5-5V1c0-.6-.4-1-1-1zM3 2h10v8H9v4H3V2z" />
                    </svg>
                    <span>
                      <span class="font-medium">{{ item.title }}</span>
                      <span v-if="item.subtitle" class="text-gray-600 dark:text-gray-400">
                        — {{ item.subtitle }}
                      </span>
                    </span>
                  </button>
                </li>
              </ul>
            </div>
          </template>

          <template v-else>
            <div v-if="loading" class="px-2 py-4 text-sm text-gray-500 dark:text-gray-400">
              Searching...
            </div>

            <div
              v-else-if="allResults.length === 0"
              class="px-2 py-4 text-sm text-gray-500 dark:text-gray-400"
            >
              No results found.
            </div>

            <template v-else>
              <div v-if="actionResults.length" class="mb-3 last:mb-0">
                <div class="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase px-2 mb-2">
                  Actions
                </div>
                <div class="px-2 mb-2 text-xs text-gray-500 dark:text-gray-400">
                  <span v-if="keepsCurrentSubpathOnSwitch">
                    Switching tenant keeps your current page path.
                  </span>
                  <span v-else>
                    Tip: use <kbd class="font-mono">st &lt;tenant&gt;</kbd> or <kbd class="font-mono">switch &lt;tenant&gt;</kbd>.
                  </span>
                </div>
                <ul class="text-sm">
                  <li v-for="item in actionResults" :key="item.id">
                    <button
                      type="button"
                      class="w-full flex items-center p-2 text-left rounded-lg"
                      :class="isSelected(item.id)
                        ? 'bg-gray-100 dark:bg-gray-700/40 text-gray-900 dark:text-white'
                        : 'text-gray-800 dark:text-gray-100 hover:bg-gray-100 dark:hover:bg-gray-700/20'"
                      @mouseenter="setSelectedById(item.id)"
                      @click="openResult(item)"
                    >
                      <svg
                        class="fill-current text-gray-400 dark:text-gray-500 shrink-0 mr-3"
                        width="16"
                        height="16"
                        viewBox="0 0 16 16"
                      >
                        <path d="M3 14V2a1 1 0 0 1 1-1h8a1 1 0 0 1 1 1v12h-2v-2H5v2H3zm2-4h2V8H5v2zm0-3h2V5H5v2zm4 3h2V8H9v2zm0-3h2V5H9v2z" />
                      </svg>
                      <span>
                        <span class="font-medium">{{ item.title }}</span>
                        <span v-if="item.subtitle" class="text-gray-600 dark:text-gray-400">
                          — {{ item.subtitle }}
                        </span>
                      </span>
                    </button>
                  </li>
                </ul>
              </div>

              <div v-if="settingsAndPageResults.length" class="mb-3 last:mb-0">
                <div class="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase px-2 mb-2">
                  Settings & Pages
                </div>
                <ul class="text-sm">
                  <li v-for="item in settingsAndPageResults" :key="item.id">
                    <button
                      type="button"
                      class="w-full flex items-center p-2 text-left rounded-lg"
                      :class="isSelected(item.id)
                        ? 'bg-gray-100 dark:bg-gray-700/40 text-gray-900 dark:text-white'
                        : 'text-gray-800 dark:text-gray-100 hover:bg-gray-100 dark:hover:bg-gray-700/20'"
                      @mouseenter="setSelectedById(item.id)"
                      @click="openResult(item)"
                    >
                      <IconAdjustmentsHorizontal
                        v-if="item.type === 'setting'"
                        class="w-4 h-4 text-gray-400 dark:text-gray-500 shrink-0 mr-3"
                      />
                      <svg
                        v-if="item.type === 'page'"
                        class="fill-current text-gray-400 dark:text-gray-500 shrink-0 mr-3"
                        width="16"
                        height="16"
                        viewBox="0 0 16 16"
                      >
                        <path d="M14 0H2c-.6 0-1 .4-1 1v14c0 .6.4 1 1 1h8l5-5V1c0-.6-.4-1-1-1zM3 2h10v8H9v4H3V2z" />
                      </svg>
                      <span>
                        <span class="font-medium">{{ item.title }}</span>
                        <span v-if="item.subtitle" class="text-gray-600 dark:text-gray-400">
                          — {{ item.subtitle }}
                        </span>
                      </span>
                    </button>
                  </li>
                </ul>
              </div>

              <div v-if="remoteResults.length" class="mb-3 last:mb-0">
                <div class="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase px-2 mb-2">
                  Objects
                </div>
                <ul class="text-sm">
                  <li v-for="item in remoteResults" :key="item.id">
                    <button
                      type="button"
                      class="w-full flex items-center p-2 text-left rounded-lg"
                      :class="isSelected(item.id)
                        ? 'bg-gray-100 dark:bg-gray-700/40 text-gray-900 dark:text-white'
                        : 'text-gray-800 dark:text-gray-100 hover:bg-gray-100 dark:hover:bg-gray-700/20'"
                      @mouseenter="setSelectedById(item.id)"
                      @click="openResult(item)"
                    >
                      <svg
                        class="fill-current text-gray-400 dark:text-gray-500 shrink-0 mr-3"
                        width="16"
                        height="16"
                        viewBox="0 0 16 16"
                      >
                        <path d="M7 2a2 2 0 100 4 2 2 0 000-4zM7 7c-2.206 0-4 1.346-4 3v1h8v-1c0-1.654-1.794-3-4-3zM12 5h4v1h-4zM12 8h4v1h-4zM12 11h4v1h-4z" />
                      </svg>
                      <span>
                        <span class="font-medium">{{ item.title }}</span>
                        <span v-if="item.subtitle" class="text-gray-600 dark:text-gray-400">
                          — {{ item.subtitle }}
                        </span>
                      </span>
                    </button>
                  </li>
                </ul>
              </div>
            </template>
          </template>
        </div>
      </div>
    </div>
  </transition>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useGlobalSearch } from '@/composables/useGlobalSearch'
import type { SearchResult } from '@/search/types'
import { IconAdjustmentsHorizontal } from '@tabler/icons-vue'
import { useTenantStore } from '@/stores/tenant'
import { rememberRecentTenantSwitch } from '@/search/action-history'

interface Props {
  id: string
  searchId: string
  modalOpen: boolean
}

const props = defineProps<Props>()

const emit = defineEmits<{
  (e: 'open-modal'): void
  (e: 'close-modal'): void
}>()

const router = useRouter()
const route = useRoute()
const tenantStore = useTenantStore()

const modalContent = ref<HTMLElement | null>(null)
const searchInput = ref<HTMLInputElement | null>(null)
const selectedIndex = ref(0)

const { query, loading, localResults, remoteResults } = useGlobalSearch()

const recentSearches = ref<SearchResult[]>([])
const recentPages = ref<SearchResult[]>([])

const hasQuery = computed(() => query.value.trim().length > 0)

const allResults = computed<SearchResult[]>(() => [
  ...localResults.value,
  ...remoteResults.value,
])

const actionResults = computed(() => localResults.value.filter(item => item.type === 'action'))
const settingsAndPageResults = computed(() => localResults.value.filter(item => item.type !== 'action'))
const keepsCurrentSubpathOnSwitch = computed(() => route.fullPath.startsWith('/t/'))

const selectedResult = computed<SearchResult | undefined>(() => {
  if (allResults.value.length === 0) return undefined
  return allResults.value[Math.max(0, Math.min(selectedIndex.value, allResults.value.length - 1))]
})

function closeModal() {
  emit('close-modal')
}

function resetState() {
  query.value = ''
  selectedIndex.value = 0
}

function buildTargetForTenant(newTenantId: string) {
  const full = route.fullPath
  if (full.startsWith('/t/')) {
    return full.replace(/^\/t\/[^/]+/, `/t/${newTenantId}`)
  }

  return `/t/${newTenantId}`
}

async function openResult(result: SearchResult) {
  saveRecent(result)

  if (result.actionKind === 'switch-tenant' && result.tenantId) {
    rememberRecentTenantSwitch(result.tenantId)
    tenantStore.setCurrentTenant(result.tenantId)
    await router.push(buildTargetForTenant(result.tenantId))
    closeModal()
    return
  }

  if (result.url) {
    await router.push(result.url)
  }

  closeModal()
}

async function openSelected() {
  if (selectedResult.value) {
    await openResult(selectedResult.value)
  }
}

function moveDown() {
  if (allResults.value.length === 0) return
  selectedIndex.value = Math.min(selectedIndex.value + 1, allResults.value.length - 1)
}

function moveUp() {
  if (allResults.value.length === 0) return
  selectedIndex.value = Math.max(selectedIndex.value - 1, 0)
}

function setSelectedById(id: string) {
  const index = allResults.value.findIndex(x => x.id === id)
  if (index >= 0) {
    selectedIndex.value = index
  }
}

function isSelected(id: string) {
  return selectedResult.value?.id === id
}

function clickHandler(event: MouseEvent) {
  const target = event.target as Node | null
  if (!props.modalOpen || !target || !modalContent.value || modalContent.value.contains(target)) return
  closeModal()
}

function keyHandler(event: KeyboardEvent) {
  if (!props.modalOpen) return
  if (event.key === 'Escape') {
    closeModal()
  }
}

function saveRecent(result: SearchResult) {
  const isPageLike = result.type === 'page' || result.type === 'setting'

  recentSearches.value = dedupeAndLimit([result, ...recentSearches.value], 6)

  if (isPageLike) {
    recentPages.value = dedupeAndLimit([result, ...recentPages.value], 6)
  }

  localStorage.setItem('search-recent-searches', JSON.stringify(recentSearches.value))
  localStorage.setItem('search-recent-pages', JSON.stringify(recentPages.value))
}

function dedupeAndLimit(items: SearchResult[], limit: number): SearchResult[] {
  const seen = new Set<string>()
  const result: SearchResult[] = []

  for (const item of items) {
    if (seen.has(item.id)) continue
    seen.add(item.id)
    result.push(item)
    if (result.length >= limit) break
  }

  return result
}

onMounted(() => {
  document.addEventListener('click', clickHandler)
  document.addEventListener('keydown', keyHandler)

  try {
    const savedSearches = localStorage.getItem('search-recent-searches')
    const savedPages = localStorage.getItem('search-recent-pages')

    recentSearches.value = savedSearches ? JSON.parse(savedSearches) as SearchResult[] : []
    recentPages.value = savedPages ? JSON.parse(savedPages) as SearchResult[] : []
  } catch {
    recentSearches.value = []
    recentPages.value = []
  }
})

onUnmounted(() => {
  document.removeEventListener('click', clickHandler)
  document.removeEventListener('keydown', keyHandler)
})

watch(
  () => props.modalOpen,
  async (open) => {
    if (!open) {
      resetState()
      return
    }

    await nextTick()
    searchInput.value?.focus()
  }
)

watch(allResults, (results) => {
  if (results.length === 0) {
    selectedIndex.value = 0
    return
  }

  if (selectedIndex.value >= results.length) {
    selectedIndex.value = results.length - 1
  }
})
</script>