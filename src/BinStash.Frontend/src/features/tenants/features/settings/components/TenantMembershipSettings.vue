<template>
  <div class="space-y-4">
    <!-- Leave tenant (for members) -->
    <div
      v-if="!isAdmin"
      class="bg-white dark:bg-gray-800 rounded-xl border border-orange-200 dark:border-orange-800/50 shadow-sm overflow-hidden"
    >
      <div class="px-5 py-4 border-b border-orange-200 dark:border-orange-800/50">
        <h2 class="font-semibold text-orange-700 dark:text-orange-400">Leave Tenant</h2>
      </div>
      <div class="p-5 flex items-center justify-between gap-4">
        <p class="text-sm text-gray-600 dark:text-gray-400">
          You will lose access to all repositories in this tenant. This cannot be undone.
        </p>
        <button
          @click="showLeaveModal = true"
          class="shrink-0 btn border border-orange-200 dark:border-orange-800 text-orange-600 dark:text-orange-400 hover:bg-orange-50 dark:hover:bg-orange-900/20 text-sm"
        >
          Leave Tenant
        </button>
      </div>
    </div>

    <!-- Danger zone (admin only) -->
    <div
      v-if="isAdmin"
      class="bg-white dark:bg-gray-800 rounded-xl border border-red-200 dark:border-red-800/50 shadow-sm overflow-hidden"
    >
      <div class="px-5 py-4 border-b border-red-200 dark:border-red-800/50">
        <h2 class="font-semibold text-red-600 dark:text-red-400 flex items-center gap-2">
          <IconAlertTriangle class="w-4 h-4" />
          Danger Zone
        </h2>
      </div>
      <div class="p-5 space-y-4">
        <div v-if="!isInstanceAdmin" class="flex items-center justify-between gap-4">
          <div>
            <p class="text-sm font-medium text-gray-800 dark:text-gray-100">Leave Tenant</p>
            <p class="text-xs text-gray-500 dark:text-gray-400 mt-0.5">Remove yourself from this tenant. Another admin must be present.</p>
          </div>
          <button
            @click="showLeaveModal = true"
            class="shrink-0 btn border border-red-200 dark:border-red-800 text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20 text-sm"
          >
            Leave
          </button>
        </div>
      </div>
    </div>

    <Teleport to="body">
      <div v-if="showLeaveModal" class="fixed inset-0 z-50 flex items-center justify-center p-4">
        <div class="absolute inset-0 bg-black/40 backdrop-blur-sm" @click="showLeaveModal = false" />
        <div class="relative bg-white dark:bg-gray-800 rounded-xl shadow-xl w-full max-w-sm p-6 border border-gray-200 dark:border-gray-700">
          <h3 class="text-lg font-semibold text-gray-800 dark:text-gray-100 mb-2">Leave Tenant?</h3>
          <p class="text-sm text-gray-500 dark:text-gray-400">You will lose access to <span class="font-medium text-gray-700 dark:text-gray-200">{{ tenantName }}</span> and all its repositories.</p>
          <div v-if="leaveError" class="mt-3 text-sm text-red-500">{{ leaveError }}</div>
          <div class="mt-6 flex justify-end gap-3">
            <button @click="showLeaveModal = false" class="btn border border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700">Cancel</button>
            <button @click="doLeave" :disabled="leaving" class="btn bg-red-500 hover:bg-red-600 text-white disabled:opacity-50">
              {{ leaving ? 'Leaving…' : 'Leave' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { IconAlertTriangle } from '@tabler/icons-vue'
import { leaveTenant } from '@/api/tenants'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'

const router = useRouter()
const auth = useAuthStore()
const tenantStore = useTenantStore()

const tenant = computed(() => tenantStore.currentTenant)
const tenantName = computed(() => tenant.value?.name ?? 'Tenant')
const isInstanceAdmin = computed(() => auth.user?.roles?.includes('InstanceAdmin') ?? false)
const isAdmin = computed(() =>
  auth.user?.roles?.includes('InstanceAdmin') || auth.user?.roles?.includes('TenantAdmin'),
)

const showLeaveModal = ref(false)
const leaving = ref(false)
const leaveError = ref<string | null>(null)

async function doLeave() {
  leaving.value = true
  leaveError.value = null
  try {
    await leaveTenant()
    tenantStore.setCurrentTenant(null)
    router.push('/select-tenant')
  } catch (e: any) {
    leaveError.value = e?.message ?? 'Failed to leave tenant.'
  } finally {
    leaving.value = false
  }
}
</script>
