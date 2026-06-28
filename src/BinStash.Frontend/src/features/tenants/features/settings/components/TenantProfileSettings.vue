<template>
  <div
    class="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700/60 shadow-sm overflow-hidden"
  >
    <div class="px-5 py-4 border-b border-gray-200 dark:border-gray-700/60">
      <h2 class="font-semibold text-gray-800 dark:text-gray-100">Tenant Profile</h2>
    </div>

    <div class="p-5 space-y-4">
      <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div>
          <p class="text-xs font-medium uppercase text-gray-400 dark:text-gray-500 mb-1">Tenant Name</p>
          <p class="text-sm font-medium text-gray-800 dark:text-gray-100">{{ tenant?.name ?? '—' }}</p>
        </div>

        <div>
          <p class="text-xs font-medium uppercase text-gray-400 dark:text-gray-500 mb-1">Slug</p>
          <p class="text-sm font-mono text-gray-700 dark:text-gray-300">{{ tenant?.slug ?? '—' }}</p>
        </div>

        <div>
          <p class="text-xs font-medium uppercase text-gray-400 dark:text-gray-500 mb-1">Tenant ID</p>
          <p class="text-xs font-mono text-gray-500 dark:text-gray-400 truncate">{{ tenantId }}</p>
        </div>

        <div>
          <p class="text-xs font-medium uppercase text-gray-400 dark:text-gray-500 mb-1">Your Role</p>
          <div class="flex flex-wrap gap-1">
            <span
              v-for="r in userRoles"
              :key="r"
              class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-violet-100 text-violet-700 dark:bg-violet-900/30 dark:text-violet-400"
            >
              {{ r }}
            </span>
            <span
              v-if="userRoles.length === 0"
              class="text-xs text-gray-400 dark:text-gray-500"
            >
              —
            </span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'

const route = useRoute()
const auth = useAuthStore()
const tenantStore = useTenantStore()

const tenantId = computed(() => route.params.tenantId as string)
const tenant = computed(() => tenantStore.currentTenant)
const userRoles = computed(() =>
  (auth.user?.roles ?? []).filter(r => r === 'TenantAdmin' || r === 'TenantMember' || r === 'InstanceAdmin'),
)
</script>
