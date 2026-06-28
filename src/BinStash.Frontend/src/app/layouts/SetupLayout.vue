<template>
  <main class="bg-white dark:bg-gray-900 min-h-dvh">
    <div class="flex justify-center items-center min-h-dvh">
      <div class="w-full max-w-2xl flex flex-col min-h-dvh">
        <div class="flex-1 flex flex-col after:flex-1">
          <!-- Header -->
          <div class="flex items-center justify-between h-16 px-4 sm:px-6 lg:px-8">
            <router-link class="block" to="/">
              <svg
                class="fill-violet-500"
                xmlns="http://www.w3.org/2000/svg"
                width="32"
                height="32"
              >
                <path
                  d="M31.956 14.8C31.372 6.92 25.08.628 17.2.044V5.76a9.04 9.04 0 0 0 9.04 9.04h5.716ZM14.8 26.24v5.716C6.92 31.372.63 25.08.044 17.2H5.76a9.04 9.04 0 0 1 9.04 9.04Zm11.44-9.04h5.716c-.584 7.88-6.876 14.172-14.756 14.756V26.24a9.04 9.04 0 0 1 9.04-9.04ZM.044 14.8C.63 6.92 6.92.628 14.8.044V5.76a9.04 9.04 0 0 1-9.04 9.04H.044Z"
                />
              </svg>
            </router-link>
            <ThemeToggle />
          </div>

          <!-- Page content -->
          <div class="flex-1">
            <router-view />
          </div>
        </div>
      </div>
    </div>
  </main>
</template>

<script setup lang="ts">
import ThemeToggle from '@/components/ThemeToggle.vue'
</script>