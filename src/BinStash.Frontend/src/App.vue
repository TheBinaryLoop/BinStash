<script setup lang="ts">
import { Toaster } from '@/components/ui/sonner'
import { useThemeStore } from '@/stores/theme'

// Instantiating the store applies the resolved theme class to <html>.
useThemeStore()
</script>

<template>
  <RouterView />
  <Toaster position="bottom-right" rich-colors close-button />
</template>
