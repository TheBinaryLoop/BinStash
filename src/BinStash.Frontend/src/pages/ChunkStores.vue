<template>
  <div class="flex h-dvh overflow-hidden">
    <!-- Sidebar -->
    <Sidebar :sidebarOpen="sidebarOpen" @close-sidebar="sidebarOpen = false" />

    <!-- Content area -->
    <div class="relative flex flex-col flex-1 overflow-y-auto overflow-x-hidden">
      <!-- Site header -->
      <Header :sidebarOpen="sidebarOpen" @toggle-sidebar="sidebarOpen = !sidebarOpen" />

      <main class="grow">
        <div class="px-4 sm:px-6 lg:px-8 py-8 w-full max-w-384 mx-auto">
          <CardGrid
              title="Chunk Stores"
              subtitle="Manage storage backends for packs and indexes."
              :items="items"
              :filter-fn="filterFn"
              :get-key="(x) => x.id"
              :is-loading="isLoading"
              :error="error ?? ''"
              search-placeholder="Search chunk stores…"
              empty-title="No chunk stores yet"
              empty-text="Create your first chunk store to start ingesting chunks and packs."
              empty-action-label="Create chunk store"
              :primary-action="{ label: 'Add Chunk Store', onClick: openCreate }"
          >
            <template #card="{ item }">
              <ChunkStoreCard :item="item" class="col-span-12 sm:col-span-6 xl:col-span-3" />
            </template>
          </CardGrid>
        </div>
      </main>
    </div>

    <!-- Create modal -->
    <div v-if="createOpen" class="fixed inset-0 z-50">
      <!-- Backdrop -->
      <div class="absolute inset-0 bg-slate-900/30" @click="closeCreate()" />

      <!-- Modal -->
      <div class="absolute inset-0 flex items-center justify-center px-4 py-6">
        <div
          class="bg-white dark:bg-gray-800 rounded-xl shadow-lg w-full max-w-lg border border-gray-200 dark:border-gray-700/60">
          <div class="px-5 py-4 border-b border-gray-200 dark:border-gray-700/60 flex items-center justify-between">
            <div class="font-semibold text-gray-800 dark:text-gray-100">Create chunk store</div>
            <button class="text-gray-400 hover:text-gray-500 dark:hover:text-gray-300" @click="closeCreate()">
              <span class="sr-only">Close</span>
              ✕
            </button>
          </div>

          <form class="px-5 py-4 space-y-4" @submit.prevent="submitCreate">
            <div>
              <label class="block text-sm font-medium mb-1" for="cs-name">Name</label>
              <input id="cs-name" class="form-input w-full" v-model.trim="form.name" required :disabled="isCreating" />
            </div>

            <div>
              <label class="block text-sm font-medium mb-1" for="cs-type">Type</label>
              <select id="cs-type" class="form-select w-full" v-model="form.type" required :disabled="isCreating">
                <!-- adjust these to your actual backend-supported types -->
                <option value="Local">Local</option>
              </select>
              <div class="text-xs text-gray-500 dark:text-gray-400 mt-1">
                Choose the storage backend implementation.
              </div>
            </div>

            <div>
              <label class="block text-sm font-medium mb-1" for="cs-path">Local path</label>
              <input id="cs-path" class="form-input w-full" v-model.trim="form.localPath" required
                :disabled="isCreating" placeholder="/var/lib/binstash/chunkstore" />
            </div>

            <div class="pt-2">
              <label class="flex items-center gap-2 select-none">
                <input type="checkbox" class="form-checkbox" v-model="form.enableChunker" :disabled="isCreating" />
                <span class="text-sm font-medium text-gray-800 dark:text-gray-100">Configure chunker</span>
              </label>
            </div>

            <div v-if="form.enableChunker" class="space-y-4">
              <div>
                <label class="block text-sm font-medium mb-1" for="ch-type">Chunker type</label>
                <input id="ch-type" class="form-input w-full" v-model.trim="form.chunkerType" :disabled="isCreating"
                  placeholder="FastCDC" />
              </div>

              <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
                <div>
                  <label class="block text-sm font-medium mb-1" for="ch-min">Min</label>
                  <input id="ch-min" class="form-input w-full" type="number" min="1" v-model.number="form.minChunkSize"
                    :disabled="isCreating" placeholder="16384" />
                </div>
                <div>
                  <label class="block text-sm font-medium mb-1" for="ch-avg">Avg</label>
                  <input id="ch-avg" class="form-input w-full" type="number" min="1" v-model.number="form.avgChunkSize"
                    :disabled="isCreating" placeholder="65536" />
                </div>
                <div>
                  <label class="block text-sm font-medium mb-1" for="ch-max">Max</label>
                  <input id="ch-max" class="form-input w-full" type="number" min="1" v-model.number="form.maxChunkSize"
                    :disabled="isCreating" placeholder="262144" />
                </div>
              </div>
            </div>

            <div v-if="createError" class="pt-1">
              <div class="bg-rose-500/20 text-rose-700 dark:text-rose-200 px-3 py-2 rounded-lg">
                <span class="text-sm">{{ createError }}</span>
              </div>
            </div>

            <div class="flex items-center justify-end gap-2 pt-2">
              <button type="button"
                class="btn border-gray-200 hover:border-gray-300 dark:border-gray-700/60 dark:hover:border-gray-600"
                @click="closeCreate()" :disabled="isCreating">
                Cancel
              </button>
              <button type="submit"
                class="btn bg-gray-900 text-gray-100 hover:bg-gray-800 dark:bg-gray-100 dark:text-gray-800 dark:hover:bg-white"
                :disabled="isCreating">
                <span v-if="!isCreating">Create</span>
                <span v-else>Creating…</span>
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>

  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import Sidebar from '@/features/instance/components/InstanceSidebar.vue'
