<template>
  <div class="flex h-dvh overflow-hidden bg-canvas text-ink-strong">
    <TenantSidebar :sidebar-open="sidebarOpen" @close-sidebar="sidebarOpen = false" />

    <div class="relative flex flex-1 flex-col overflow-hidden">
      <Header :sidebar-open="sidebarOpen" @toggle-sidebar="sidebarOpen = !sidebarOpen" />
      <main class="flex-1 overflow-y-auto">
        <div class="mx-auto w-full max-w-7xl px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
          <router-view />
        </div>
      </main>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import Header from '@/shared/components/navigation/Header.vue'
import TenantSidebar from '@/features/tenants/components/TenantSidebar.vue'

const sidebarOpen = ref(false)
</script>
