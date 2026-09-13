<script setup lang="ts">
import { Toaster } from '@/components/ui/sonner'
import { TooltipProvider } from '@/components/ui/tooltip'
import { useThemeStore } from '@/stores/theme'

// Instantiating the store applies the resolved theme class to <html>.
useThemeStore()
</script>

<template>
  <!--
    reka-ui's TooltipRoot requires a TooltipProvider ancestor. Without one it does not
    error — the trigger's children simply never render, which looks like a data bug (an
    empty table cell) rather than a missing provider. Mounted once at the root so every
    tooltip in the app is covered.
  -->
  <TooltipProvider :delay-duration="200">
    <RouterView />
  </TooltipProvider>
  <Toaster position="bottom-right" rich-colors close-button />
</template>
