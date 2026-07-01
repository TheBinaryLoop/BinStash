<template>
  <div class="flex flex-col gap-4">
    <h2 class="mb-2 text-xl font-bold text-ink-strong">Step 7: Review Setup</h2>
    <p class="mb-4 text-sm text-ink-muted">Please review your configuration before finishing the setup. If you need to make changes, use the Reset button.</p>
    <div class="flex flex-col gap-2 rounded-card border border-hairline bg-raised p-4">
      <div class="text-sm text-ink-muted">
        <strong class="text-ink-strong">Tenancy Mode: </strong>
        <span>{{ status.data?.tenancyMode || 'N/A' }}</span>
      </div>
      <div v-if="status.data?.tenancyMode === 'Single'" class="text-sm text-ink-muted">
        <strong class="text-ink-strong">Default Tenant: </strong>
        <span>{{ status.data?.tenants?.[0]?.name || 'N/A' }}</span>
      </div>
      <div class="text-sm text-ink-muted">
        <strong class="text-ink-strong">Chunk Stores:</strong>
        <span>
          <template v-if="status.data?.chunkStores && status.data.chunkStores.length">
            <ul class="ml-5 mt-1 list-disc">
              <li v-for="cs in status.data.chunkStores" :key="cs.id">{{ cs.name }} ({{ cs.type }})</li>
            </ul>
          </template>
          <template v-else>
            N/A
          </template>
        </span>
      </div>
      <div class="text-sm text-ink-muted">
        <strong class="text-ink-strong">Storage Classes:</strong>
        <span>
          <template v-if="status.data?.storageClasses && status.data.storageClasses.length">
            <ul class="ml-5 mt-1 list-disc">
              <li v-for="sc in status.data.storageClasses" :key="sc.name">{{ sc.name }} ({{ sc.displayName }})</li>
            </ul>
          </template>
          <template v-else>
            N/A
          </template>
        </span>
      </div>
      <div class="text-sm text-ink-muted">
        <strong class="text-ink-strong">Storage Class Default Mappings:</strong>
        <span>
          <template v-if="status.data?.storageClassDefaultMappings && status.data.storageClassDefaultMappings.length">
            <ul class="ml-5 mt-1 list-disc">
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
      <div v-if="status.data?.instanceAdmins && status.data?.instanceAdmins.length" class="text-sm text-ink-muted">
        <strong class="text-ink-strong">Instance Admin User(s):</strong>
        <span>
          <template v-if="status.data?.instanceAdmins.length">
            <ul class="ml-5 mt-1 list-disc">
              <li v-for="admin in status.data.instanceAdmins" :key="admin.id">{{ admin.email }}</li>
            </ul>
          </template>
          <template v-else>
            N/A
          </template>
        </span>
      </div>
      <div v-if="status.data?.tenantAdmins && status.data?.tenantAdmins.length" class="text-sm text-ink-muted">
        <strong class="text-ink-strong">Tenant Admin User(s):</strong>
        <span>
          <template v-if="status.data?.tenantAdmins.length">
            <ul class="ml-5 mt-1 list-disc">
              <li v-for="admin in status.data.tenantAdmins" :key="admin.id">{{ admin.email }}</li>
            </ul>
          </template>
          <template v-else>
            N/A
          </template>
        </span>
      </div>
    </div>
    <div class="mt-4 flex justify-end">
      <BaseButton type="button" @click="onFinish">
        Finish Setup
      </BaseButton>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { useSetupStore } from '@/features/setup/store/setup.store'
import { useRouter } from 'vue-router'
import { computed } from 'vue'
import { finishSetup } from '@/features/setup/api/setup.api'
import { BaseButton } from '@/shared/components/ui'

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
