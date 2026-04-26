<template>
  <div class="flex flex-col gap-4">
    <template v-if="existingChunkstores.length > 0 && !showForm">
      <h2 class="text-xl font-bold text-gray-800 dark:text-gray-100">Step 4: Configure Local Chunkstore</h2>
      <p class="text-gray-600 dark:text-gray-400">
        The following chunkstores are already configured:
      </p>
      <ul class="list-disc ml-5 text-gray-700 dark:text-gray-300">
        <li v-for="store in existingChunkstores" :key="store.id">
          <strong>{{ store.name }}</strong>
          <span class="text-gray-500 dark:text-gray-400">({{ store.id }})</span>
        </li>
      </ul>
      <div class="mt-4 flex gap-3">
        <button
          class="px-6 py-2 text-sm font-medium bg-gray-200 hover:bg-gray-300 dark:bg-gray-700 dark:hover:bg-gray-600 text-gray-800 dark:text-gray-100 rounded-md cursor-pointer transition-colors"
          @click="handleAddAnother"
        >
          Add another chunkstore
        </button>
        <button
          class="px-6 py-2 text-sm font-medium bg-violet-500 hover:bg-violet-600 text-white rounded-md cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          @click="handleContinue"
        >
          Continue
        </button>
      </div>
    </template>
    <form v-else @submit.prevent="onSubmit" class="flex flex-col gap-4">
      <h2 class="text-xl font-bold text-gray-800 dark:text-gray-100">Step 3: Configure Local Chunkstore</h2>
      <p class="text-gray-600 dark:text-gray-400">
        Configure the local chunkstore for BinStash. You can use the default name and path or customize them.
      </p>
      <div class="flex flex-col gap-1">
        <label for="chunkstore-name" class="text-sm font-medium text-gray-700 dark:text-gray-300">Chunkstore Name</label>
        <input
          id="chunkstore-name"
          v-model="name"
          type="text"
          required
          :disabled="loading"
          autocomplete="off"
          class="w-full px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-violet-500 disabled:opacity-50"
        />
      </div>
      <div class="flex flex-col gap-1">
        <label for="chunkstore-type" class="text-sm font-medium text-gray-700 dark:text-gray-300">Chunkstore Type</label>
        <select
          id="chunkstore-type"
          v-model="type"
          class="w-full px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-violet-500"
        >
          <option selected disabled>- Select a type -</option>
          <option v-for="chunkstoreType in enabledChunkStoreTypes" :value="chunkstoreType.value">{{ chunkstoreType.name }}</option>
        </select>
      </div>
      <div class="flex flex-col gap-1">
        <label for="chunkstore-path" class="text-sm font-medium text-gray-700 dark:text-gray-300">Local Path</label>
        <input
          id="chunkstore-path"
          v-model="localPath"
          type="text"
          required
          :disabled="loading"
          autocomplete="off"
          class="w-full px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-violet-500 disabled:opacity-50"
        />
      </div>
      <div v-if="chunkStoreId" class="text-green-600 dark:text-green-400 text-sm">
        Chunkstore created! ID: <code class="font-mono text-xs bg-gray-100 dark:bg-gray-700 px-1 py-0.5 rounded">{{ chunkStoreId }}</code>
      </div>
      <div v-if="error" class="text-red-600 dark:text-red-400 text-sm">{{ error }}</div>
<button
      type="submit"
      :disabled="loading"
      class="flex items-center justify-center px-6 py-2 text-sm font-medium bg-violet-500 hover:bg-violet-600 text-white rounded-md cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
    >
      <Spinner v-if="loading" color="white" class="w-4 h-4 mr-2" />
      {{ loading ? 'Creating...' : 'Create Chunkstore' }}
    </button>
    </form>
  </div>
</template>

<script lang="ts" setup>
import { ref, onMounted } from 'vue'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { listChunkStores, createChunkStore as createApiChunkStore, getEnabledChunkStoreTypes, setChunkStoreDone } from '@/features/setup/api/setup.api'
import { ChunkStoreSummaryDto } from '@/api/chunkStores'
import Spinner from '@/shared/components/feedback/Spinner.vue'

const setupStore = useSetupStore()
const name = ref('local')
const localPath = ref('')
const type = ref(-1)
const enabledChunkStoreTypes = ref<{ name: string, value: number }[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const chunkStoreId = ref<string | null>(null)
const existingChunkstores = ref<ChunkStoreSummaryDto[]>([])
const showForm = ref(false)

function getDefaultPath() {
  const ua = navigator.userAgent
  if (/Windows/i.test(ua)) return 'C:\\BinStash\\Chunks'
  if (/Linux/i.test(ua)) return '/var/lib/binstash/chunks'
  return './data/chunks'
}

async function fetchChunkstores() {
  try {
    existingChunkstores.value = await listChunkStores()
  } catch {
    existingChunkstores.value = []
  }
}

onMounted(async () => {
  localPath.value = getDefaultPath()
  getEnabledChunkStoreTypes()
    .then(types => {
      enabledChunkStoreTypes.value = types
    })
    .catch(() => {
      error.value = 'Failed to fetch chunkstore types.'
    })
  await fetchChunkstores()
  showForm.value = existingChunkstores.value.length === 0
})

async function onSubmit() {
  loading.value = true
  error.value = null
  chunkStoreId.value = null
  try {
    const res = await createApiChunkStore({
      type: type.value.toString(),
      name: name.value.trim(),
      localPath: localPath.value.trim(),
    })
    chunkStoreId.value = res.id
    await fetchChunkstores()
    showForm.value = false
    await setupStore.fetchStatus()
  } catch (e: any) {
    error.value = e.message || 'Failed to create chunkstore.'
  } finally {
    loading.value = false
  }
}

function handleAddAnother() {
  name.value = 'local'
  localPath.value = getDefaultPath()
  type.value = -1
  chunkStoreId.value = null
  error.value = null
  showForm.value = true
}

async function handleContinue() {
  showForm.value = false
  await markChunkStoreDone()
}

async function markChunkStoreDone() {
  loading.value = true
  error.value = null
  try {
    await setChunkStoreDone()
    await setupStore.fetchStatus()
  } catch (e: any) {
    error.value = e.message || 'Failed to mark chunkstore as done.'
  } finally {
    loading.value = false
  }
}
</script>