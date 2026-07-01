<template>
  <SidebarShell :sidebar-open="sidebarOpen" home-to="/instance" @close-sidebar="emit('close-sidebar')">
    <template #badge>
      <SidebarBadge label="Instance Admin" tone="accent" :icon="IconShieldHalfFilled" />
    </template>

    <SidebarSection title="Instance">
      <SidebarItem to="/instance" :icon="IconLayoutDashboard" label="Overview" exact />
      <SidebarItem to="/instance/tenants" :icon="IconBuildingSkyscraper" label="Tenants" />
      <SidebarItem to="/instance/users" :icon="IconUsers" label="Users" />
    </SidebarSection>

    <SidebarSection title="Storage">
      <SidebarItem to="/chunk-stores" :icon="IconDatabase" label="Chunk Stores" />
    </SidebarSection>

    <SidebarSection title="Account">
      <SidebarItem to="/instance/settings" :icon="IconAdjustmentsHorizontal" label="Settings" />
    </SidebarSection>
  </SidebarShell>
</template>

<script setup lang="ts">
import SidebarShell from '@/shared/components/navigation/SidebarShell.vue'
import SidebarSection from '@/shared/components/navigation/SidebarSection.vue'
import SidebarItem from '@/shared/components/navigation/SidebarItem.vue'
import SidebarBadge from '@/shared/components/navigation/SidebarBadge.vue'
import {
  IconLayoutDashboard,
  IconBuildingSkyscraper,
  IconUsers,
  IconDatabase,
  IconAdjustmentsHorizontal,
  IconShieldHalfFilled,
} from '@tabler/icons-vue'

defineProps<{ sidebarOpen: boolean }>()
const emit = defineEmits<{ (e: 'close-sidebar'): void }>()
</script>
