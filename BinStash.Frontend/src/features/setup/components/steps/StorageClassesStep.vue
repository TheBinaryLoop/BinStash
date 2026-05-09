<template>
  <form
    @submit.prevent="onSubmit"
    class="flex flex-col gap-4 w-full max-w-none"
  >
    <h2 class="text-xl font-bold text-gray-800 dark:text-gray-100">Step 4: Create Storage Classes</h2>
    <p class="text-gray-600 dark:text-gray-400">
      Add one or more storage classes. At least one is required.
    </p>
    <div class="flex flex-col gap-3 w-full max-w-full">
      <div
        v-for="(sc, idx) in storageClasses"
        :key="idx"
        class="flex flex-row flex-wrap gap-4 items-center bg-gray-50 dark:bg-gray-700/50 border border-gray-200 dark:border-gray-600 rounded-lg px-4 py-3 w-full box-border"
      >
        <input
          v-model="sc.name"
          placeholder="Name"
          required
          class="flex-1 min-w-40 px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-violet-500"
        />
        <input
          v-model="sc.displayName"
          placeholder="Display Name"
          required
          class="flex-1 min-w-40 px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-violet-500"
        />
        <input
          v-model="sc.description"
          placeholder="Description"
          class="flex-1 min-w-40 px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-violet-500"
        />
        <button
          type="button"
          @click="remove(idx)"
          v-if="storageClasses.length > 1"
          class="flex-none px-3 py-2 text-sm text-red-600 dark:text-red-400 hover:text-red-700 dark:hover:text-red-300 cursor-pointer"
        >
          Remove
        </button>
      </div>
    </div>
    <button
      type="button"
      @click="add"
      class="px-4 py-2 text-sm font-medium bg-gray-100 hover:bg-gray-200 dark:bg-gray-700 dark:hover:bg-gray-600 text-gray-700 dark:text-gray-300 rounded-md cursor-pointer transition-colors w-fit"
    >
      Add Storage Class
    </button>
    <div v-if="error" class="text-red-600 dark:text-red-400 text-sm">{{ error }}</div>
    <button
      type="submit"
      :disabled="loading"
      class="flex items-center justify-center px-6 py-2 text-sm font-medium bg-violet-500 hover:bg-violet-600 text-white rounded-md cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
    >
      <Spinner v-if="loading" color="white" class="w-4 h-4 mr-2" />
      {{ loading ? 'Saving...' : 'Save Storage Classes' }}
    </button>
    <div v-if="success" class="text-green-600 dark:text-green-400 text-sm">
      Storage classes saved successfully.
    </div>
  </form>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { createStorageClasses } from '@/features/setup/api/setup.api'
import Spinner from '@/shared/components/feedback/Spinner.vue'

const setupStore = useSetupStore()
const loading = ref(false)
const error = ref<string | null>(null)
const success = ref(false)

interface StorageClass {
  name: string
  displayName: string
  description?: string
}

const storageClasses = ref<StorageClass[]>([
  { name: '', displayName: '', description: '' }
])

function add() {
  storageClasses.value.push({ name: '', displayName: '', description: '' })
}

function remove(idx: number) {
  storageClasses.value.splice(idx, 1)
}

async function onSubmit() {
  error.value = null
  success.value = false
  if (storageClasses.value.length === 0) {
    error.value = 'At least one storage class is required.'
    return
  }
  if (storageClasses.value.some(sc => !sc.name || !sc.displayName)) {
    error.value = 'Name and Display Name are required for each storage class.'
    return
  }
  loading.value = true
  try {
    await createStorageClasses(storageClasses.value)
    success.value = true
    await setupStore.fetchStatus()
  } catch (e: any) {
    error.value = e.message || 'Failed to save storage classes.'
  } finally {
    loading.value = false
  }
}
</script>