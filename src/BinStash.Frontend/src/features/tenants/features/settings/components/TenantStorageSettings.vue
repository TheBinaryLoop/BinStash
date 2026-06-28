<template>
  <div
    class="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700/60 shadow-sm overflow-hidden"
  >
    <div class="px-5 py-4 border-b border-gray-200 dark:border-gray-700/60">
      <h2 class="font-semibold text-gray-800 dark:text-gray-100">Storage Classes</h2>
    </div>

    <div class="p-5">
      <div v-if="loadingClasses" class="text-sm text-gray-400">Loading…</div>
      <div v-else-if="storageClasses.length === 0" class="text-sm text-gray-400">No storage classes configured.</div>

      <div v-else class="space-y-2">
        <div
          v-for="sc in storageClasses"
          :key="sc.name"
          class="flex items-center justify-between gap-4 px-4 py-3 bg-gray-50 dark:bg-gray-700/30 rounded-lg border border-gray-200 dark:border-gray-700"
        >
          <div>
            <span class="font-medium text-sm text-gray-800 dark:text-gray-100">{{ sc.name }}</span>
            <span v-if="sc.description" class="ml-2 text-xs text-gray-500 dark:text-gray-400">{{ sc.description }}</span>
          </div>

          <span
            v-if="sc.isDefault"
            class="text-xs px-2 py-0.5 rounded-full font-medium bg-teal-100 text-teal-700 dark:bg-teal-900/30 dark:text-teal-400"
          >
            Default
          </span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { listStorageClasses, type StorageClassDto } from '@/api/tenants'

const loadingClasses = ref(true)
const storageClasses = ref<StorageClassDto[]>([])

onMounted(async () => {
  try {
    storageClasses.value = await listStorageClasses()
  } catch {
    storageClasses.value = []
  } finally {
    loadingClasses.value = false
  }
})
</script>