import Header from '@/shared/components/navigation/Header.vue'
import CardGrid from '@/components/list/CardGrid.vue'
import ChunkStoreCard from '@/partials/chunkstores/ChunkStoreCard.vue'

import FileDatabaseIcon from '@/images/icons/file-database.svg'
import { createChunkStore, listChunkStores, type ChunkStoreSummaryDto } from '../api/chunkStores'

const sidebarOpen = ref(false)

type CardItem = {
  id: string
  name: string
  image: string
  link: string
  location?: string
  content?: string
}


const isLoading = ref(false)
const error = ref<string | null>(null)
const items = ref<CardItem[]>([])

const filterFn = (item: CardItem, q: string) =>
  item.name.toLowerCase().includes(q) ||
  (item.content ?? '').toLowerCase().includes(q)


async function load() {
  isLoading.value = true
  error.value = null
  try {
    const data = await listChunkStores()
    items.value = data.map(mapToCard)
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Could not load chunk stores.'
  } finally {
    isLoading.value = false
  }
}

function mapToCard(x: ChunkStoreSummaryDto): CardItem {
  return {
    id: x.id,
    name: x.name,
    image: FileDatabaseIcon,
    link: `/chunk-stores/${x.id}`,
    location: '💾',
    content: 'Storage backend for packs + indexes.',
  }
}

/* Create modal */
const createOpen = ref(false)
const isCreating = ref(false)
const createError = ref<string | null>(null)

const form = ref({
  name: '',
  type: 'Local',
  localPath: '',
  enableChunker: false,
  chunkerType: 'FastCDC',
  minChunkSize: undefined as number | undefined,
  avgChunkSize: undefined as number | undefined,
  maxChunkSize: undefined as number | undefined,
})

function openCreate() {
  createError.value = null
  createOpen.value = true
}

function closeCreate() {
  if (isCreating.value) return
  createOpen.value = false
}

async function submitCreate() {
  createError.value = null
  isCreating.value = true
  try {
    await createChunkStore({
      name: form.value.name,
      type: form.value.type,
      localPath: form.value.localPath,
      chunker: form.value.enableChunker
        ? {
          type: form.value.chunkerType,
          minChunkSize: form.value.minChunkSize ?? null,
          avgChunkSize: form.value.avgChunkSize ?? null,
          maxChunkSize: form.value.maxChunkSize ?? null,
        }
        : null,
    })

    createOpen.value = false
    // reload list
    await load()
  } catch (e) {
    createError.value = e instanceof Error ? e.message : 'Could not create chunk store.'
  } finally {
    isCreating.value = false
  }
}

onMounted(load)
</script>
