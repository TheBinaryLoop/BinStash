<template>
  <SidebarShell :sidebar-open="sidebarOpen" :home-to="`/t/${tenantId}`" @close-sidebar="emit('close-sidebar')">
    <template #badge>
      <SidebarBadge :label="tenantName" tone="success" :icon="IconCircleCheck" />
    </template>

    <SidebarSection title="Workspace">
      <SidebarItem :to="`/t/${tenantId}`" :icon="IconLayoutDashboard" label="Overview" exact />
      <SidebarItem :to="`/t/${tenantId}/repositories`" :icon="IconGitBranch" label="Repositories" />
    </SidebarSection>

    <SidebarSection v-if="isTenantAdmin" title="Administration">
      <SidebarItem :to="`/t/${tenantId}/members`" :icon="IconUsers" label="Members" />
      <SidebarItem :to="`/t/${tenantId}/service-accounts`" :icon="IconRobot" label="Service Accounts" />
    </SidebarSection>

    <SidebarSection title="Account">
      <SidebarItem :to="`/t/${tenantId}/settings`" :icon="IconAdjustmentsHorizontal" label="Settings" />
    </SidebarSection>

    <template v-if="isInstanceAdmin" #bottom>
      <router-link
        to="/instance"
        class="group flex items-center gap-3 rounded-control px-3 py-2 text-sm font-medium text-ink-muted transition hover:bg-raised hover:text-ink-strong lg:justify-center lg:sidebar-expanded:justify-start"
      >
        <IconArrowLeft class="h-5 w-5 shrink-0 text-ink-subtle transition group-hover:text-accent" />
        <span class="whitespace-nowrap lg:hidden lg:sidebar-expanded:inline">Back to Instance</span>
      </router-link>
    </template>
  </SidebarShell>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useTenantStore } from '@/stores/tenant'
import { useAuthStore } from '@/stores/auth'
import SidebarShell from '@/shared/components/navigation/SidebarShell.vue'
import SidebarSection from '@/shared/components/navigation/SidebarSection.vue'
import SidebarItem from '@/shared/components/navigation/SidebarItem.vue'
import SidebarBadge from '@/shared/components/navigation/SidebarBadge.vue'
import {
  IconLayoutDashboard,
  IconGitBranch,
  IconUsers,
  IconRobot,
  IconAdjustmentsHorizontal,
  IconArrowLeft,
  IconCircleCheck,
} from '@tabler/icons-vue'

defineProps<{ sidebarOpen: boolean }>()
const emit = defineEmits<{ (e: 'close-sidebar'): void }>()

const route = useRoute()
const tenantStore = useTenantStore()
const authStore = useAuthStore()

const tenantId = computed(() => (route.params.tenantId as string) ?? tenantStore.currentTenantId ?? '')
const tenantName = computed(
  () => tenantStore.tenants.find((t) => t.tenantId === tenantId.value)?.name ?? 'Tenant',
)

const isTenantAdmin = computed(() => {
  const tenantRole = tenantStore.currentTenant?.role
  const globalRoles = authStore.user?.roles ?? []
  return tenantRole === 'TenantAdmin' || globalRoles.includes('InstanceAdmin')
})

const isInstanceAdmin = computed(() => (authStore.user?.roles ?? []).includes('InstanceAdmin'))
</script>
