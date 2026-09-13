<script setup lang="ts">
import AppLogo from '@/components/app/AppLogo.vue'
import ThemeToggle from '@/components/app/ThemeToggle.vue'
</script>

<template>
  <div class="bg-background relative flex min-h-dvh flex-col">
    <!-- A single soft wash keeps the auth screens from reading as a bare form. -->
    <div
      class="pointer-events-none absolute inset-x-0 top-0 h-80 bg-gradient-to-b from-primary/[0.07] to-transparent"
      aria-hidden="true"
    />

    <header class="relative flex items-center justify-between px-6 py-5">
      <RouterLink :to="{ name: 'sign-in' }" class="flex items-center gap-2.5">
        <AppLogo :size="26" />
        <span class="text-sm font-semibold tracking-tight">BinStash</span>
      </RouterLink>
      <ThemeToggle />
    </header>

    <main class="relative flex flex-1 items-center justify-center px-6 pb-20">
      <div class="w-full max-w-[26rem]">
        <RouterView />
      </div>
    </main>
  </div>
</template>
