<template>
  <div class="space-y-4">
    <!-- Leave tenant (for members) -->
    <div
      v-if="!isAdmin"
      class="overflow-hidden rounded-card border border-warning/25 bg-card"
    >
      <div class="border-b border-warning/25 px-5 py-4">
        <h2 class="font-semibold text-warning">Leave Tenant</h2>
      </div>
      <div class="flex items-center justify-between gap-4 p-5">
        <p class="text-sm text-ink-muted">
          You will lose access to all repositories in this tenant. This cannot be undone.
        </p>
        <BaseButton variant="secondary" size="sm" class="shrink-0 text-warning" @click="showLeaveModal = true">
          Leave Tenant
        </BaseButton>
      </div>
    </div>

    <!-- Danger zone (admin only) -->
    <div
      v-if="isAdmin"
      class="overflow-hidden rounded-card border border-danger/25 bg-card"
    >
      <div class="border-b border-danger/25 px-5 py-4">
        <h2 class="flex items-center gap-2 font-semibold text-danger">
          <IconAlertTriangle class="h-4 w-4" />
          Danger Zone
        </h2>
      </div>
      <div class="space-y-4 p-5">
        <div v-if="!isInstanceAdmin" class="flex items-center justify-between gap-4">
          <div>
            <p class="text-sm font-medium text-ink-strong">Leave Tenant</p>
            <p class="mt-0.5 text-xs text-ink-muted">Remove yourself from this tenant. Another admin must be present.</p>
          </div>
          <BaseButton variant="secondary" size="sm" class="shrink-0 text-danger" @click="showLeaveModal = true">
            Leave
          </BaseButton>
        </div>
      </div>
    </div>

    <BaseModal v-model:open="showLeaveModal" title="Leave Tenant?" size="sm">
      <div class="space-y-3">
        <p class="text-sm text-ink-muted">
          You will lose access to <span class="font-medium text-ink-strong">{{ tenantName }}</span> and all its repositories.
        </p>
        <div v-if="leaveError" class="text-sm text-danger">{{ leaveError }}</div>
      </div>
      <template #footer>
        <BaseButton variant="secondary" @click="showLeaveModal = false">Cancel</BaseButton>
        <BaseButton variant="danger" :loading="leaving" @click="doLeave">{{ leaving ? 'Leaving…' : 'Leave' }}</BaseButton>
      </template>
    </BaseModal>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { IconAlertTriangle } from '@tabler/icons-vue'
import { leaveTenant } from '@/api/tenants'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'
import { BaseButton, BaseModal } from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'

const toast = useToast()

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
    toast.success('You left the tenant')
    tenantStore.setCurrentTenant(null)
    router.push('/select-tenant')
  } catch (e: any) {
    leaveError.value = e?.message ?? 'Failed to leave tenant.'
    toast.error(leaveError.value)
  } finally {
    leaving.value = false
  }
}
</script>
