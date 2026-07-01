<template>
  <main class="min-h-dvh bg-canvas text-ink-strong">
    <div class="flex min-h-dvh justify-center">
      <div class="flex w-full max-w-3xl flex-col">
        <!-- Header -->
        <div class="flex h-16 items-center justify-between px-4 sm:px-6 lg:px-8">
          <router-link class="flex items-center gap-2.5" to="/">
            <span class="flex h-8 w-8 items-center justify-center rounded-[10px] bg-linear-to-br from-brand-from to-brand-to shadow-sm">
              <svg class="h-5 w-5 fill-white" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
                <path d="M31.956 14.8C31.372 6.92 25.08.628 17.2.044V5.76a9.04 9.04 0 0 0 9.04 9.04h5.716ZM14.8 26.24v5.716C6.92 31.372.63 25.08.044 17.2H5.76a9.04 9.04 0 0 1 9.04 9.04Zm11.44-9.04h5.716c-.584 7.88-6.876 14.172-14.756 14.756V26.24a9.04 9.04 0 0 1 9.04-9.04ZM.044 14.8C.63 6.92 6.92.628 14.8.044V5.76a9.04 9.04 0 0 1-9.04 9.04H.044Z" />
              </svg>
            </span>
            <span class="text-sm font-semibold tracking-tight text-ink-strong">BinStash</span>
          </router-link>
          <ThemeToggle />
        </div>

        <!-- Page content -->
        <div class="flex flex-1 items-start justify-center px-4 py-8 sm:px-6 lg:px-8">
          <router-view />
        </div>
      </div>
    </div>
  </main>
</template>

<script setup lang="ts">
import ThemeToggle from '@/components/ThemeToggle.vue'
</script>
