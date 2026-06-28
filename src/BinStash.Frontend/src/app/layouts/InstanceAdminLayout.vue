<template>
  <div class="flex h-dvh overflow-hidden bg-slate-100 dark:bg-[#090E1E] text-gray-900 dark:text-white transition-colors duration-200">
    <InstanceSidebar :sidebar-open="sidebarOpen" @close-sidebar="sidebarOpen = false" />

    <div class="relative flex flex-col flex-1 overflow-y-auto overflow-x-hidden">
      <main class="grow">
        <div class="px-3 py-3 sm:px-4 sm:py-4 lg:px-5 lg:py-5 w-full">
          <div class="min-h-[calc(100dvh-2.5rem)] overflow-hidden rounded-[28px] border border-slate-200/80 bg-white shadow-sm dark:border-slate-800 dark:bg-[#101935] dark:shadow-[0_24px_60px_rgba(0,0,0,0.28)] transition-colors duration-200">
            <Header :sidebar-open="sidebarOpen" @toggle-sidebar="sidebarOpen = !sidebarOpen" variant="tenant" />
            <div class="px-5 py-5 sm:px-6 lg:px-8 lg:py-8">
              <router-view />
            </div>
          </div>
        </div>
      </main>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import Header from '@/shared/components/navigation/Header.vue'
import InstanceSidebar from '@/features/instance/components/InstanceSidebar.vue'

const sidebarOpen = ref(false)
</script>