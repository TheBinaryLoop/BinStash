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
    <BaseModal v-model:open="createOpen" title="Create chunk store" size="lg" :close-on-backdrop="!isCreating">
      <form id="cs-create-form" class="space-y-4" @submit.prevent="submitCreate">
        <BaseInput v-model.trim="form.name" label="Name" required :disabled="isCreating" />
        <BaseSelect
          v-model="form.type"
          label="Type"
          required
          :disabled="isCreating"
          hint="Choose the storage backend implementation."
        >
          <option value="Local">Local</option>
        </BaseSelect>
        <BaseInput
          v-model.trim="form.localPath"
          label="Local path"
          required
          :disabled="isCreating"
          placeholder="/var/lib/binstash/chunkstore"
        />
        <BaseCheckbox v-model="form.enableChunker" :disabled="isCreating" label="Configure chunker" />
        <div v-if="form.enableChunker" class="space-y-4">
          <BaseInput v-model.trim="form.chunkerType" label="Chunker type" :disabled="isCreating" placeholder="FastCDC" />
          <div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <BaseInput v-model.number="form.minChunkSize" label="Min" type="number" :disabled="isCreating" placeholder="16384" />
            <BaseInput v-model.number="form.avgChunkSize" label="Avg" type="number" :disabled="isCreating" placeholder="65536" />
            <BaseInput v-model.number="form.maxChunkSize" label="Max" type="number" :disabled="isCreating" placeholder="262144" />
          </div>
        </div>
        <div v-if="createError" class="rounded-control border border-danger/20 bg-danger-soft px-3 py-2 text-sm text-danger">
          {{ createError }}
        </div>
      </form>
      <template #footer>
        <BaseButton variant="secondary" :disabled="isCreating" @click="closeCreate()">Cancel</BaseButton>
        <BaseButton :loading="isCreating" @click="submitCreate">Create</BaseButton>
      </template>
    </BaseModal>

  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import Sidebar from '@/features/instance/components/InstanceSidebar.vue'
import Header from '@/shared/components/navigation/Header.vue'
import CardGrid from '@/components/list/CardGrid.vue'
import ChunkStoreCard from '@/partials/chunkstores/ChunkStoreCard.vue'
import { BaseModal, BaseInput, BaseSelect, BaseButton, BaseCheckbox } from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'

import FileDatabaseIcon from '@/images/icons/file-database.svg'
import { createChunkStore, listChunkStores, type ChunkStoreSummaryDto } from '../api/chunkStores'

const sidebarOpen = ref(false)
const toast = useToast()

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
    toast.success('Chunk store created')
    // reload list
    await load()
  } catch (e) {
    createError.value = e instanceof Error ? e.message : 'Could not create chunk store.'
    toast.error(createError.value)
  } finally {
    isCreating.value = false
  }
}

onMounted(load)
</script>
