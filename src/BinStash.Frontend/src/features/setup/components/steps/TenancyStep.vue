<template>
  <form
    @submit.prevent="onSubmit"
    class="flex flex-col gap-4"
  >
    <h2 class="text-xl font-bold text-gray-800 dark:text-gray-100">Step 2: Select Tenancy Mode</h2>
    <p class="text-gray-600 dark:text-gray-400">
      Choose whether this BinStash instance will operate in Single-tenant or Multi-tenant mode.
    </p>
    <div v-if="locked" class="text-red-600 dark:text-red-400 text-sm mb-4">
      <strong>Configured in appsettings/env and cannot be changed here.</strong>
    </div>
    <div class="flex gap-6 mb-4" v-else>
      <label class="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300 cursor-pointer">
        <input type="radio" value="Single" v-model="mode" :disabled="loading" class="accent-violet-500" />
        Single
      </label>
      <label class="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300 cursor-pointer">
        <input type="radio" value="Multi" v-model="mode" :disabled="loading" class="accent-violet-500" />
        Multi
      </label>
    </div>
    <div v-if="error" class="text-red-600 dark:text-red-400 text-sm">{{ error }}</div>
<button
      type="submit"
      :disabled="loading || !mode || locked"
      class="flex items-center justify-center px-6 py-2 text-sm font-medium bg-violet-500 hover:bg-violet-600 text-white rounded-md cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
    >
      <Spinner v-if="loading" color="white" class="w-4 h-4 mr-2" />
      {{ loading ? 'Saving...' : locked ? 'Locked' : 'Save Tenancy Mode' }}
    </button>
  </form>
</template>

<script lang="ts" setup>
import { ref, computed, onMounted } from 'vue'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { setTenancyMode, getSetupStatus } from '@/features/setup/api/setup.api'
import Spinner from '@/shared/components/feedback/Spinner.vue'

const setupStore = useSetupStore()
const status = computed(() => setupStore.status)
const loading = ref(false)
const error = ref<string | null>(null)
const mode = ref<'Single' | 'Multi' | null>(null)
const locked = ref(false)

onMounted(() => {
  if (status.value?.data?.tenancyMode) {
    mode.value = status.value.data.tenancyMode
  }
  if (status.value && typeof status.value.data?.tenancyMode === 'string') {
    // If backend returns locked, we should fetch it after submit, but for now assume not locked
    locked.value = false
  }
})

async function onSubmit() {
  if (!mode.value) return
  loading.value = true
  error.value = null
  try {
    const res = await setTenancyMode(mode.value)
    locked.value = !!res.locked
    await setupStore.fetchStatus()
  } catch (e: any) {
    error.value = e.message || 'Failed to set tenancy mode.'
  } finally {
    loading.value = false
  }
}
</script>