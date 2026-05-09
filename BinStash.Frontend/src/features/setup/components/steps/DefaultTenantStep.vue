<template>
  <form @submit.prevent="onSubmit" class="flex flex-col gap-4">
    <h2 class="text-xl font-bold text-gray-800 dark:text-gray-100">Step: Configure Default Tenant</h2>
    <p class="text-gray-600 dark:text-gray-400">
      Set up the name and slug for your default tenant.
    </p>
    <div class="flex flex-col gap-1">
      <label for="tenant-name" class="text-sm font-medium text-gray-700 dark:text-gray-300">Tenant Name</label>
      <input
        id="tenant-name"
        v-model="name"
        type="text"
        required
        :disabled="loading"
        class="w-full px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-violet-500 disabled:opacity-50"
      />
    </div>
    <div class="flex flex-col gap-1">
      <label for="tenant-slug" class="text-sm font-medium text-gray-700 dark:text-gray-300">Tenant Slug</label>
      <input
        id="tenant-slug"
        v-model="slug"
        type="text"
        required
        :disabled="loading"
        class="w-full px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-violet-500 disabled:opacity-50"
      />
    </div>
    <button
      type="submit"
      :disabled="loading || !name || !slug"
      class="flex items-center justify-center px-6 py-2 text-sm font-medium bg-violet-500 hover:bg-violet-600 text-white rounded-md cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
    >
      <Spinner v-if="loading" color="white" class="w-4 h-4 mr-2" />
      {{ loading ? 'Saving...' : 'Save Default Tenant' }}
    </button>
    <div v-if="success" class="text-green-600 dark:text-green-400 text-sm">
      Default tenant configured successfully.
    </div>
    <div v-if="error" class="text-red-600 dark:text-red-400 text-sm">{{ error }}</div>
  </form>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { configureDefaultTenant } from '@/features/setup/api/setup.api'
import { useSetupStore } from '@/features/setup/store/setup.store'
import Spinner from '@/shared/components/feedback/Spinner.vue'

const setupStore = useSetupStore()
const name = ref('')
const slug = ref('')
const loading = ref(false)
const error = ref<string | null>(null)
const success = ref(false)

async function onSubmit() {
  loading.value = true
  error.value = null
  success.value = false
  try {
    await configureDefaultTenant({
      name: name.value.trim(),
      slug: slug.value.trim(),
    })
    success.value = true
    await setupStore.fetchStatus()
  } catch (e: any) {
    error.value = e.message || 'Failed to configure default tenant.'
  } finally {
    loading.value = false
  }
}
</script>