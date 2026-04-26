<template>
  <div class="flex flex-col gap-4">
    <h2 class="text-xl font-bold text-gray-800 dark:text-gray-100 mb-2">Step 7: Review Setup</h2>
    <p class="text-gray-600 dark:text-gray-400 mb-4">Please review your configuration before finishing the setup. If you need to make changes, use the Reset button.</p>
    <div class="bg-gray-50 dark:bg-gray-700/50 border border-gray-200 dark:border-gray-600 rounded-lg p-4 flex flex-col gap-2">
      <div class="text-sm text-gray-700 dark:text-gray-300">
        <strong class="text-gray-800 dark:text-gray-100">Tenancy Mode: </strong>
        <span>{{ status.data?.tenancyMode || 'N/A' }}</span>
      </div>
      <div v-if="status.data?.tenancyMode === 'Single'" class="text-sm text-gray-700 dark:text-gray-300">
        <strong class="text-gray-800 dark:text-gray-100">Default Tenant: </strong>
        <span>{{ status.data?.tenants?.[0]?.name || 'N/A' }}</span>
      </div>
      <div class="text-sm text-gray-700 dark:text-gray-300">
        <strong class="text-gray-800 dark:text-gray-100">Chunk Stores:</strong>
        <span>
          <template v-if="status.data?.chunkStores && status.data.chunkStores.length">
            <ul class="list-disc ml-5 mt-1">
              <li v-for="cs in status.data.chunkStores" :key="cs.id">{{ cs.name }} ({{ cs.type }})</li>
            </ul>
          </template>
          <template v-else>
            N/A
          </template>
        </span>
      </div>
      <div class="text-sm text-gray-700 dark:text-gray-300">
        <strong class="text-gray-800 dark:text-gray-100">Storage Classes:</strong>
        <span>
          <template v-if="status.data?.storageClasses && status.data.storageClasses.length">
            <ul class="list-disc ml-5 mt-1">
              <li v-for="sc in status.data.storageClasses" :key="sc.name">{{ sc.name }} ({{ sc.displayName }})</li>
            </ul>
          </template>
          <template v-else>
            N/A
          </template>
        </span>
      </div>
      <div class="text-sm text-gray-700 dark:text-gray-300">
        <strong class="text-gray-800 dark:text-gray-100">Storage Class Default Mappings:</strong>
        <span>
          <template v-if="status.data?.storageClassDefaultMappings && status.data.storageClassDefaultMappings.length">
            <ul class="list-disc ml-5 mt-1">
              <li v-for="m in status.data.storageClassDefaultMappings" :key="m.storageClassName">
                {{ m.storageClassName }} → {{ status.data?.chunkStores?.find(cs => cs.id === m.chunkStoreId)?.name || 'N/A' }} {{ m.isEnabled ? '(Enabled)' : '(Disabled)' }} {{ m.isDefault ? '(Default)' : '' }}
              </li>
            </ul>
          </template>
          <template v-else>
            N/A
          </template>
        </span>
      </div>
      <div v-if="status.data?.instanceAdmins && status.data?.instanceAdmins.length" class="text-sm text-gray-700 dark:text-gray-300">
        <strong class="text-gray-800 dark:text-gray-100">Instance Admin User(s):</strong>
        <span>
          <template v-if="status.data?.instanceAdmins.length">
            <ul class="list-disc ml-5 mt-1">
              <li v-for="admin in status.data.instanceAdmins" :key="admin.id">{{ admin.email }}</li>
            </ul>
          </template>
          <template v-else>
            N/A
          </template>
        </span>
      </div>
      <div v-if="status.data?.tenantAdmins && status.data?.tenantAdmins.length" class="text-sm text-gray-700 dark:text-gray-300">
        <strong class="text-gray-800 dark:text-gray-100">Tenant Admin User(s):</strong>
        <span>
          <template v-if="status.data?.tenantAdmins.length">
            <ul class="list-disc ml-5 mt-1">
              <li v-for="admin in status.data.tenantAdmins" :key="admin.id">{{ admin.email }}</li>
            </ul>
          </template>
          <template v-else>
            N/A
          </template>
        </span>
      </div>
    </div>
    <div class="flex justify-end mt-4">
      <button
        type="button"
        @click="onFinish"
        class="px-6 py-2 text-sm font-medium bg-violet-500 hover:bg-violet-600 text-white rounded-md cursor-pointer transition-colors"
      >
        Finish Setup
      </button>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { useSetupStore } from '@/features/setup/store/setup.store'
import { useRouter } from 'vue-router'
import { computed } from 'vue'
import { finishSetup } from '@/features/setup/api/setup.api'

const setupStore = useSetupStore()
const status = computed(() => setupStore.status as any || {})
const router = useRouter()

async function onFinish() {
  try {
    await finishSetup()
    await setupStore.fetchStatus()
  } catch (e: any) { 

  } finally {

  }
}
</script>