<script setup lang="ts">
import { Button } from '@/components/ui/button'
</script>

<template>
  <div class="flex min-h-dvh flex-col items-center justify-center gap-6 px-6 text-center">
    <p class="text-muted-foreground font-mono text-sm">404</p>
    <div class="space-y-2">
      <h1 class="text-2xl font-semibold tracking-tight">Page not found</h1>
      <p class="text-muted-foreground text-sm">
        That page does not exist, or you do not have access to it.
      </p>
    </div>
    <Button variant="outline" @click="$router.push('/')">Back to BinStash</Button>
  </div>
</template>
