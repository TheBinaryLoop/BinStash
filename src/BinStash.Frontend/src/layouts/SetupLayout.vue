<script setup lang="ts">
import AppLogo from '@/components/app/AppLogo.vue'
import ThemeToggle from '@/components/app/ThemeToggle.vue'
</script>

<template>
  <div class="bg-background flex min-h-dvh flex-col">
    <header class="border-hairline flex items-center justify-between border-b px-6 py-4">
      <div class="flex items-center gap-2.5">
        <AppLogo :size="26" />
        <span class="text-sm font-semibold tracking-tight">BinStash setup</span>
      </div>
      <ThemeToggle />
    </header>

    <main class="flex flex-1 justify-center px-6 py-10">
      <div class="w-full max-w-4xl">
        <RouterView />
      </div>
    </main>
  </div>
</template>
