<template>
  <form @submit.prevent="onSubmit" class="flex flex-col gap-4">
    <h2 class="text-xl font-bold text-gray-800 dark:text-gray-100">Step 1: Enter Setup Code</h2>
    <p class="text-gray-600 dark:text-gray-400">
      Enter the setup code from the server logs/console to begin the BinStash setup process.
    </p>
    <div class="flex flex-col gap-1">
      <label for="setup-code" class="text-sm font-medium text-gray-700 dark:text-gray-300">Setup Code</label>
      <input
        id="setup-code"
        v-model="code"
        type="text"
        autocomplete="off"
        required
        placeholder="ABCD-EFGH-IJKL-MNOP"
        :disabled="loading"
        class="w-full px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-violet-500 disabled:opacity-50"
      />
    </div>
    <div v-if="error" class="text-red-600 dark:text-red-400 text-sm">{{ error }}</div>
<button
      type="submit"
      :disabled="loading"
      class="flex items-center justify-center px-6 py-2 text-sm font-medium bg-violet-500 hover:bg-violet-600 text-white rounded-md cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
    >
      <Spinner v-if="loading" color="white" class="w-4 h-4 mr-2" />
      {{ loading ? 'Verifying...' : 'Start Setup Session' }}
    </button>
  </form>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { claimSetupSession } from '@/features/setup/api/setup.api'
import Spinner from '@/shared/components/feedback/Spinner.vue'

const setupStore = useSetupStore()
const code = ref('')
const loading = ref(false)
const error = ref<string | null>(null)

async function onSubmit() {
  loading.value = true
  error.value = null
  try {
    await claimSetupSession(code.value.trim())
    // Wait briefly to allow the setup cookie to be set before fetching status
    await new Promise(resolve => setTimeout(resolve, 200))
    await setupStore.fetchStatus()
    code.value = ''
  } catch (e: any) {
    if (e.message === 'Request failed (401)') {
      error.value = 'Invalid or expired setup code.'
    } else if (e.message === 'setup_required') {
      error.value = 'Setup is required but could not be claimed. Try again.'
    } else {
      error.value = e.message || 'Failed to claim setup session.'
    }
  } finally {
    loading.value = false
  }
}
</script>